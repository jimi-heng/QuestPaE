using UnityEngine;
using System.Collections;

public class RotateObject : MonoBehaviour
{
    public float rotationDuration = 2f;

    public void apply()
    {
        StartCoroutine(RotateOverTime(180f));
    }

    private IEnumerator RotateOverTime(float targetAngle)
    {
        float startAngle = transform.rotation.eulerAngles.y;
        float elapsedTime = 0f;

        while (elapsedTime < rotationDuration)
        {
            elapsedTime += Time.deltaTime;
            float currentAngle = Mathf.Lerp(startAngle, startAngle + targetAngle, elapsedTime / rotationDuration);
            transform.rotation = Quaternion.Euler(0, currentAngle, 0);
            yield return null;
        }

        transform.rotation = Quaternion.Euler(0, startAngle + targetAngle, 0);
    }
}

