using System;
using UnityEngine;

namespace QuestPaE.Scripts
{
    public class OpenController : MonoBehaviour
    {
        private Animator animator;
        private bool state = false;

        void Start()
        {
            animator = GetComponent<Animator>();
        }

        public void OpenCurtain()
        {
            if (state == false)
            {
                animator.SetTrigger("openTrigger");
                state = true;
            }
        }

        public void CloseCurtain()
        {
            if (state == true)
            {
                animator.SetTrigger("closeTrigger");
                state = false;
            }
        }
    }
}