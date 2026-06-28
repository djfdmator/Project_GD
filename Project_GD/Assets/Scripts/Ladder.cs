using UnityEngine;

// ─────────────────────────────────────────────
//  사다리 / 로프 오브젝트에 붙이는 스크립트
//  BoxCollider2D(Is Trigger 체크) + Layer = Ladder 로 세팅
// ─────────────────────────────────────────────
[RequireComponent(typeof(BoxCollider2D))]
public class Ladder : MonoBehaviour
{
    public float topY;     // 사다리 맨 위 y
    public float bottomY;  // 사다리 맨 아래 y
    public float centerX;  // 중심 x (붙을 때 스냅용)

    void Awake()
    {
        var col = GetComponent<BoxCollider2D>();
        topY = col.bounds.max.y;
        bottomY = col.bounds.min.y;
        centerX = transform.position.x;
    }
}
