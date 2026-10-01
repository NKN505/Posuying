using UnityEngine;

public class TestAnim : MonoBehaviour
{
    void Start()
    {
        Animation anim = GetComponent<Animation>();
        if (anim != null)
        {
            Debug.Log("Animation encontrada, clips: " + anim.GetClipCount());
            anim.Play();
        }
        else
        {
            Debug.Log("No se encontro Animation");
        }
    }
}
