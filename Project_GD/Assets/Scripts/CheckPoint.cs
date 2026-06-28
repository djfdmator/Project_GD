using UnityEngine;

public class CheckPoint : MonoBehaviour
{
    [SerializeField]
    private GameObject _deadZone;
    
    public void Set()
    {
        _deadZone.SetActive(true);
    }
}
