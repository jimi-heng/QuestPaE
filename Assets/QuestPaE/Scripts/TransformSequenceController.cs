using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class TransformOffset // 偏移途径点
{
    [Header("在运动前进方向上施加偏移点的影响, Offset Ratio 为偏移点对起始点的影响权重")]
    public Vector3 localPosition;
    public Vector3 localEulerRotation;
    public Vector3 localScale = Vector3.one;
}

[Serializable]
public class TransformStep // 保持原名，兼容旧数据
{
    public Vector3 localPosition;
    public Vector3 localEulerRotation;
    public Vector3 localScale = Vector3.one;
}

public class TransformSequenceController : MonoBehaviour
{
    #region ======== Inspector (from startPoint to endPoint) ========
    [Tooltip("目标物体，不填默认自己")]
    public Transform obj;

    [Tooltip("起点")]
    public TransformStep startPoint;
    [Tooltip("终点")]
    public TransformStep endPoint;

    [Tooltip("偏移途径点")]
    public TransformOffset stepOffset;

    [Range(0f, 1f), Tooltip("偏移点距离起点的比例（0=起点  1=终点）")]
    public float offsetRatio = 0.5f;

    [Min(0.01f), Tooltip("总时长")]
    public float duration = 1f;

    [Tooltip("速度曲线（X 会被归一化到 0-真实路程）")]
    public AnimationCurve speedCurve = AnimationCurve.Linear(0, 1, 1, 1);

    public UnityEvent onSequenceFinish;

    [SerializeField, Range(10, 500)]
    public int samples = 200;

    public enum MovementMode { AutoAnimation, PathAnimation } // 在类定义前添加这个枚举
    public enum PointSourceType { Manual, Anchor }  // 路径点来源类型
    [Header("路径点来源")]
    public PointSourceType pointSourceType = PointSourceType.Manual;  // 默认手动模式

    [Header("锚点设置")]
    public Transform anchor; // 拖入包含所有路径点的父物体               
    private bool _hasLoadedAnchorPoints = false; // 运行时是否已经从 Anchor 加载过路径点

    [Header("路径点列表")]
    public List<TransformStep> listPoint = new List<TransformStep>();  // 存储多个路径点
    public int PointCount => listPoint.Count; // 只读属性，显示路径点数量

    [Header("运动模式")]
    public MovementMode movementMode = MovementMode.AutoAnimation;

    [Header("PathAnimation模式设置")]
    [Tooltip("当前已爬升的总距离")]
    public float currentClimbedDistance = 0f;
    [Tooltip("下一步爬升的距离")]
    public float nextClimbDistance = 1f;

    public enum PathControlType { TwoPoint, MultiPoint }  // 路径控制类型：无控制 或 多点控制
    [Header("路径控制模式")]
    public PathControlType pathControlType = PathControlType.TwoPoint;  // 使用下拉框选择控制类型

    public enum LineType { Polyline, Bezier }   // 路径类型：折线或贝塞尔曲线
    [Header("路径类型（单条路径）")]
    public LineType lineType = LineType.Polyline;  // 使用下拉框选择折线或贝塞尔曲线

    [Header("调试设置")]
    [Tooltip("启用调试日志输出")]
    public bool enableDebug = true;

    [Tooltip("调试日志输出间隔（秒）")]
    public float debugInterval = 0.1f;

    [Tooltip("在Scene视图中绘制路径")]
    public bool drawPathInScene = true;

    [Tooltip("路径颜色")]
    public Color pathColor = Color.green;
    #endregion

    #region ======== Runtime Only ========
    public float RealLength => _realLength;
    private int _lastParameterHash = 0;
    public bool IsPlaying => _isPlaying;
    public bool IsPathAnimating => _isPathAnimating;
    public float PathLength => _realLength;
    private float _targetClimbedDistance;      // 目标爬升距离
    private bool _isPathAnimating;             // 是否正在PathAnimation中
    private float _pathAnimationTimer;         // PathAnimation计时器
    private float _realLength;          // 新路径总长度
    private float _totalTime;           // 积分后总时间
    private float[] _distanceToTime;    // 路程->归一化时间
    private float _timer;
    private bool _isPlaying;
    private AnimationCurve _runtimeSpeedCurve; // 运行时曲线变量
    private float _debugTimer;
    private List<Vector3> _debugPositions = new List<Vector3>();
    private float _currentSegmentDuration; // 当前爬升段的持续时间
    private float _currentSegmentLength;   // 当前爬升段的长度
    private AnimationCurve _currentSegmentSpeedCurve; // 当前段的速度曲线
    private struct StepSegment // 每一步的队列信息
    {
        public float distance;   // 本步希望爬升的路径距离
        public float duration;   // 本步期望的持续时间
    }

    private readonly Queue<StepSegment> _stepQueue = new Queue<StepSegment>();
    #endregion

    #region ======== 构造新路径 ========
    /* 根据当前 offsetRatio / lineType 生成 3 个关键帧：
       listStep[0] = start
       listStep[1] = offsetPoint
       listStep[2] = end
       同时计算真实路程 _realLength
    */
    [System.NonSerialized] private TransformStep[] listStep;

    private void BuildPath()
    {
        if (pathControlType == PathControlType.MultiPoint)
        {
            // 如果是Anchor模式且anchor不为空，自动更新路径点
            if (pointSourceType == PointSourceType.Anchor && anchor != null)
            {
                UpdatePointsFromAnchor();
            }

            // 对于MultiPoint模式，我们只需要存储原始点
            if (listPoint.Count < 2)
            {
                Debug.LogError("MultiPoint mode requires at least 2 points");
                return;
            }

            // 直接使用用户输入的点，不插入任何偏移点
            listStep = new TransformStep[listPoint.Count];
            for (int i = 0; i < listPoint.Count; i++)
            {
                listStep[i] = listPoint[i];
            }

            // 计算总长度
            if (lineType == LineType.Polyline)
            {
                // 折线：基于原始点之间的直线距离
                _realLength = 0f;
                for (int i = 0; i < listStep.Length - 1; i++)
                {
                    _realLength += Vector3.Distance(listStep[i].localPosition, listStep[i + 1].localPosition);
                }
            }
            else
            {
                // 样条曲线：计算完整样条曲线的近似长度
                _realLength = CalculateSplineLength(listStep);
            }

            // 调试输出
            if (enableDebug)
            {
                Debug.Log($"BuildPath - MultiPoint模式: 共{listStep.Length}个点, 总长度: {_realLength}, 线型: {lineType}");
                for (int i = 0; i < listStep.Length; i++)
                {
                    Debug.Log($"点 {i}: {listStep[i].localPosition}");
                }
            }
        }
        else
        {
            // 保持原有的TwoPoint模式逻辑不变
            listStep = new TransformStep[3];
            listStep[0] = startPoint;
            listStep[2] = endPoint;

            /* --------------- 1. 建立"路径坐标系" --------------- */
            Vector3 pathStart = startPoint.localPosition;
            Vector3 pathEnd = endPoint.localPosition;
            Vector3 pathZ = (pathEnd - pathStart).normalized;
            Vector3 pathX = Vector3.Cross(Vector3.up, pathZ).normalized;
            if (pathX == Vector3.zero) pathX = Vector3.Cross(Vector3.forward, pathZ).normalized;
            Vector3 pathY = Vector3.Cross(pathZ, pathX).normalized;
            Matrix4x4 pathMatrix = Matrix4x4.TRS(Vector3.zero,
                                                 Quaternion.LookRotation(pathZ, pathY),
                                                 Vector3.one);

            /* --------------- 2. 把 stepOffset 转到局部坐标系 --------------- */
            Vector3 localOffsetPos = pathMatrix.MultiplyPoint3x4(stepOffset.localPosition);
            Vector3 localOffsetRotEuler = stepOffset.localEulerRotation;

            /* --------------- 3. 计算途径点（绝对值） --------------- */
            Vector3 offsetPos = Vector3.Lerp(pathStart, pathEnd, offsetRatio) + localOffsetPos;

            Quaternion baseRot = Quaternion.Lerp(Quaternion.Euler(startPoint.localEulerRotation),
                                                 Quaternion.Euler(endPoint.localEulerRotation),
                                                 offsetRatio);
            Quaternion localOffsetRot = Quaternion.Euler(localOffsetRotEuler);
            Quaternion offsetRot = baseRot * localOffsetRot;

            listStep[1] = new TransformStep
            {
                localPosition = offsetPos,
                localEulerRotation = offsetRot.eulerAngles,
                localScale = stepOffset.localScale
            };

            // 计算路径长度
            if (lineType == LineType.Polyline)
            {
                _realLength = Vector3.Distance(listStep[0].localPosition, listStep[1].localPosition) +
                             Vector3.Distance(listStep[1].localPosition, listStep[2].localPosition);
            }
            else
            {
                // 三阶贝塞尔，用 20 段近似
                const int SEG = 20;
                Vector3 p0 = listStep[0].localPosition;
                Vector3 p1 = listStep[1].localPosition;
                Vector3 p2 = listStep[2].localPosition;
                float len = 0;
                Vector3 prev = p0;
                for (int i = 1; i <= SEG; i++)
                {
                    float t = i / (float)SEG;
                    Vector3 curr = Mathf.Pow(1 - t, 2) * p0 +
                                  2 * (1 - t) * t * p1 +
                                  Mathf.Pow(t, 2) * p2;
                    len += Vector3.Distance(prev, curr);
                    prev = curr;
                }
                _realLength = len;
            }
        }
    }

    // 计算完整贝塞尔曲线的长度（用于TwoPoint模式）
    private float CalculateBezierLength(TransformStep[] points)
    {
        if (points.Length < 2) return 0f;

        const int SEG = 50;
        float length = 0f;
        Vector3 prev = points[0].localPosition;

        for (int i = 1; i <= SEG; i++)
        {
            float t = i / (float)SEG;
            Vector3 curr = CalculateBezierPoint(t, points);
            length += Vector3.Distance(prev, curr);
            prev = curr;
        }

        return length;
    }

