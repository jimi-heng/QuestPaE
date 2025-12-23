using UnityEngine;
using System.Collections;

namespace QuestPaE.Scripts
{
    // 基础控制器，包含walk, wave和头部偏转等基础功能
    public class BaseController : MonoBehaviour
    {
        protected Animator animator;

        [SerializeField] private Transform headBone;
        [SerializeField] private Transform target;
        [SerializeField] private float turnDirection; // 转向角度
        [SerializeField] protected float delay; // 触发的延迟时间
        [SerializeField] protected float walkTime; // walk的持续时间
        [SerializeField] protected float waveTime; // wave的持续时间

        public void Start()
        {
            animator = GetComponent<Animator>(); //获取动画
        }

        public void Stand()
        {
            animator.SetTrigger("standTrigger");
        }

        public void LateUpdate()
        {
            // 优先执行手动转向
            if (turnDirection != 0)
            {
                headBone.localRotation *= Quaternion.Euler(10f, turnDirection, 0);
            }
            else
            {
                // 如果target和headBone不为空，看向target
                if (target != null && headBone != null)
                {
                    headBone.LookAt(target);
                }
            }
        }

        public void StartWalk()
        {
            StartCoroutine(WalkAfterDelay(delay, walkTime));
        }

        protected IEnumerator WalkAfterDelay(float delay, float walkTime)
        {
            yield return new WaitForSeconds(delay); // 延迟触发
            animator.SetTrigger("walkTrigger");
            yield return new WaitForSeconds(walkTime); // 切换到站立姿态
            animator.SetTrigger("standTrigger");
        }

        public void StartWave()
        {
            StartCoroutine(WaveAfterDelay(delay, waveTime));
        }

        protected IEnumerator WaveAfterDelay(float delay, float waveTime)
        {
            yield return new WaitForSeconds(delay); //延迟触发
            animator.SetTrigger("waveTrigger");
            yield return new WaitForSeconds(waveTime); // 切换到站立姿态
            animator.SetTrigger("standTrigger");
        }
    }
}