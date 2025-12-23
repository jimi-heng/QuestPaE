using UnityEngine;

namespace QuestPaE.Scripts
{
    public class OffsetController : MonoBehaviour
    {
        public float offsetX = 0.05f;
        public float offsetY = -0.15f;
        public float offsetZ = 0.05f;

        public void OffsetControl()
        {
            transform.localPosition = new Vector3(offsetX, offsetY, offsetZ);
        }
    }
}