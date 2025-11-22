using UnityEngine;

public class Control : MonoBehaviour
{
    Animator animator;//获取自身的动画器
    public GameObject Text1;//获取文本
    public AudioSource audioSource;//获取音乐播放器
    public AudioClip clip;//该物体选用的音乐或声音
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {

    }
    public void 文字介绍()
    {
        //用于开关文本
        Debug.Log("1");
        if (!Text1.activeInHierarchy)//判断物体是否处于激活状态
        {
            //如果不处于激活状态，执行以下
            Text1.SetActive(true);//打开文本
        }
        else
        {
            Text1.SetActive(false);//关闭文本
        }
        Debug.Log("2");

    }
    public void 播放音频()
    {
        if (audioSource.clip == null)
        {
            audioSource.clip = clip;//将音频源更改为我们上面选择的音频
            audioSource.Play();//播放音频
        }
        else
        {
            audioSource.Stop();//停止播放
            audioSource.clip = null;//清除音频
        }

    }
    public void 播放动画()
    {
        animator.SetTrigger("Attack");//播放一次动画
    }
}
