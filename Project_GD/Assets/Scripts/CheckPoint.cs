using UnityEngine;

public class CheckPoint : MonoBehaviour
{
    [SerializeField]
    private GameObject _deadZone;
    [SerializeField]
    private Collider2D _collider2D;
    
    public void Set()
    {
        _collider2D.enabled = false;
        _deadZone.SetActive(true);
    }
}
