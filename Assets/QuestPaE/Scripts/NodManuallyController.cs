using UnityEngine;
using System.Collections;

namespace QuestPaE.Scripts
{
    public class NodManuallyController : MonoBehaviour
    {
        public Transform headBone;
        public float nodAngle = 15f; // 点头角度
        public float speed = 2f; // 动画速度
        public float delayTime = 3f;

        public void Nod()
        {
            StartCoroutine(NodAfterDelay());
        }

        private IEnumerator NodAfterDelay()
        {
            yield return new WaitForSeconds(delayTime);

            // 点两下
            yield return StartCoroutine(NodAnimation());
            yield return StartCoroutine(NodAnimation());
        }

        // 让头上下点一次
        private IEnumerator NodAnimation()
        {
            Quaternion startRot = headBone.localRotation;
            Quaternion downRot = startRot * Quaternion.Euler(nodAngle, 0, 0);

            // ↓头
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * speed;
                headBone.localRotation = Quaternion.Lerp(startRot, downRot, t);
                yield return null;
            }

            // ↑回去
            t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * speed;
                headBone.localRotation = Quaternion.Lerp(downRot, startRot, t);
                yield return null;
            }
        }
    }
}