    // 计算样条曲线上的点（Catmull-Rom样条，会经过所有控制点）
    private Vector3 CalculateSplinePoint(float t, TransformStep[] points)
    {
        if (points.Length == 0) return Vector3.zero;
        if (points.Length == 1) return points[0].localPosition;

        // 将全局t转换为段索引和段内t
        int segmentCount = points.Length - 1;
        float exactSegmentIndex = t * segmentCount;
        int segmentIndex = Mathf.FloorToInt(exactSegmentIndex);
        float segmentT = exactSegmentIndex - segmentIndex;

        // 确保不越界
        segmentIndex = Mathf.Clamp(segmentIndex, 0, segmentCount - 1);
        segmentT = Mathf.Clamp01(segmentT);

        // 获取当前段的四个控制点（用于Catmull-Rom样条）
        Vector3 p0, p1, p2, p3;

        if (segmentIndex == 0)
        {
            // 第一段：使用虚拟的前一个点
            p0 = points[0].localPosition;
            p1 = points[0].localPosition;
            p2 = points[1].localPosition;
            p3 = points[Mathf.Min(2, points.Length - 1)].localPosition;
        }
        else if (segmentIndex == segmentCount - 1)
        {
            // 最后一段：使用虚拟的后一个点
            p0 = points[Mathf.Max(segmentIndex - 1, 0)].localPosition;
            p1 = points[segmentIndex].localPosition;
            p2 = points[segmentIndex + 1].localPosition;
            p3 = points[segmentIndex + 1].localPosition;
        }
        else
        {
            // 中间段：使用真实的四个点
            p0 = points[segmentIndex - 1].localPosition;
            p1 = points[segmentIndex].localPosition;
            p2 = points[segmentIndex + 1].localPosition;
            p3 = points[Mathf.Min(segmentIndex + 2, points.Length - 1)].localPosition;
        }

        // 计算基础样条点
        Vector3 basePoint = CalculateCatmullRomPoint(segmentT, p0, p1, p2, p3);

        // 应用stepOffset的位置偏移
        if (stepOffset.localPosition != Vector3.zero)
        {
            Vector3 offset = ApplyPositionOffset(segmentIndex, segmentT, basePoint, p1, p2);
            basePoint += offset;
        }

        return basePoint;
    }

    // 应用位置偏移
    private Vector3 ApplyPositionOffset(int segmentIndex, float segmentT, Vector3 basePoint, Vector3 segmentStart, Vector3 segmentEnd)
    {
        // 建立当前线段的路径坐标系
        Vector3 pathZ = (segmentEnd - segmentStart).normalized;
        Vector3 pathX = Vector3.Cross(Vector3.up, pathZ).normalized;
        if (pathX == Vector3.zero) pathX = Vector3.Cross(Vector3.forward, pathZ).normalized;
        Vector3 pathY = Vector3.Cross(pathZ, pathX).normalized;

        Matrix4x4 pathMatrix = Matrix4x4.TRS(Vector3.zero,
                                             Quaternion.LookRotation(pathZ, pathY),
                                             Vector3.one);

        // 将stepOffset的localPosition转换到世界坐标系
        Vector3 worldOffset = pathMatrix.MultiplyPoint3x4(stepOffset.localPosition);

        // 根据offsetRatio调整偏移强度
        float offsetStrength = CalculateOffsetStrength(segmentT);

        return worldOffset * offsetStrength;
    }

