using UnityEngine;

public class TouchControl : MonoBehaviour
{
    [Header("旋转设置")]
    public float rotationSpeed = 2f;  // 旋转速度系数
    public bool invertRotation = false;  // 是否反转旋转方向
    [Range(0, 20)]
    public float rotationDeadZone = 5f; // 旋转防抖阈值（像素）

    [Header("缩放设置")]
    public float minScale = 0.1f;  // 最小缩放比例
    public float maxScale = 3f;   // 最大缩放比例
    public float zoomSpeed = 0.001f;  // 缩放速度系数
    [Range(0, 0.1f)]
    public float zoomDeadZone = 0.01f; // 缩放防抖阈值（比例变化）

    private Vector2 lastSingleTouchPosition;  // 记录上一帧单指位置
    private float initialTouchDistance;       // 记录双指初始距离
    private float lastTouchDistance;          // 记录上一帧双指距离（用于连续缩放）
    private bool isRotating;                  // 旋转操作标志
    private bool isZooming;                   // 缩放操作标志
    private Vector3 originalScale;            // 记录物体原始缩放

    void Start()
    {
        // 保存物体初始缩放值
        originalScale = transform.localScale;
    }

    void Update()
    {
        // 获取当前触摸数量
        int touchCount = Input.touchCount;

        // 单指触摸：旋转物体
        if (touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                // 重置旋转状态
                isRotating = false;
                lastSingleTouchPosition = touch.position;
            }
            else if (touch.phase == TouchPhase.Moved)
            {
                // 计算触摸点位移
                Vector2 deltaPosition = touch.position - lastSingleTouchPosition;

                // 防抖处理：当移动距离超过阈值才开始旋转
                if (!isRotating)
                {
                    if (deltaPosition.magnitude > rotationDeadZone)
                    {
                        isRotating = true;
                    }
                }

                if (isRotating)
                {
                    // 根据设置反转旋转方向
                    float direction = invertRotation ? -1f : 1f;

                    // 计算旋转量（Y轴对应水平移动）
                    float rotationY = deltaPosition.x * rotationSpeed * direction;

                    // 应用旋转（使用世界坐标旋转）
                    transform.Rotate(0, rotationY, 0);
                }

                // 更新记录的位置
                lastSingleTouchPosition = touch.position;
            }
            else if (touch.phase == TouchPhase.Ended)
            {
                isRotating = false;
            }
        }
        // 双指触摸：缩放物体
        else if (touchCount == 2)
        {
            Touch touch1 = Input.GetTouch(0);
            Touch touch2 = Input.GetTouch(1);

            // 计算当前双指距离
            float currentTouchDistance = Vector2.Distance(touch1.position, touch2.position);

            if (touch1.phase == TouchPhase.Began || touch2.phase == TouchPhase.Began)
            {
                // 重置缩放状态
                isZooming = false;

                // 记录初始双指距离
                initialTouchDistance = currentTouchDistance;
                lastTouchDistance = currentTouchDistance;
            }
            else if (touch1.phase == TouchPhase.Moved || touch2.phase == TouchPhase.Moved)
            {
                // 防抖处理：当缩放比例变化超过阈值才开始缩放
                if (!isZooming)
                {
                    float scaleChange = Mathf.Abs(currentTouchDistance - initialTouchDistance);
                    if (scaleChange > zoomDeadZone * initialTouchDistance)
                    {
                        isZooming = true;
                    }
                }

                if (isZooming)
                {
                    // 计算距离变化量
                    float distanceDelta = currentTouchDistance - lastTouchDistance;

                    // 计算缩放因子
                    float scaleFactor = 1 + distanceDelta * zoomSpeed;

                    // 应用缩放（基于当前缩放）
                    Vector3 newScale = transform.localScale * scaleFactor;

                    // 限制缩放范围（相对于原始尺寸）
                    newScale = RelativeClampScale(newScale);

                    transform.localScale = newScale;
                }

                // 更新记录的距离
                lastTouchDistance = currentTouchDistance;
            }
            else if (touch1.phase == TouchPhase.Ended || touch2.phase == TouchPhase.Ended)
            {
                isZooming = false;
            }
        }
        else
        {
            // 当没有触摸或触摸数量变化时重置状态
            isRotating = false;
            isZooming = false;
        }
    }

    // 相对原始尺寸的缩放限制
    private Vector3 RelativeClampScale(Vector3 scale)
    {
        // 计算相对于原始尺寸的缩放比例
        float relativeScale = scale.x / originalScale.x;

        // 限制缩放比例在[minScale, maxScale]范围内
        relativeScale = Mathf.Clamp(relativeScale, minScale, maxScale);

        // 返回实际缩放值
        return originalScale * relativeScale;
    }
}