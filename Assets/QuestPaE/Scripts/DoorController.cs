using UnityEngine;
using System.Collections;

namespace QuestPaE.Scripts
{
    public class DoorController : MonoBehaviour
    {
        public Transform rightDoor;
        public Transform leftDoor;

        public float openDistance = 0.000339f; // 开门的距离
        public float openTime = 1.0f; // 开门的耗时

        public float peopleWaitTime = 1.0f;
        public float doorWaitTime = 1.0f;

        public NurseController nurseController;

        private Vector3 leftStart; // 左门的初始位置
        private Vector3 rightStart; // 右门的初始位置

        private bool isDoorMoving = false; // 是否只有一个协程

        private void Start()
        {
            // 获取初始位置，便于位置的重置
            leftStart = leftDoor.localPosition;
            rightStart = rightDoor.localPosition;
        }

        public void OpenDoor()
        {
            // 如果还没有协程，启动一个
            if (!isDoorMoving)
            {
                StartCoroutine(MoveDoor());
            }
        }

        IEnumerator MoveDoor()
        {
            isDoorMoving = true;
            // 用户开始行走
            yield return new WaitForSeconds(peopleWaitTime);
            nurseController.StartWalk();
            //门开始打开
            yield return new WaitForSeconds(doorWaitTime);
            Vector3 leftTarget = leftStart + Vector3.left * openDistance;
            Vector3 rightTarget = rightStart + Vector3.right * openDistance;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / openTime;
                leftDoor.localPosition = Vector3.Lerp(leftStart, leftTarget, t);
                rightDoor.localPosition = Vector3.Lerp(rightStart, rightTarget, t);
                yield return null;
            }

            isDoorMoving = false;
        }

        public void CloseDoor()
        {
            // 重置为初始位置
            leftDoor.localPosition = leftStart;
            rightDoor.localPosition = rightStart;
        }
    }
}