    // Catmull-Rom样条计算
    private Vector3 CalculateCatmullRomPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        // Catmull-Rom样条公式
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2 * p1) +
            (-p0 + p2) * t +
            (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
            (-p0 + 3 * p1 - 3 * p2 + p3) * t3
        );
    }

    // 计算样条曲线长度
    private float CalculateSplineLength(TransformStep[] points)
    {
        if (points.Length < 2) return 0f;

        const int SEG = 50;
        float length = 0f;
        Vector3 prev = CalculateSplinePoint(0, points);

        for (int i = 1; i <= SEG; i++)
        {
            float t = i / (float)SEG;
            Vector3 curr = CalculateSplinePoint(t, points);
            length += Vector3.Distance(prev, curr);
            prev = curr;
        }

        return length;
    }

    // 保留原有的贝塞尔曲线计算方法（用于TwoPoint模式）
    private Vector3 CalculateBezierPoint(float t, TransformStep[] points)
    {
        if (points.Length == 0) return Vector3.zero;
        if (points.Length == 1) return points[0].localPosition;

        // 使用德卡斯特里奥算法计算贝塞尔曲线点
        Vector3[] temp = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            temp[i] = points[i].localPosition;
        }

        for (int level = points.Length - 1; level > 0; level--)
        {
            for (int i = 0; i < level; i++)
            {
                temp[i] = Vector3.Lerp(temp[i], temp[i + 1], t);
            }
        }

        return temp[0];
    }
    #endregion

    #region ======== 速度曲线归一化（复用原逻辑） ========
    private void NormalizeCurve()
    {
        if (speedCurve == null || speedCurve.length == 0)
        {
            _runtimeSpeedCurve = AnimationCurve.Linear(0, 1, 1, 1);
            return;
        }

        // 复制原始曲线
        Keyframe[] keys = new Keyframe[speedCurve.length];
        for (int i = 0; i < speedCurve.length; i++)
        {
            keys[i] = new Keyframe(
                speedCurve[i].time,
                speedCurve[i].value,
                speedCurve[i].inTangent,
                speedCurve[i].outTangent
            );
        }

        _runtimeSpeedCurve = new AnimationCurve(keys);

        float originalLength = _runtimeSpeedCurve.keys[_runtimeSpeedCurve.length - 1].time;
        float scaleFactor = _realLength / Mathf.Max(originalLength, 0.001f);

        Keyframe[] newKeys = new Keyframe[_runtimeSpeedCurve.length];
        for (int i = 0; i < _runtimeSpeedCurve.length; i++)
        {
            float newTime = _runtimeSpeedCurve.keys[i].time * scaleFactor;
            float newInTangent = _runtimeSpeedCurve.keys[i].inTangent / scaleFactor;
            float newOutTangent = _runtimeSpeedCurve.keys[i].outTangent / scaleFactor;

            newKeys[i] = new Keyframe(newTime, _runtimeSpeedCurve.keys[i].value, newInTangent, newOutTangent);
        }

        _runtimeSpeedCurve = new AnimationCurve(newKeys);
    }
    #endregion

    #region ======== 生命周期 ========
    private void Awake() { if (obj == null) obj = transform; }

    private void Start()
    {
        if (obj == null) obj = transform;

        // 初始化参数跟踪
        _lastStepOffset = new TransformOffset
        {
            localPosition = stepOffset.localPosition,
            localEulerRotation = stepOffset.localEulerRotation,
            localScale = stepOffset.localScale
        };
        _lastOffsetRatio = offsetRatio;

        BuildPath();
        NormalizeCurve();
        PrecomputeDistanceToTime();
        ResetToStart();
        _isPlaying = true;
    }

    private void Update()
    {
        // 检查参数是否发生变化
        CheckParameterChanges();

        if (!_isPlaying) return;

        // 根据运动模式选择不同的更新逻辑
        if (movementMode == MovementMode.AutoAnimation)
        {
            UpdateAutoAnimation();
        }
        else if (movementMode == MovementMode.PathAnimation)
        {
            UpdatePathAnimation();
        }
    }

    // 检查参数变化并清除轨迹
    private void CheckParameterChanges()
    {
        // 计算当前参数的哈希值
        int currentHash = CalculateParameterHash();

        if (currentHash != _lastParameterHash)
        {
            // 参数发生变化，清除调试轨迹
            ResetDebugTrail();
            _lastParameterHash = currentHash;

            if (enableDebug)
            {
                Debug.Log("参数发生变化，清除调试轨迹");
            }
        }
    }

    // 计算参数哈希值
    private int CalculateParameterHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + movementMode.GetHashCode();
            hash = hash * 31 + pathControlType.GetHashCode();
            hash = hash * 31 + lineType.GetHashCode();
            hash = hash * 31 + pointSourceType.GetHashCode();
            hash = hash * 31 + anchor?.GetHashCode() ?? 0;
            hash = hash * 31 + listPoint.Count;
            return hash;
        }
    }

    private void UpdateAutoAnimation()
    {
        _timer += Time.deltaTime;
        float tNorm = Mathf.Clamp01(_timer / duration);
        float distWorld = GetDistanceAtTime(tNorm);

        float u01 = distWorld / _realLength;
        SamplePath(u01, out Vector3 pos, out Quaternion rot, out Vector3 scale);
        obj.localPosition = pos;
        obj.localRotation = rot;
        obj.localScale = scale;

        // 记录调试位置 - 限制数量防止内存溢出
        if (drawPathInScene)
        {
            RecordDebugPosition(pos, u01);
        }

        // 调试输出 - 添加stepOffset信息
        if (enableDebug)
        {
            _debugTimer += Time.deltaTime;
            if (_debugTimer >= debugInterval)
            {
                // 计算当前偏移强度
                int segmentCount = listStep.Length - 1;
                float exactSegmentIndex = u01 * segmentCount;
                int segmentIndex = Mathf.FloorToInt(exactSegmentIndex);
                float segmentT = exactSegmentIndex - segmentIndex;
                float offsetStrength = CalculateOffsetStrength(segmentT);

                Debug.Log($"Time: {_timer:F2}, u01: {u01:F3}, Position: {pos}, Rotation: {rot.eulerAngles}, Scale: {scale}, OffsetStrength: {offsetStrength:F3}");
                _debugTimer = 0f;
            }
        }

        if (_timer >= duration)
        {
            _isPlaying = false;
            var last = listStep[listStep.Length - 1];
            obj.localPosition = last.localPosition;
            obj.localRotation = Quaternion.Euler(last.localEulerRotation);
            obj.localScale = last.localScale;

            // 最终调试输出
            if (enableDebug)
            {
                Debug.Log($"Sequence finished at Position: {last.localPosition}, Rotation: {last.localEulerRotation}, Scale: {last.localScale}");
            }

            onSequenceFinish?.Invoke();
        }
    }

    private void UpdatePathAnimation()
    {
        if (!_isPathAnimating) return;

        _pathAnimationTimer += Time.deltaTime;

        // 计算线性进度（基于时间）
        float linearProgress = Mathf.Clamp01(_pathAnimationTimer / _currentSegmentDuration);

        // 根据速度曲线计算实际进度
        float actualProgress = ApplySpeedCurveToProgress(linearProgress);

        // 计算当前爬升的距离
        float currentDistance = Mathf.Lerp(currentClimbedDistance, _targetClimbedDistance, actualProgress);

        // 确保距离不超过总长度
        currentDistance = Mathf.Min(currentDistance, _realLength);

        // 将距离转换为路径参数u01
        float u01 = Mathf.Clamp01(currentDistance / _realLength);

        // 采样路径
        SamplePath(u01, out Vector3 pos, out Quaternion rot, out Vector3 scale);
        obj.localPosition = pos;
        obj.localRotation = rot;
        obj.localScale = scale;

        // 记录调试位置
        if (drawPathInScene)
        {
            RecordDebugPosition(pos, u01);
        }

        // 调试输出
        if (enableDebug && _debugTimer >= debugInterval)
        {
            Debug.Log($"PathAnimation - LinearProgress: {linearProgress:F3}, ActualProgress: {actualProgress:F3}, Climbed: {currentDistance:F2}/{_realLength:F2}, u01: {u01:F3}, Position: {pos}");
            _debugTimer = 0f;
        }
        else
        {
            _debugTimer += Time.deltaTime;
        }

        // 检查是否完成当前段爬升
        if (linearProgress >= 1f)
        {
            // 确保精确到达目标距离
            currentClimbedDistance = _targetClimbedDistance;
            _isPathAnimating = false;

            if (enableDebug)
            {
                Debug.Log(
                    $"PathAnimation segment finished. Total climbed: {currentClimbedDistance:F2}, " +
                    $"队列剩余 = {_stepQueue.Count}"
                );
            }

            // 先检查是否已经到达终点
            bool reachedEnd = Mathf.Abs(currentClimbedDistance - _realLength) < 0.01f;

            if (reachedEnd)
            {
                // 确保精确到达终点
                currentClimbedDistance = _realLength;

                // 强制使用最后一个路径点的位置
                if (listStep != null && listStep.Length > 0)
                {
                    var lastStep = listStep[listStep.Length - 1];
                    obj.localPosition = lastStep.localPosition;
                    obj.localRotation = Quaternion.Euler(lastStep.localEulerRotation);
                    obj.localScale = lastStep.localScale;

                    if (enableDebug)
                    {
                        Debug.Log($"PathAnimation到达终点! 使用最后一个anchor位置: {lastStep.localPosition}");
                    }
                }

                // 走完终点，清空队列
                _stepQueue.Clear();

                if (enableDebug)
                {
                    Debug.Log(
                        $"PathAnimation到达终点! 爬升总距离: {currentClimbedDistance:F2}/{_realLength:F2}，队列已清空"
                    );
                }

                onSequenceFinish?.Invoke();
            }
            else
            {
                // 还没到终点，但当前段结束了：如果队列中还有段，则继续下一段
                if (_stepQueue.Count > 0)
                {
                    StartNextSegmentFromQueue();
                }
                else
                {
                    // 没有新的段，就暂时停在当前距离
                    if (enableDebug)
                    {
                        Debug.Log("PathAnimation 当前段结束，暂无后续排队段，等待新的 ClimbDistance 调用。");
                    }
                }
            }
        }

    }

    private void SnapToFinalAnchorIfAtEnd()
    {
        if (listStep == null || listStep.Length == 0) return;

        bool atEnd =
            Mathf.Abs(_realLength - currentClimbedDistance) < 0.001f ||
            Mathf.Abs(_realLength - _targetClimbedDistance) < 0.001f;

        if (atEnd)
        {
            var last = listStep[listStep.Length - 1];
            obj.localPosition = last.localPosition;
            obj.localRotation = Quaternion.Euler(last.localEulerRotation);
            obj.localScale = last.localScale;

            if (enableDebug)
                Debug.Log("已钉在最后一个anchor（SnapToFinalAnchorIfAtEnd）。");
        }
    }

    // PathAnimation模式的方法：每一步使用 stepDistance + stepDuration
    public void ClimbDistance(float stepDistance, float stepDuration)
    {
        if (movementMode != MovementMode.PathAnimation)
        {
            Debug.LogWarning($"ClimbDistance 只能在 PathAnimation 模式下调用，当前模式: {movementMode}");
            return;
        }

        // 只有在当前没有 PathAnimation 段在执行时才重建路径
        // 保证后续 CV 调用不会在中途改变当前段的几何和 _realLength
        if (!_isPathAnimating)
        {
            BuildPath();
            NormalizeCurve();
            PrecomputeDistanceToTime();
        }

        // 如果之前没在播放，自动开启
        if (!_isPlaying)
        {
            if (enableDebug)
                Debug.Log("ClimbDistance: 自动开始 PathAnimation 播放");
            _isPlaying = true;
            enabled = true;
        }

        // === 新逻辑：不再打断当前段，而是把这一步加入队列 ===
        float clampedDistance = Mathf.Max(0f, stepDistance);
        float clampedDuration = Mathf.Max(0.0001f, stepDuration);

        _stepQueue.Enqueue(new StepSegment
        {
            distance = clampedDistance,
            duration = clampedDuration
        });

        if (enableDebug)
        {
            Debug.Log(
                $"ClimbDistance 入队: 距离 = {clampedDistance:F3} m, 时长 = {clampedDuration:F3} s, " +
                $"当前队列长度 = {_stepQueue.Count}"
            );
        }

        // 如果当前没有在执行任何 PathAnimation 段，则立即启动队列中的下一段
        if (!_isPathAnimating)
        {
            StartNextSegmentFromQueue();
        }
    }

    // 兼容旧代码：如果只给距离，就用当前全局 duration 作为这一步的时长
    public void ClimbDistance(float stepDistance)
    {
        ClimbDistance(stepDistance, duration);
    }

    // 从队列中启动下一段 PathAnimation
    private void StartNextSegmentFromQueue()
    {
        if (enableDebug)
        {
            Debug.Log(
                $"[PathSeg] START | segLength={_currentSegmentLength:F3} | " +
                $"segDuration={_currentSegmentDuration:F3} | " +
                $"currDist={currentClimbedDistance:F3} -> target={_targetClimbedDistance:F3}"
            );
        }

        // 没有排队的段，直接返回
        if (_stepQueue.Count == 0)
        {
            _isPathAnimating = false;
            return;
        }

        // 计算当前剩余可爬距离
        float remainingDistance = _realLength - currentClimbedDistance;
        if (remainingDistance <= 0.001f)
        {
            // 已经基本到终点了，清空队列并钉到终点
            _stepQueue.Clear();
            currentClimbedDistance = _realLength;
            _targetClimbedDistance = _realLength;
            _isPathAnimating = false;
            SnapToFinalAnchorIfAtEnd();

            if (enableDebug)
                Debug.Log("StartNextSegmentFromQueue: 已到终点，清空队列。");

            onSequenceFinish?.Invoke();
            return;
        }

        // 取出下一段
        StepSegment seg = _stepQueue.Dequeue();
        float stepDist = Mathf.Min(seg.distance, remainingDistance);

        // 如果这一段距离太小，直接尝试下一段
        if (stepDist <= 0.001f)
        {
            if (enableDebug)
                Debug.Log("StartNextSegmentFromQueue: 本段距离太小，尝试下一段。");
            StartNextSegmentFromQueue();
            return;
        }

        _currentSegmentLength = stepDist;
        _currentSegmentDuration = Mathf.Max(0.0001f, seg.duration);
        _targetClimbedDistance = Mathf.Clamp(
            currentClimbedDistance + _currentSegmentLength, 0f, _realLength);

        // 为这一段映射速度曲线
        MapSpeedCurveForCurrentSegment();

        _pathAnimationTimer = 0f;
        _isPathAnimating = true;

        // 更新下一步最大爬升距离的显示（仅用于 Inspector）
        nextClimbDistance = _realLength - currentClimbedDistance;

        if (enableDebug)
        {
            Debug.Log(
                $"StartNextSegmentFromQueue: 启动新段，长度 = {_currentSegmentLength:F3} m, " +
                $"时长 = {_currentSegmentDuration:F3} s, 剩余距离 = {nextClimbDistance:F3} m, " +
                $"队列剩余 = {_stepQueue.Count}"
            );
        }
    }

    // 计算不考虑duration的基础总时间（用于比例计算）
    private float GetTotalBaseDuration()
    {
        if (_runtimeSpeedCurve == null || _realLength <= 0)
            return 1f;

        const int integrationSamples = 50;
        float ds = _realLength / integrationSamples;
        float totalTime = 0f;

        for (int i = 0; i < integrationSamples; i++)
        {
            float s = (i + 0.5f) * ds;
            float v = _runtimeSpeedCurve.Evaluate(s);
            if (v <= 0.001f) v = 0.001f;
            totalTime += ds / v;
        }

        return totalTime;
    }

    // 自动运动完全部剩余距离
    public void ClimbAllRemaining()
    {
        if (movementMode != MovementMode.PathAnimation)
        {
            Debug.LogWarning($"ClimbAllRemaining只能在PathAnimation模式下调用，当前模式: {movementMode}");
            return;
        }

        if (!_isPlaying)
        {
            _isPlaying = true;
            enabled = true;
        }

        if (_isPathAnimating)
        {
            Debug.LogWarning("正在执行PathAnimation，请等待当前段完成");
            return;
        }

        // 计算剩余距离
        float remainingDistance = _realLength - currentClimbedDistance;

        if (remainingDistance <= 0.001f)
        {
            Debug.Log("已经到达终点，无需运动");
            return;
        }

        if (enableDebug)
        {
            Debug.Log($"开始自动运动全部剩余距离: {remainingDistance:F2}米");
        }

        // 使用协程或直接调用完成全部运动
        StartCoroutine(ClimbAllRemainingCoroutine(remainingDistance));
    }

    // 协程方法：逐步完成全部运动
    private System.Collections.IEnumerator ClimbAllRemainingCoroutine(float totalRemainingDistance)
    {
        float remaining = totalRemainingDistance;

        while (remaining > 0.001f && _isPlaying)
        {
            // 如果上一段还在跑，先等它结束
            while (_isPathAnimating)
                yield return null;

            float climbThisStep = Mathf.Min(nextClimbDistance, remaining);
            if (climbThisStep <= 0.001f) break;

            // 先触发这一段
            ClimbDistance(climbThisStep);

            // === 移动到这里：等待本段真正完成 ===
            while (_isPathAnimating)
                yield return null;

            // 再结算“已完成的距离”
            remaining -= climbThisStep;

            if (enableDebug)
                Debug.Log($"自动运动步骤完成: 爬升 {climbThisStep:F2}米, 剩余 {remaining:F2}米");
        }

        // 协程整体完成后，确保精确钉到最后一个 anchor
        SnapToFinalAnchorIfAtEnd();

        if (enableDebug)
            Debug.Log("自动运动全部完成");

    }

    public void ResetClimb()
    {
        currentClimbedDistance = 0f;
        _targetClimbedDistance = 0f;
        _currentSegmentLength = 0f;
        _currentSegmentDuration = 0f;
        _isPathAnimating = false;
        _pathAnimationTimer = 0f;
        _isPlaying = true; // 确保重置后仍然在播放状态

        // 清空排队的步段
        _stepQueue.Clear();
        nextClimbDistance = _realLength;

        // 重置到起点
        var first = listStep[0];
        obj.localPosition = first.localPosition;
        obj.localRotation = Quaternion.Euler(first.localEulerRotation);
        obj.localScale = first.localScale;

        // 重置调试轨迹
        ResetDebugTrail();

        if (enableDebug)
        {
            Debug.Log("PathAnimation已重置（队列已清空）");
        }
    }

    private void RecordDebugPosition(Vector3 position, float progress)
    {
        // 检查是否需要开始新的一段轨迹
        if (_debugPositions.Count > 0)
        {
            Vector3 lastPosition = _debugPositions[_debugPositions.Count - 1];
            float distance = Vector3.Distance(lastPosition, position);

            // 如果距离异常大，说明出现了跳跃（可能是循环播放）
            if (distance > 2.0f) // 调整这个阈值根据你的场景大小
            {
                // 如果是接近起点或终点的跳跃，可能是正常的循环，不记录警告
                if (progress > 0.1f && progress < 0.9f)
                {
                    Debug.LogWarning($"检测到轨迹跳跃: {distance}, 进度: {progress}");
                }
                // 清空轨迹，开始新的轨迹段
                _debugPositions.Clear();
            }
        }

        _debugPositions.Add(position);
    }
    #endregion

    #region ======== 路径采样 ========
    /* 统一用 0-1 采样整条路径 */
    private void SamplePath(float u01, out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        // 如果是PathAnimation模式，使用专用采样方法
        if (movementMode == MovementMode.PathAnimation)
        {
            SamplePathForPathAnimation(u01, out pos, out rot, out scale);
            return;
        }

        if (pathControlType == PathControlType.MultiPoint)
        {
            if (listStep == null || listStep.Length < 2)
            {
                pos = Vector3.zero;
                rot = Quaternion.identity;
                scale = Vector3.one;
                return;
            }

            if (lineType == LineType.Polyline)
            {
                // 折线模式：保持原有的分段逻辑
                // 计算总段数
                int segmentCount = listStep.Length - 1;

                // 计算每段的累积长度
                float[] segmentLengths = new float[segmentCount];
                float totalLength = 0f;
                for (int i = 0; i < segmentCount; i++)
                {
                    segmentLengths[i] = Vector3.Distance(listStep[i].localPosition, listStep[i + 1].localPosition);
                    totalLength += segmentLengths[i];
                }

                // 根据u01找到当前所在的段
                float targetLength = u01 * totalLength;
                float accumulatedLength = 0f;
                int currentSegment = 0;
                float segmentU = 0f;

                for (int i = 0; i < segmentCount; i++)
                {
                    float segmentEnd = accumulatedLength + segmentLengths[i];
                    if (targetLength <= segmentEnd || i == segmentCount - 1)
                    {
                        currentSegment = i;
                        segmentU = (targetLength - accumulatedLength) / segmentLengths[i];
                        break;
                    }
                    accumulatedLength += segmentLengths[i];
                }

                // 确保不越界
                currentSegment = Mathf.Clamp(currentSegment, 0, segmentCount - 1);
                segmentU = Mathf.Clamp01(segmentU);

                // 对当前段应用TwoPoint模式的采样逻辑
                SampleTwoPointSegment(currentSegment, segmentU, out pos, out rot, out scale);
            }
            else
            {
                // 样条模式：使用完整的样条曲线（会经过所有点）
                pos = CalculateSplinePoint(u01, listStep);

                // 旋转和缩放也使用样条插值
                int segmentCount = listStep.Length - 1;
                float exactSegmentIndex = u01 * segmentCount;
                int segmentIndex = Mathf.FloorToInt(exactSegmentIndex);
                float segmentT = exactSegmentIndex - segmentIndex;

                segmentIndex = Mathf.Clamp(segmentIndex, 0, segmentCount - 1);
                segmentT = Mathf.Clamp01(segmentT);

                // 使用新的样条变换插值方法
                ApplySplineTransform(u01, segmentIndex, segmentT, out rot, out scale);
            }
        }
        else
        {
            // 保持原有的TwoPoint模式采样逻辑不变
            if (lineType == LineType.Polyline)
            {
                // 同样拆成 0-0.5  0.5-1 两段
                if (u01 <= 0.5f)
                {
                    float t = u01 * 2f;
                    var s = listStep[0]; var e = listStep[1];
                    pos = Vector3.LerpUnclamped(s.localPosition, e.localPosition, t);
                    rot = Quaternion.SlerpUnclamped(Quaternion.Euler(s.localEulerRotation),
                                                      Quaternion.Euler(e.localEulerRotation), t);
                    scale = Vector3.LerpUnclamped(s.localScale, e.localScale, t);
                }
                else
                {
                    float t = (u01 - 0.5f) * 2f;
                    var s = listStep[1]; var e = listStep[2];
                    pos = Vector3.LerpUnclamped(s.localPosition, e.localPosition, t);
                    rot = Quaternion.SlerpUnclamped(Quaternion.Euler(s.localEulerRotation),
                                                      Quaternion.Euler(e.localEulerRotation), t);
                    scale = Vector3.LerpUnclamped(s.localScale, e.localScale, t);
                }
            }
            else
            {
                // 贝塞尔位置
                Vector3 p0 = listStep[0].localPosition;
                Vector3 p1 = listStep[1].localPosition;
                Vector3 p2 = listStep[2].localPosition;
                pos = Mathf.Pow(1 - u01, 2) * p0 +
                      2 * (1 - u01) * u01 * p1 +
                      Mathf.Pow(u01, 2) * p2;

                // 旋转/缩放也走"两段"
                if (u01 <= 0.5f)
                {
                    float t = u01 * 2f;
                    var q0 = Quaternion.Euler(listStep[0].localEulerRotation);
                    var q1 = Quaternion.Euler(listStep[1].localEulerRotation);
                    rot = Quaternion.SlerpUnclamped(q0, q1, t);
                    scale = Vector3.LerpUnclamped(listStep[0].localScale,
                                                  listStep[1].localScale, t);
                }
                else
                {
                    float t = (u01 - 0.5f) * 2f;
                    var q1 = Quaternion.Euler(listStep[1].localEulerRotation);
                    var q2 = Quaternion.Euler(listStep[2].localEulerRotation);
                    rot = Quaternion.SlerpUnclamped(q1, q2, t);
                    scale = Vector3.LerpUnclamped(listStep[1].localScale,
                                                  listStep[2].localScale, t);
                }
            }
        }
    }

    // PathAnimation模式专用的采样方法，确保每步爬升独立应用stepOffset
    private void SamplePathForPathAnimation(float u01, out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        if (listStep == null || listStep.Length < 2)
        {
            pos = Vector3.zero; rot = Quaternion.identity; scale = Vector3.one;
            return;
        }

        u01 = Mathf.Clamp01(u01);

        // === 在 PathAnimation 段内：无论是不是最后一步，都用“段内 Bezier/Polyline + stepOffset/offsetRatio” ===
        if (movementMode == MovementMode.PathAnimation && _isPathAnimating)
        {
            // 段起点/终点基于“基础路径”（不含 stepOffset）
            float startU = Mathf.Clamp01(_realLength > 0f ? currentClimbedDistance / _realLength : 0f);
            float endU = Mathf.Clamp01(_realLength > 0f ? _targetClimbedDistance / _realLength : 0f);

            Vector3 segmentStart = CalculateBasePathPoint(startU);
            Vector3 segmentEnd = CalculateBasePathPoint(endU);

            // 段内采样（内部会根据 lineType 选择 Polyline 或 Bezier，并正确应用 stepOffset/offsetRatio）
            ApplyStepOffsetToCurrentSegment(u01, segmentStart, segmentEnd, out pos, out rot, out scale);
            return;
        }

        // === 非段内（比如段刚结束或未播放）：保持已有“完整路径采样”与 snap 逻辑 ===
        if (u01 >= 0.999f)
        {
            var lastStep = listStep[listStep.Length - 1];
            pos = lastStep.localPosition;
            rot = Quaternion.Euler(lastStep.localEulerRotation);
            scale = lastStep.localScale;
            return;
        }

        // 保持你原先的“完整路径采样”分支（MultiPoint/TwoPoint + Polyline/Bezier）
        if (pathControlType == PathControlType.MultiPoint)
        {
            if (lineType == LineType.Polyline)
            {
                int segmentCount = listStep.Length - 1;
                float exactSegmentIndex = u01 * segmentCount;
                int segmentIndex = Mathf.FloorToInt(exactSegmentIndex);
                float segmentT = exactSegmentIndex - segmentIndex;

                segmentIndex = Mathf.Clamp(segmentIndex, 0, segmentCount - 1);
                segmentT = Mathf.Clamp01(segmentT);

                TransformStep start = listStep[segmentIndex];
                TransformStep end = listStep[segmentIndex + 1];

                pos = Vector3.Lerp(start.localPosition, end.localPosition, segmentT);
                rot = Quaternion.Slerp(Quaternion.Euler(start.localEulerRotation),
                                       Quaternion.Euler(end.localEulerRotation), segmentT);
                scale = Vector3.Lerp(start.localScale, end.localScale, segmentT);
            }
            else
            {
                pos = CalculateSplinePoint(u01, listStep);

                int segmentCount = listStep.Length - 1;
                float exactIndex = u01 * segmentCount;
                int index = Mathf.FloorToInt(exactIndex);
                float t = exactIndex - index;

                index = Mathf.Clamp(index, 0, segmentCount - 1);
                t = Mathf.Clamp01(t);

                TransformStep start = listStep[index];
                TransformStep end = listStep[index + 1];

                rot = Quaternion.Slerp(Quaternion.Euler(start.localEulerRotation),
                                       Quaternion.Euler(end.localEulerRotation), t);
                scale = Vector3.Lerp(start.localScale, end.localScale, t);
            }
        }
        else
        {
            if (lineType == LineType.Polyline)
            {
                int segmentCount = 2;
                float exactIndex = u01 * segmentCount;
                int segmentIndex = Mathf.FloorToInt(exactIndex);
                float segmentU = exactIndex - segmentIndex;
                segmentIndex = Mathf.Clamp(segmentIndex, 0, 1);
                segmentU = Mathf.Clamp01(segmentU);

                TransformStep s0 = listStep[0];
                TransformStep s2 = listStep[2];

                // 按 TwoPoint 的预览逻辑做插值
                if (segmentIndex == 0)
                {
                    pos = Vector3.Lerp(s0.localPosition, listStep[1].localPosition, segmentU);
                    rot = Quaternion.Slerp(Quaternion.Euler(s0.localEulerRotation),
                                           Quaternion.Euler(listStep[1].localEulerRotation), segmentU);
                    scale = Vector3.Lerp(s0.localScale, listStep[1].localScale, segmentU);
                }
                else
                {
                    pos = Vector3.Lerp(listStep[1].localPosition, s2.localPosition, segmentU);
                    rot = Quaternion.Slerp(Quaternion.Euler(listStep[1].localEulerRotation),
                                           Quaternion.Euler(s2.localEulerRotation), segmentU);
                    scale = Vector3.Lerp(listStep[1].localScale, s2.localScale, segmentU);
                }
            }
            else // Bezier
            {
                TransformStep s0 = listStep[0];
                TransformStep s1 = listStep[1];
                TransformStep s2 = listStep[2];

                pos = Mathf.Pow(1 - u01, 2) * s0.localPosition +
                      2 * (1 - u01) * u01 * s1.localPosition +
                      Mathf.Pow(u01, 2) * s2.localPosition;

                if (u01 <= 0.5f)
                {
                    float t = u01 * 2f;
                    rot = Quaternion.Slerp(Quaternion.Euler(s0.localEulerRotation),
                                           Quaternion.Euler(s1.localEulerRotation), t);
                    scale = Vector3.Lerp(s0.localScale, s1.localScale, t);
                }
                else
                {
                    float t = (u01 - 0.5f) * 2f;
                    rot = Quaternion.Slerp(Quaternion.Euler(s1.localEulerRotation),
                                           Quaternion.Euler(s2.localEulerRotation), t);
                    scale = Vector3.Lerp(s1.localScale, s2.localScale, t);
                }
            }
        }
    }

    private Vector3 CalculateBasePathPoint(float u01)
    {
        if (listStep == null || listStep.Length < 2) return Vector3.zero;

        // >>> 修复：末端与起点的快速返回，避免“落在最后一段的 t=0”
        if (u01 <= 0f) return listStep[0].localPosition;
        if (u01 >= 1f) return listStep[listStep.Length - 1].localPosition;

        if (pathControlType == PathControlType.MultiPoint)
        {
            if (lineType == LineType.Polyline)
            {
                int segmentCount = listStep.Length - 1;
                float exactSegmentIndex = u01 * segmentCount;
                int segmentIndex = Mathf.FloorToInt(exactSegmentIndex);
                float segmentT = exactSegmentIndex - segmentIndex;

                segmentIndex = Mathf.Clamp(segmentIndex, 0, segmentCount - 1);
                segmentT = Mathf.Clamp01(segmentT);

                TransformStep start = listStep[segmentIndex];
                TransformStep end = listStep[segmentIndex + 1];

                return Vector3.Lerp(start.localPosition, end.localPosition, segmentT);
            }
            else
            {
                return CalculateSplinePointWithoutOffset(u01, listStep);
            }
        }
        else
        {
            TransformStep start = listStep[0];
            TransformStep end = listStep[listStep.Length - 1];
            return Vector3.Lerp(start.localPosition, end.localPosition, u01);
        }
    }

    // 计算不包含stepOffset的样条点
    private Vector3 CalculateSplinePointWithoutOffset(float t, TransformStep[] points)
    {
        if (points.Length == 0) return Vector3.zero;
        if (points.Length == 1) return points[0].localPosition;

        // >>> 修复：首末端快速返回，确保精确落在最后一个 anchor
        if (t <= 0f) return points[0].localPosition;
        if (t >= 1f) return points[points.Length - 1].localPosition;

        int segmentCount = points.Length - 1;
        float exactSegmentIndex = t * segmentCount;
        int segmentIndex = Mathf.FloorToInt(exactSegmentIndex);
        float segmentT = exactSegmentIndex - segmentIndex;

        segmentIndex = Mathf.Clamp(segmentIndex, 0, segmentCount - 1);
        segmentT = Mathf.Clamp01(segmentT);

        // 获取当前段的四个控制点（用于Catmull-Rom样条）
        Vector3 p0, p1, p2, p3;

        if (segmentIndex == 0)
        {
            // 第一段：使用虚拟的前一个点
            p0 = points[0].localPosition;
            p1 = points[0].localPosition;
            p2 = points[1].localPosition;
            p3 = points[Mathf.Min(2, points.Length - 1)].localPosition;
        }
        else if (segmentIndex == segmentCount - 1)
        {
            // 最后一段：使用虚拟的后一个点
            p0 = points[Mathf.Max(segmentIndex - 1, 0)].localPosition;
            p1 = points[segmentIndex].localPosition;
            p2 = points[segmentIndex + 1].localPosition;
            p3 = points[segmentIndex + 1].localPosition;
        }
        else
        {
            // 中间段：使用真实的四个点
            p0 = points[segmentIndex - 1].localPosition;
            p1 = points[segmentIndex].localPosition;
            p2 = points[segmentIndex + 1].localPosition;
            p3 = points[Mathf.Min(segmentIndex + 2, points.Length - 1)].localPosition;
        }

        // 计算基础样条点（不应用stepOffset）
        return CalculateCatmullRomPoint(segmentT, p0, p1, p2, p3);
    }

    // 在当前爬升段上应用stepOffset
    private void ApplyStepOffsetToCurrentSegment(float u01, Vector3 segmentStart, Vector3 segmentEnd, out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        // 计算当前段内的参数
        float segmentStartU = currentClimbedDistance / _realLength;
        float segmentEndU = _targetClimbedDistance / _realLength;
        float segmentU = Mathf.InverseLerp(segmentStartU, segmentEndU, u01);

        // 建立当前线段的路径坐标系
        Vector3 pathZ = (segmentEnd - segmentStart).normalized;
        Vector3 pathX = Vector3.Cross(Vector3.up, pathZ).normalized;
        if (pathX == Vector3.zero) pathX = Vector3.Cross(Vector3.forward, pathZ).normalized;
        Vector3 pathY = Vector3.Cross(pathZ, pathX).normalized;
        Matrix4x4 pathMatrix = Matrix4x4.TRS(Vector3.zero,
                                            Quaternion.LookRotation(pathZ, pathY),
                                            Vector3.one);

        if (lineType == LineType.Polyline)
        {
            // 折线模式：分段线性插值
            Vector3 localOffsetPos = pathMatrix.MultiplyPoint3x4(stepOffset.localPosition);
            Vector3 offsetPos = Vector3.Lerp(segmentStart, segmentEnd, offsetRatio) + localOffsetPos;

            if (segmentU <= offsetRatio)
            {
                float t = segmentU / offsetRatio;
                pos = Vector3.Lerp(segmentStart, offsetPos, t);
            }
            else
            {
                float t = (segmentU - offsetRatio) / (1f - offsetRatio);
                pos = Vector3.Lerp(offsetPos, segmentEnd, t);
            }
        }
        else
        {
            // 贝塞尔曲线模式
            Vector3 localOffsetPos = pathMatrix.MultiplyPoint3x4(stepOffset.localPosition);
            Vector3 offsetPos = Vector3.Lerp(segmentStart, segmentEnd, offsetRatio) + localOffsetPos;

            // 二阶贝塞尔曲线
            pos = Mathf.Pow(1 - segmentU, 2) * segmentStart +
                  2 * (1 - segmentU) * segmentU * offsetPos +
                  Mathf.Pow(segmentU, 2) * segmentEnd;
        }

        // 应用旋转和缩放
        ApplyBasicTransform(u01, out rot, out scale);

        // 应用stepOffset的旋转和缩放影响
        ApplyStepOffsetToTransform(0, segmentU, ref rot, ref scale);
    }

    // 应用基本的旋转和缩放插值
    private void ApplyBasicTransform(float u01, out Quaternion rot, out Vector3 scale)
    {
        if (listStep.Length < 2)
        {
            rot = Quaternion.identity;
            scale = Vector3.one;
            return;
        }

        // >>> 修复：末端直接取最后点的姿态与缩放
        if (u01 >= 1f)
        {
            var last = listStep[listStep.Length - 1];
            rot = Quaternion.Euler(last.localEulerRotation);
            scale = last.localScale;
            return;
        }
        if (u01 <= 0f)
        {
            var first = listStep[0];
            rot = Quaternion.Euler(first.localEulerRotation);
            scale = first.localScale;
            return;
        }

        if (listStep.Length == 2)
        {
            TransformStep start = listStep[0];
            TransformStep end = listStep[1];
            rot = Quaternion.Slerp(
                Quaternion.Euler(start.localEulerRotation),
                Quaternion.Euler(end.localEulerRotation),
                u01
            );
            scale = Vector3.Lerp(start.localScale, end.localScale, u01);
        }
        else
        {
            int segmentCount = listStep.Length - 1;
            float exactIndex = u01 * segmentCount;
            int index = Mathf.FloorToInt(exactIndex);
            float t = exactIndex - index;

            index = Mathf.Clamp(index, 0, segmentCount - 1);
            t = Mathf.Clamp01(t);

            TransformStep start = listStep[index];
            TransformStep end = listStep[index + 1];

            rot = Quaternion.Slerp(
                Quaternion.Euler(start.localEulerRotation),
                Quaternion.Euler(end.localEulerRotation),
                t
            );
            scale = Vector3.Lerp(start.localScale, end.localScale, t);
        }
    }

    // 样条模式的变换插值
    private void ApplySplineTransform(float u01, int segmentIndex, float segmentT, out Quaternion rot, out Vector3 scale)
    {
        if (listStep.Length < 2)
        {
            rot = Quaternion.identity;
            scale = Vector3.one;
            return;
        }

        // 计算样条插值的旋转和缩放
        // 使用与位置相同的样条参数u01，确保一致性

        // 对于旋转，使用球面线性插值
        if (listStep.Length == 2)
        {
            // 只有两个点，直接线性插值
            TransformStep start = listStep[0];
            TransformStep end = listStep[1];
            rot = Quaternion.Slerp(
                Quaternion.Euler(start.localEulerRotation),
                Quaternion.Euler(end.localEulerRotation),
                u01
            );
            scale = Vector3.Lerp(start.localScale, end.localScale, u01);
        }
        else
        {
            // 多个点，使用分段球面线性插值
            int segmentCount = listStep.Length - 1;
            float exactIndex = u01 * segmentCount;
            int index = Mathf.FloorToInt(exactIndex);
            float t = exactIndex - index;

            index = Mathf.Clamp(index, 0, segmentCount - 1);
            t = Mathf.Clamp01(t);

            TransformStep start = listStep[index];
            TransformStep end = listStep[index + 1];

            rot = Quaternion.Slerp(
                Quaternion.Euler(start.localEulerRotation),
                Quaternion.Euler(end.localEulerRotation),
                t
            );
            scale = Vector3.Lerp(start.localScale, end.localScale, t);
        }

        // 应用stepOffset的影响
        ApplyStepOffsetToTransform(segmentIndex, segmentT, ref rot, ref scale);
    }

    // 应用stepOffset到已有的旋转和缩放（引用传递）
    private void ApplyStepOffsetToTransform(int segmentIndex, float segmentT, ref Quaternion rot, ref Vector3 scale)
    {
        // 应用stepOffset的旋转偏移
        Quaternion offsetRot = Quaternion.Euler(stepOffset.localEulerRotation);

        // 根据offsetRatio调整偏移强度
        float offsetStrength = CalculateOffsetStrength(segmentT);
        Quaternion finalRot = rot * Quaternion.Slerp(Quaternion.identity, offsetRot, offsetStrength);

        // 应用stepOffset的缩放偏移
        Vector3 finalScale = Vector3.Lerp(scale, stepOffset.localScale, offsetStrength);

        rot = finalRot;
        scale = finalScale;
    }

    // 计算偏移强度（基于offsetRatio）
    private float CalculateOffsetStrength(float segmentT)
    {
        // 在offsetRatio附近达到最大强度，向两侧衰减
        float distanceToPeak = Mathf.Abs(segmentT - offsetRatio);
        float strength = Mathf.Clamp01(1f - distanceToPeak / Mathf.Max(offsetRatio, 1f - offsetRatio));
        return strength * strength; // 平方衰减，使效果更平滑
    }

    // 对单个线段应用TwoPoint模式的采样逻辑
    private void SampleTwoPointSegment(int segmentIndex, float segmentU, out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        TransformStep start = listStep[segmentIndex];
        TransformStep end = listStep[segmentIndex + 1];

        // 建立当前线段的"路径坐标系"
        Vector3 pathStart = start.localPosition;
        Vector3 pathEnd = end.localPosition;
        Vector3 pathZ = (pathEnd - pathStart).normalized;
        Vector3 pathX = Vector3.Cross(Vector3.up, pathZ).normalized;
        if (pathX == Vector3.zero) pathX = Vector3.Cross(Vector3.forward, pathZ).normalized;
        Vector3 pathY = Vector3.Cross(pathZ, pathX).normalized;
        Matrix4x4 pathMatrix = Matrix4x4.TRS(Vector3.zero,
                                             Quaternion.LookRotation(pathZ, pathY),
                                             Vector3.one);

        // 计算当前线段的偏移点（与TwoPoint模式相同的逻辑）
        Vector3 localOffsetPos = pathMatrix.MultiplyPoint3x4(stepOffset.localPosition);
        Vector3 offsetPos = Vector3.Lerp(pathStart, pathEnd, offsetRatio) + localOffsetPos;

        Quaternion baseRot = Quaternion.Lerp(Quaternion.Euler(start.localEulerRotation),
                                             Quaternion.Euler(end.localEulerRotation),
                                             offsetRatio);
        Quaternion localOffsetRot = Quaternion.Euler(stepOffset.localEulerRotation);
        Quaternion offsetRot = baseRot * localOffsetRot;

        TransformStep offsetPoint = new TransformStep
        {
            localPosition = offsetPos,
            localEulerRotation = offsetRot.eulerAngles,
            localScale = stepOffset.localScale
        };

        // 根据路径类型进行采样
        if (lineType == LineType.Polyline)
        {
            // 折线：分段线性插值
            if (segmentU <= offsetRatio)
            {
                float t = segmentU / offsetRatio;
                pos = Vector3.Lerp(start.localPosition, offsetPoint.localPosition, t);
                rot = Quaternion.Slerp(Quaternion.Euler(start.localEulerRotation),
                                      Quaternion.Euler(offsetPoint.localEulerRotation), t);
                scale = Vector3.Lerp(start.localScale, offsetPoint.localScale, t);
            }
            else
            {
                float t = (segmentU - offsetRatio) / (1f - offsetRatio);
                pos = Vector3.Lerp(offsetPoint.localPosition, end.localPosition, t);
                rot = Quaternion.Slerp(Quaternion.Euler(offsetPoint.localEulerRotation),
                                      Quaternion.Euler(end.localEulerRotation), t);
                scale = Vector3.Lerp(offsetPoint.localScale, end.localScale, t);
            }
        }
        else
        {
            // 贝塞尔曲线
            Vector3 p0 = start.localPosition;
            Vector3 p1 = offsetPoint.localPosition;
            Vector3 p2 = end.localPosition;

            pos = Mathf.Pow(1 - segmentU, 2) * p0 +
                  2 * (1 - segmentU) * segmentU * p1 +
                  Mathf.Pow(segmentU, 2) * p2;

            // 旋转和缩放分段处理
            if (segmentU <= 0.5f)
            {
                float t = segmentU * 2f;
                rot = Quaternion.Slerp(Quaternion.Euler(start.localEulerRotation),
                                      Quaternion.Euler(offsetPoint.localEulerRotation), t);
                scale = Vector3.Lerp(start.localScale, offsetPoint.localScale, t);
            }
            else
            {
                float t = (segmentU - 0.5f) * 2f;
                rot = Quaternion.Slerp(Quaternion.Euler(offsetPoint.localEulerRotation),
                                      Quaternion.Euler(end.localEulerRotation), t);
                scale = Vector3.Lerp(offsetPoint.localScale, end.localScale, t);
            }
        }
    }
    #endregion

    #region ======== 路程-时间积分（复用原逻辑） ========
    private void PrecomputeDistanceToTime()
    {
        int n = samples;
        _distanceToTime = new float[n + 1];
        float ds = _realLength / n;
        float inv = 1f / duration;

        float total = 0f;
        for (int i = 0; i < n; i++)
        {
            float s = (i + 0.5f) * ds;
            float v = _runtimeSpeedCurve.Evaluate(s);
            if (v <= 0f) v = 0.001f;
            total += ds / (v * inv);
        }
        _totalTime = total;

        float acc = 0f;
        _distanceToTime[0] = 0f;
        for (int i = 1; i <= n; i++)
        {
            float s = (i - 0.5f) * ds;
            float v = _runtimeSpeedCurve.Evaluate(s);
            if (v <= 0f) v = 0.001f;
            acc += ds / (v * inv);
            _distanceToTime[i] = acc / total;
        }
    }

    private float GetDistanceAtTime(float tNorm)
    {
        if (tNorm <= 0) return 0;
        if (tNorm >= 1) return _realLength;

        int left = 0, right = samples;
        while (left <= right)
        {
            int mid = (left + right) >> 1;
            float mt = _distanceToTime[mid];
            if (Mathf.Approximately(mt, tNorm)) return mid * _realLength / samples;
            if (mt < tNorm) left = mid + 1;
            else right = mid - 1;
        }

        int idx0 = Mathf.Clamp(right, 0, samples);
        int idx1 = Mathf.Clamp(left, 0, samples);
        float t0 = _distanceToTime[idx0];
        float t1 = _distanceToTime[idx1];
        if (Mathf.Approximately(t1, t0)) return idx0 * _realLength / samples;
        float w = (tNorm - t0) / (t1 - t0);
        return Mathf.Lerp(idx0 * _realLength / samples, idx1 * _realLength / samples, w);
    }
    #endregion

    #region ======== 速度曲线应用 ========
    // 在Scene视图中绘制调试信息
    private void OnDrawGizmos()
    {
        if (!drawPathInScene) return;

        // 加强空引用检查
        if (this == null) return;

        // 实时更新路径构建，确保预览及时刷新（包括运行时）
#if UNITY_EDITOR
        // 只在必要时重建预览路径
        if (!Application.isPlaying)
        {
            BuildPathForPreview();
        }
        else
        {
            // 运行时模式下也检查参数变化并更新预览
            CheckAndUpdateRuntimePreview();
        }
#endif

        // 确保listStep已初始化
        if (listStep == null || listStep.Length == 0) return;

        // 绘制路径点
        Gizmos.color = Color.red;
        for (int i = 0; i < listStep.Length; i++)
        {
            Vector3 worldPos = GetWorldPosition(listStep[i].localPosition);
            Gizmos.DrawSphere(worldPos, 0.1f);

            // 显示点序号
#if UNITY_EDITOR
            UnityEditor.Handles.Label(worldPos + Vector3.up * 0.2f, $"{i}");
#endif
        }

        // 绘制路径连线 - 对于PathAnimation模式，始终绘制基础路径
        Gizmos.color = pathColor;

        // 如果是PathAnimation模式，绘制基础路径（不考虑stepOffset）
        if (movementMode == MovementMode.PathAnimation)
        {
            DrawBasePathForPathAnimation();
        }
        else
        {
            // 原有的AutoAnimation模式路径绘制逻辑
            DrawPathForAutoAnimation();
        }

        // 绘制实际运动轨迹使用世界坐标
        if (_debugPositions.Count > 1)
        {
            Gizmos.color = Color.yellow;
            for (int i = 1; i < _debugPositions.Count; i++)
            {
                Vector3 worldPrev = GetWorldPosition(_debugPositions[i - 1]);
                Vector3 worldCurr = GetWorldPosition(_debugPositions[i]);
                Gizmos.DrawLine(worldPrev, worldCurr);
            }
        }

        // ========== 可选绘制PathAnimation模式的当前爬升段临时路径 ==========
        if (movementMode == MovementMode.PathAnimation && _isPathAnimating && listStep != null && listStep.Length >= 2)
        {
            DrawCurrentClimbSegmentPreview();
        }
    }

    // 绘制PathAnimation模式的基础路径（不考虑stepOffset）
    private void DrawBasePathForPathAnimation()
    {
        if (listStep == null || listStep.Length < 2) return;

        if (pathControlType == PathControlType.TwoPoint && listStep.Length >= 3)
        {
            // TwoPoint模式：直接连接起点和终点
            Vector3 p0 = GetWorldPosition(listStep[0].localPosition);
            Vector3 p2 = GetWorldPosition(listStep[2].localPosition);

            if (lineType == LineType.Polyline)
            {
                // 折线：直接连接起点和终点
                Gizmos.DrawLine(p0, p2);
            }
            else
            {
                // 贝塞尔曲线：使用起点和终点绘制直线（基础路径）
                Gizmos.DrawLine(p0, p2);
            }
        }
        else if (pathControlType == PathControlType.MultiPoint && listStep.Length >= 2)
        {
            if (lineType == LineType.Polyline)
            {
                // 折线：绘制所有基础点之间的连线
                for (int i = 0; i < listStep.Length - 1; i++)
                {
                    Vector3 p0 = GetWorldPosition(listStep[i].localPosition);
                    Vector3 p1 = GetWorldPosition(listStep[i + 1].localPosition);
                    Gizmos.DrawLine(p0, p1);
                }
            }
            else
            {
                // 样条曲线：绘制基础样条曲线（不应用stepOffset）
                const int segments = 50;
                Vector3 prev = GetWorldPosition(CalculateSplinePointWithoutOffset(0, listStep));

                for (int i = 1; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    Vector3 curr;

                    // 确保最后一个点精确地使用最后一个控制点的位置
                    if (i == segments)
                    {
                        curr = GetWorldPosition(listStep[listStep.Length - 1].localPosition);
                    }
                    else
                    {
                        curr = GetWorldPosition(CalculateSplinePointWithoutOffset(t, listStep));
                    }

                    Gizmos.DrawLine(prev, curr);
                    prev = curr;
                }

                // 双重保险：检查最后一段是否精确连接到终点
                Vector3 exactLastPoint = GetWorldPosition(listStep[listStep.Length - 1].localPosition);
                if (Vector3.Distance(prev, exactLastPoint) > 0.001f)
                {
                    Gizmos.DrawLine(prev, exactLastPoint);
                }
            }
        }

        // 标记为基础路径
#if UNITY_EDITOR
        if (listStep.Length > 0)
        {
            Vector3 labelPos = GetWorldPosition(listStep[0].localPosition);
            UnityEditor.Handles.Label(labelPos + Vector3.up * 0.5f, "基础路径");
        }
#endif
    }

    // 原有的AutoAnimation模式路径绘制逻辑
    private void DrawPathForAutoAnimation()
    {
        if (pathControlType == PathControlType.TwoPoint && listStep.Length >= 3)
        {
            // TwoPoint模式：绘制逻辑使用世界坐标
            Vector3 p0 = GetWorldPosition(listStep[0].localPosition);
            Vector3 p1 = GetWorldPosition(listStep[1].localPosition);
            Vector3 p2 = GetWorldPosition(listStep[2].localPosition);

            if (lineType == LineType.Polyline)
            {
                // 只在Polyline模式下绘制折线
                Gizmos.DrawLine(p0, p1);
                Gizmos.DrawLine(p1, p2);
            }
            else
            {
                // 在Bezier模式下只绘制贝塞尔曲线
                const int segments = 20;
                Vector3 prev = p0;
                for (int i = 1; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    Vector3 curr = Mathf.Pow(1 - t, 2) * p0 +
                                   2 * (1 - t) * t * p1 +
                                   Mathf.Pow(t, 2) * p2;
                    Gizmos.DrawLine(prev, curr);
                    prev = curr;
                }
            }
        }
        else if (pathControlType == PathControlType.MultiPoint && listStep.Length >= 2)
        {
            if (lineType == LineType.Polyline)
            {
                // 折线：绘制直线连接使用世界坐标
                for (int i = 0; i < listStep.Length - 1; i++)
                {
                    Vector3 p0 = GetWorldPosition(listStep[i].localPosition);
                    Vector3 p1 = GetWorldPosition(listStep[i + 1].localPosition);
                    Gizmos.DrawLine(p0, p1);
                }
            }
            else
            {
                // 样条曲线：绘制完整样条曲线（会经过所有点）使用世界坐标
                const int segments = 50;
                Vector3 prev = GetWorldPosition(CalculateSplinePoint(0, listStep));

                for (int i = 1; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    Vector3 curr;

                    // 确保最后一个点精确地使用最后一个控制点的位置
                    if (i == segments)
                    {
                        curr = GetWorldPosition(listStep[listStep.Length - 1].localPosition);
                    }
                    else
                    {
                        curr = GetWorldPosition(CalculateSplinePoint(t, listStep));
                    }

                    Gizmos.DrawLine(prev, curr);
                    prev = curr;
                }

                // 双重保险：检查最后一段是否精确连接到终点
                Vector3 exactLastPoint = GetWorldPosition(listStep[listStep.Length - 1].localPosition);
                if (Vector3.Distance(prev, exactLastPoint) > 0.001f)
                {
                    Gizmos.DrawLine(prev, exactLastPoint);
                }
            }
        }
    }

    // 绘制当前爬升段的临时路径（可选显示）
    private void DrawCurrentClimbSegmentPreview()
    {
        Gizmos.color = Color.cyan;

        // 计算当前爬升段的起点和终点
        Vector3 segmentStart = CalculateBasePathPoint(currentClimbedDistance / _realLength);
        Vector3 segmentEnd = CalculateBasePathPoint(_targetClimbedDistance / _realLength);

        Vector3 worldSegmentStart = GetWorldPosition(segmentStart);
        Vector3 worldSegmentEnd = GetWorldPosition(segmentEnd);

        // 建立当前线段的路径坐标系
        Vector3 pathZ = (segmentEnd - segmentStart).normalized;
        Vector3 pathX = Vector3.Cross(Vector3.up, pathZ).normalized;
        if (pathX == Vector3.zero) pathX = Vector3.Cross(Vector3.forward, pathZ).normalized;
        Vector3 pathY = Vector3.Cross(pathZ, pathX).normalized;
        Matrix4x4 pathMatrix = Matrix4x4.TRS(Vector3.zero,
                                            Quaternion.LookRotation(pathZ, pathY),
                                            Vector3.one);

        Vector3 localOffsetPos = pathMatrix.MultiplyPoint3x4(stepOffset.localPosition);
        Vector3 offsetPos = Vector3.Lerp(segmentStart, segmentEnd, offsetRatio) + localOffsetPos;
        Vector3 worldOffsetPos = GetWorldPosition(offsetPos);

        if (lineType == LineType.Polyline)
        {
            // 绘制折线模式的临时路径
            Gizmos.DrawLine(worldSegmentStart, worldOffsetPos);
            Gizmos.DrawLine(worldOffsetPos, worldSegmentEnd);

            // 绘制临时路径点
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(worldOffsetPos, 0.08f);
        }
        else
        {
            // 绘制贝塞尔曲线模式的临时路径
            const int segments = 20;
            Vector3 prev = worldSegmentStart;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 curr = Mathf.Pow(1 - t, 2) * worldSegmentStart +
                               2 * (1 - t) * t * worldOffsetPos +
                               Mathf.Pow(t, 2) * worldSegmentEnd;
                Gizmos.DrawLine(prev, curr);
                prev = curr;
            }

            // 绘制临时路径点
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(worldOffsetPos, 0.08f);
        }

        // 绘制当前爬升段的起点和终点标记
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(worldSegmentStart, 0.06f);
        Gizmos.DrawWireSphere(worldSegmentEnd, 0.06f);

        // 显示爬升段信息
