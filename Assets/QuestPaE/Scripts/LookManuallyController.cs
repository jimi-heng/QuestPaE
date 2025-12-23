using UnityEngine;
using System.Collections;
using Unity.VisualScripting;

namespace QuestPaE.Scripts
{
    public class LookManuallyController : MonoBehaviour
    {
        [SerializeField] private Transform bodyBone;
        [SerializeField] private Transform headBone;
        [SerializeField] private float lookAngle;
        [SerializeField] private float headAngle;
        [SerializeField] private float lookSpeed;
        [SerializeField] private float delayTime;

        public void Look()
        {
            StartCoroutine(LookAfterDelay());
        }

        private IEnumerator LookAfterDelay()
        {
            yield return new WaitForSeconds(delayTime);
            
            Quaternion  startRot = bodyBone.localRotation;
            Quaternion downRot = startRot * Quaternion.Euler(lookAngle, 0, 0);
            
            Quaternion startHead = headBone.localRotation;
            Quaternion downHead = startHead*Quaternion.Euler(0, headAngle ,0);
            
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * lookSpeed;
                bodyBone.localRotation = Quaternion.Lerp(startRot, downRot, t);
                headBone.localRotation = Quaternion.Lerp(startHead, downHead, t);
                yield return null;
            }
        }
    }
}