using UnityEngine;


public class Goal : MonoBehaviour
{
    [SerializeField] 
    private Animator _animator;
    [SerializeField]
    private Collider2D _collider2D;
    
    public void Interact()
    {
        _collider2D.enabled = false;
        _animator.SetBool("Interact", true);
    }
}