#if UNITY_EDITOR
        UnityEditor.Handles.Label(worldSegmentStart + Vector3.up * 0.3f, $"爬升起点\n距离: {currentClimbedDistance:F2}");
        UnityEditor.Handles.Label(worldSegmentEnd + Vector3.up * 0.3f, $"爬升终点\n距离: {_targetClimbedDistance:F2}");
        UnityEditor.Handles.Label(worldOffsetPos + Vector3.up * 0.3f, "临时偏移点");
#endif
    }

    // 用于跟踪参数变化的字段
    private TransformOffset _lastStepOffset;
    private float _lastOffsetRatio;
    // 运行时预览更新检查
    private void CheckAndUpdateRuntimePreview()
    {
        // 检查TwoPoint模式下相关参数是否变化
        if (pathControlType == PathControlType.TwoPoint)
        {
            // 检查stepOffset或offsetRatio是否变化
            bool needsUpdate = false;

            // 检查stepOffset变化
            if (_lastStepOffset == null ||
                !_lastStepOffset.localPosition.Equals(stepOffset.localPosition) ||
                !_lastStepOffset.localEulerRotation.Equals(stepOffset.localEulerRotation) ||
                !_lastStepOffset.localScale.Equals(stepOffset.localScale))
            {
                needsUpdate = true;
                _lastStepOffset = new TransformOffset
                {
                    localPosition = stepOffset.localPosition,
                    localEulerRotation = stepOffset.localEulerRotation,
                    localScale = stepOffset.localScale
                };
            }

            // 检查offsetRatio变化
            if (Mathf.Abs(_lastOffsetRatio - offsetRatio) > 0.001f)
            {
                needsUpdate = true;
                _lastOffsetRatio = offsetRatio;
            }

            if (needsUpdate)
            {
                BuildPathForPreview();
            }
        }
    }

    private void MapSpeedCurveForCurrentSegment()
    {
        float L = Mathf.Max(0f, _currentSegmentLength);
        if (speedCurve == null || speedCurve.length == 0 || L <= 0f)
        {
            _currentSegmentSpeedCurve = AnimationCurve.Linear(0, 1, Mathf.Max(L, 0.0001f), 1);
            return;
        }

        // 拷贝
        var keys = new Keyframe[speedCurve.length];
        for (int i = 0; i < speedCurve.length; i++)
            keys[i] = new Keyframe(speedCurve[i].time, speedCurve[i].value, speedCurve[i].inTangent, speedCurve[i].outTangent);

        var curve = new AnimationCurve(keys);
        float originalLen = curve.keys[curve.length - 1].time;

        if (originalLen <= 0f)
        {
            _currentSegmentSpeedCurve = AnimationCurve.Linear(0, 1, Mathf.Max(L, 0.0001f), 1);
            return;
        }

        float scale = L / originalLen;
        var remapped = new Keyframe[curve.length];
        for (int i = 0; i < curve.length; i++)
        {
            float nt = curve.keys[i].time * scale;
            float inTan = curve.keys[i].inTangent / Mathf.Max(scale, 1e-6f);
            float outTan = curve.keys[i].outTangent / Mathf.Max(scale, 1e-6f);
            remapped[i] = new Keyframe(nt, curve.keys[i].value, inTan, outTan);
        }

        _currentSegmentSpeedCurve = new AnimationCurve(remapped);
    }

    // 将线性时间进度映射为受速度权重影响的距离进度（y 仅作为“权重”）
    private float ApplySpeedCurveToProgress(float linearProgress)
    {
        if (_currentSegmentSpeedCurve == null || _currentSegmentLength <= 0f)
            return Mathf.Clamp01(linearProgress);

        const int N = 100;           // 采样密度
        float L = _currentSegmentLength;
        float ds = L / N;
        const float EPS = 1e-4f;     // 权重下限，避免除零

        // 计算总“无量纲时间”Tau = ∫ ds / w(s)
        float Tau = 0f;
        for (int i = 0; i < N; i++)
        {
            float sMid = (i + 0.5f) * ds;
            float w = _currentSegmentSpeedCurve.Evaluate(sMid);
            if (w < EPS) w = EPS;
            Tau += ds / w;
        }

        // 线性时间占比 -> 目标无量纲时间
        float p = Mathf.Clamp01(linearProgress);
        float targetTau = p * Tau;

        // 反求 x：累计 tau(x) 直到达到 targetTau
        float accTau = 0f;
        for (int i = 0; i < N; i++)
        {
            float sMid = (i + 0.5f) * ds;
            float w = _currentSegmentSpeedCurve.Evaluate(sMid);
            if (w < EPS) w = EPS;

            float dTau = ds / w;

            if (accTau + dTau >= targetTau)
            {
                // 在当前小段内线性反解
                float remain = targetTau - accTau;      // 还需要的“无量纲时间”
                float localT = remain / dTau;           // 当前 ds 内占比 [0,1]
                float x = (i * ds) + localT * ds;
                return Mathf.Clamp01(x / L);
            }

            accTau += dTau;
        }

        // 数值误差兜底
        return 1f;
    }

    // 计算当前段的持续时间（基于速度曲线积分）
    private float CalculateSegmentDuration(float segmentLength)
    {
        if (_currentSegmentSpeedCurve == null || segmentLength <= 0)
            return 0f;

        // 使用数值积分计算时间：时间 = ∫(1/v) ds
        const int integrationSamples = 50;
        float ds = segmentLength / integrationSamples;
        float totalTime = 0f;

        for (int i = 0; i < integrationSamples; i++)
        {
            float s = (i + 0.5f) * ds; // 中点采样
            float v = _currentSegmentSpeedCurve.Evaluate(s);

            if (v <= 0.001f) // 避免除零
                v = 0.001f;

            totalTime += ds / v;
        }

        return totalTime;
    }

    // 构建预览路径
    private void BuildPathForPreview()
    {
        // 如果是Anchor模式且anchor不为空，自动更新路径点
        if (pathControlType == PathControlType.MultiPoint &&
            pointSourceType == PointSourceType.Anchor && anchor != null)
        {
            UpdatePointsFromAnchor();
        }

        if (pathControlType == PathControlType.MultiPoint)
        {
            // 对于MultiPoint模式，使用listPoint构建预览路径
            if (listPoint.Count < 2) return;

            listStep = new TransformStep[listPoint.Count];
            for (int i = 0; i < listPoint.Count; i++)
            {
                listStep[i] = listPoint[i];
            }
        }
        else
        {
            // TwoPoint模式预览路径构建 - 确保使用最新的offsetRatio和stepOffset值
            if (startPoint == null || endPoint == null) return;

            listStep = new TransformStep[3];
            listStep[0] = startPoint;
            listStep[2] = endPoint;

            // 计算中间点（偏移点）- 使用当前Inspector中的最新值
            Vector3 pathStart = startPoint.localPosition;
            Vector3 pathEnd = endPoint.localPosition;
            Vector3 pathZ = (pathEnd - pathStart).normalized;
            Vector3 pathX = Vector3.Cross(Vector3.up, pathZ).normalized;
            if (pathX == Vector3.zero) pathX = Vector3.Cross(Vector3.forward, pathZ).normalized;
            Vector3 pathY = Vector3.Cross(pathZ, pathX).normalized;
            Matrix4x4 pathMatrix = Matrix4x4.TRS(Vector3.zero,
                                                Quaternion.LookRotation(pathZ, pathY),
                                                Vector3.one);

            // 使用当前stepOffset和offsetRatio值
            Vector3 localOffsetPos = pathMatrix.MultiplyPoint3x4(stepOffset.localPosition);
            Vector3 offsetPos = Vector3.Lerp(pathStart, pathEnd, offsetRatio) + localOffsetPos;

            Quaternion baseRot = Quaternion.Lerp(Quaternion.Euler(startPoint.localEulerRotation),
                                                Quaternion.Euler(endPoint.localEulerRotation),
                                                offsetRatio);
            Quaternion localOffsetRot = Quaternion.Euler(stepOffset.localEulerRotation);
            Quaternion offsetRot = baseRot * localOffsetRot;

            listStep[1] = new TransformStep
            {
                localPosition = offsetPos,
                localEulerRotation = offsetRot.eulerAngles,
                localScale = stepOffset.localScale
            };
        }

        // 更新参数哈希
        _lastParameterHash = CalculateParameterHash();

        // 调试信息
#if UNITY_EDITOR
        if (enableDebug && Application.isPlaying)
        {
            Debug.Log($"Preview路径已更新 - 模式: {pathControlType}, 线型: {lineType}, OffsetRatio: {offsetRatio}");
        }
#endif
    }

    // 获取世界坐标位置
    private Vector3 GetWorldPosition(Vector3 localPosition)
    {
        if (this == null) return localPosition;

        Transform referenceTransform = obj != null ? obj.parent : transform;
        if (referenceTransform == null) return localPosition;

        return referenceTransform.TransformPoint(localPosition);
    }

    // 绘制虚线
    private void DrawDashedLine(Vector3 start, Vector3 end, float dashLength)
    {
        float distance = Vector3.Distance(start, end);
        if (distance < 0.01f) return;

        int dashCount = Mathf.Max(1, Mathf.FloorToInt(distance / dashLength));
        dashCount = Mathf.Min(dashCount, 50); // 合理上限

        for (int i = 0; i < dashCount; i++)
        {
            if (i % 2 == 1) continue; // 跳过间隔段

            float startT = i * dashLength / distance;
            float endT = Mathf.Min((i + 1) * dashLength / distance, 1f);

            if (startT >= 1f) break;

            Vector3 dashStart = Vector3.Lerp(start, end, startT);
            Vector3 dashEnd = Vector3.Lerp(start, end, endT);
            Gizmos.DrawLine(dashStart, dashEnd);
        }
    }
    #endregion

    #region ======== 公共接口（保持兼容） ========
    public void ResetToStart()
    {
        _timer = 0;
        var first = listStep[0];
        obj.localPosition = first.localPosition;
        obj.localRotation = Quaternion.Euler(first.localEulerRotation);
        obj.localScale = first.localScale;

        // 如果是PathAnimation模式，同时重置爬升距离
        if (movementMode == MovementMode.PathAnimation)
        {
            currentClimbedDistance = 0f;
            _targetClimbedDistance = 0f;
            _isPathAnimating = false;
            _pathAnimationTimer = 0f;
        }
    }

    // 从Anchor子物体更新路径点的方法
    [ContextMenu("从Anchor更新路径点")]
    public void UpdatePointsFromAnchor()
    {
        // 若运行时且已经加载过路径点, 则直接跳过
        if (Application.isPlaying && _hasLoadedAnchorPoints)
        {
            return;
        }

        if (anchor == null)
        {
            if (Application.isPlaying)
                Debug.LogWarning("Anchor未设置，无法获取路径点");
            return;
        }

        listPoint.Clear();

        // 获取所有直接子物体
        foreach (Transform child in anchor)
        {
            if (child != null)
            {
                var step = new TransformStep
                {
                    localPosition = child.localPosition,
                    localEulerRotation = child.localEulerAngles,
                    localScale = child.localScale
                };
                listPoint.Add(step);
                if (Application.isPlaying)
                    Debug.Log($"添加路径点: {child.name}", child);
            }
        }

        _hasLoadedAnchorPoints = true;
        if (Application.isPlaying)
            Debug.Log($"从Anchor '{anchor.name}' 获取了 {listPoint.Count} 个路径点");
    }

    public void ForceReloadPathFromAnchor()
    {
        _hasLoadedAnchorPoints = false;
        UpdatePointsFromAnchor();
        BuildPath();
    }

    [ContextMenu("打印路径点信息")]
    public void PrintPathPoints()
    {
        if (listStep == null || listStep.Length == 0)
        {
            Debug.Log("No path points available.");
            return;
        }

        Debug.Log($"=== 路径点信息 (共{listStep.Length}个点) ===");
        for (int i = 0; i < listStep.Length; i++)
        {
            var point = listStep[i];
            Debug.Log($"点 {i}: Pos={point.localPosition}, Rot={point.localEulerRotation}, Scale={point.localScale}");
        }

        Debug.Log($"路径总长度: {_realLength}");
        Debug.Log($"路径类型: {pathControlType}, 线型: {lineType}");
    }

    [ContextMenu("重置调试轨迹")]
    public void ResetDebugTrail()
    {
        _debugPositions.Clear();
    }

    [ContextMenu("Play Again")]
    public void Play()
    {
        // 重置调试轨迹
        ResetDebugTrail();
        // 更新参数哈希
        _lastParameterHash = CalculateParameterHash();

        // 1. 用最新 Inspector 值重新生成路径
        BuildPath();
        if (enableDebug) // 打印路径信息用于调试
        {
            PrintPathPoints();
        }
        // 2. 重新归一化速度曲线
        NormalizeCurve();
        // 3. 重新积分"路程→时间"表
        PrecomputeDistanceToTime();
        // 4. 根据运动模式复位
        if (movementMode == MovementMode.AutoAnimation)
        {
            ResetToStart();
            _timer = 0;
            _isPlaying = true;
        }
        else if (movementMode == MovementMode.PathAnimation)
        {
            ResetClimb();
            _isPlaying = true;

            // 初始化当前段参数
            _currentSegmentLength = 0f;
            _currentSegmentDuration = 0f;
        }
        // 5. 开始播放
        enabled = true;

        if (enableDebug)
        {
            Debug.Log($"开始播放变换序列 - 模式: {movementMode}, 路径长度: {_realLength:F2}");
        }
    }

    // 在登山步态进入 Idle 时强制把当前 Path 段结算完，清空队列，确保后续“恢复后的第一步”从一个干净状态开始。
    public void ForceCompleteCurrentSegment()
    {
        if (movementMode != MovementMode.PathAnimation)
            return;

        // 如果当前还有一段在播放，直接结算到目标距离
        if (_isPathAnimating)
        {
            currentClimbedDistance = _targetClimbedDistance;
            _isPathAnimating = false;
            _pathAnimationTimer = 0f;

            if (enableDebug)
            {
                Debug.Log(
                    $"[TransformSequenceController] ForceCompleteCurrentSegment: " +
                    $"强制结束当前段，climbed = {currentClimbedDistance:F3}"
                );
            }
        }

        // 清空所有排队中的步段
        _stepQueue.Clear();

        // 如果已经接近终点，顺便 Snap 一下，避免累积误差
        SnapToFinalAnchorIfAtEnd();

        if (enableDebug)
        {
            Debug.Log(
                "[TransformSequenceController] ForceCompleteCurrentSegment: " +
                "队列已清空，Path 状态已与当前 climbed 同步。"
            );
        }
    }

    public void Pause() => _isPlaying = false;
    public void Resume() => _isPlaying = true;
    public void Stop() { _isPlaying = false; enabled = false; }
    #endregion

    // 在Inspector值改变时自动更新
