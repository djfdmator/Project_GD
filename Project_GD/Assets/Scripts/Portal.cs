using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Unity.VisualScripting;

public class Portal : MonoBehaviour
{
    [SerializeField] 
    private string sceneName;
    [SerializeField] 
    private AudioSource audioSource;    
    public void Go()
    {
        StartCoroutine(GoRoutine());
    }

    private IEnumerator GoRoutine()
    {
        audioSource.Play();
        yield return new WaitForSeconds(2.0f);
        SceneManager.LoadScene(sceneName);
    }
}
