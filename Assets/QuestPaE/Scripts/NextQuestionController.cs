using UnityEngine;

public class NextQuestionController : MonoBehaviour
{
    [SerializeField] private GameObject[] gameObjects;
    private int num = 0;

    public void ClickNext()
    {
        if (num < gameObjects.Length - 1)
        {
            gameObjects[num].SetActive(false); // 隐藏上一个问题
            num++;
            gameObjects[num].SetActive(true); // 显示当前问题
        }
    }

    // 重置问题的显示
    public void MenuReset()
    {
        num = 0;
        // 显示第一个问题
        gameObjects[0].SetActive(true);

        // 隐藏剩下的问题
        for (int i = 1; i < gameObjects.Length; i++)
        {
            gameObjects[i].SetActive(false);
        }
    }
}