#if UNITY_EDITOR
    private void OnValidate()
    {
        // 在运行模式下，当任何相关参数改变时都更新路径点
        if (Application.isPlaying)
        {
            // 检查是否有需要触发预览更新的字段变化
            bool needsUpdate = false;

            // 检查TwoPoint模式相关字段
            if (pathControlType == PathControlType.TwoPoint)
            {
                // 检查offsetRatio或stepOffset相关字段是否变化
                // 注意：这里我们无法直接检测字段变化，所以每次都强制更新
                needsUpdate = true;
            }

            // 检查MultiPoint模式相关字段
            if (pathControlType == PathControlType.MultiPoint)
            {
                // 检查Anchor相关字段
                if (pointSourceType == PointSourceType.Anchor)
                {
                    needsUpdate = true;
                }

                // 检查stepOffset相关字段变化
                if (_lastStepOffset == null ||
                    !_lastStepOffset.localPosition.Equals(stepOffset.localPosition) ||
                    !_lastStepOffset.localEulerRotation.Equals(stepOffset.localEulerRotation) ||
                    !_lastStepOffset.localScale.Equals(stepOffset.localScale))
                {
                    needsUpdate = true;
                    _lastStepOffset = new TransformOffset
                    {
                        localPosition = stepOffset.localPosition,
                        localEulerRotation = stepOffset.localEulerRotation,
                        localScale = stepOffset.localScale
                    };
                }
            }

            // 检查路径类型变化
            needsUpdate = true; // 简化处理：总是更新

            if (needsUpdate)
            {
                // 强制标记为需要重新绘制
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        // 重建预览路径
                        BuildPathForPreview();
                        // 强制场景重绘
                        UnityEditor.SceneView.RepaintAll();
                    }
                };
            }

            // 在编辑模式下，当anchor改变时自动更新路径点
            if (pointSourceType == PointSourceType.Anchor && anchor != null)
            {
                UpdatePointsFromAnchor();
            }
        }
    }
#endif
}