using UnityEngine;
using UnityEngine.SceneManagement;

public class Portal : MonoBehaviour
{
    [SerializeField] 
    private string sceneName;

    public void Go()
    {
        SceneManager.LoadScene(sceneName);
    }
}
