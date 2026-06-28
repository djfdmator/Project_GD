using System;
using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────
//  플레이어 본체 (State Machine Context)
//  - 공통 데이터/입력/헬퍼를 들고 있고
//  - 현재 상태 객체 하나만 유지하면서 전환을 관리한다.
// ─────────────────────────────────────────────
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Move")] public float moveSpeed = 6f;
    public float acceleration = 60f;
    public float deceleration = 80f;
    [Range(0f, 1f)] public float airControl = 0.65f; // 공중에서의 좌우 제어력

    [Header("Jump")] public float jumpForce = 12f;
    public float fallMultiplier = 2.5f; // 하강 시 더 빨리 떨어지는 정도
    public float lowJumpMultiplier = 3f; // 점프키를 짧게 떼면 빨리 하강
    public float coyoteTime = 0.1f; // 발판에서 벗어난 직후에도 점프 허용
    public float jumpBuffer = 0.1f; // 착지 직전에 누른 점프 입력을 저장

    [Header("Drop Through")]
    public float dropThroughTime = 0.4f;  
    
    [Header("Climb")] public float climbSpeed = 4f;

    [Header("Checks")] public Transform groundCheck; // 발밑에 빈 GameObject 하나 만들어 할당
    public float groundCheckRadius = 0.2f;
    public LayerMask groundLayer;
    public LayerMask ladderLayer;

    [Header("Animation")] public Animator animator; // 캐릭터 Animator (비우면 자식에서 자동 검색)

    // ── 컴포넌트 / 입력 (상태 클래스에서 접근) ──
    [HideInInspector] public Rigidbody2D rb; 
    public Collider2D playerCol;
    [HideInInspector] public float inputX, inputY;
    [HideInInspector] public bool jumpPressed, jumpHeld;
    [HideInInspector] public float defaultGravity;
    [HideInInspector] public bool facingRight = true;

    // ── 타이머 ──
    [HideInInspector] public float coyoteCounter;
    [HideInInspector] public float jumpBufferCounter;

    // ── 상태머신 ──
    public PlayerState CurrentState { get; private set; }
    public IdleState idleState;
    public MoveState moveState;
    public AirState airState;
    public ClimbState climbState;

    private Portal _portal;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        defaultGravity = rb.gravityScale;
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // 상태 객체는 한 번만 생성해서 재사용 (GC 부담 없음)
        idleState = new IdleState(this);
        moveState = new MoveState(this);
        airState = new AirState(this);
        climbState = new ClimbState(this);
    }

    void Start()
    {
        ChangeState(idleState);
    }

    void Update()
    {
        ReadInput();
        UpdateTimers();
        CurrentState.LogicUpdate(); // 입력 판단 / 상태 전환
        UpdateAnimator(); // 애니메이터 파라미터 갱신
    }

    void FixedUpdate()
    {
        CurrentState.PhysicsUpdate(); // 실제 물리 이동
    }

    public void ChangeState(PlayerState newState)
    {
        CurrentState?.Exit();
        CurrentState = newState;
        CurrentState.Enter();
    }

    // ───────────── 입력 / 타이머 ─────────────

    void ReadInput()
    {
        // 좌우 이동 (방향키). 둘 다 누르면 0
        inputX = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                 - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);

        // 상하 (방향키, 사다리용)
        inputY = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                 - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);

        // 점프 (컨트롤 키)
        jumpPressed = Input.GetKeyDown(KeyCode.LeftAlt);
        jumpHeld = Input.GetKey(KeyCode.LeftAlt);
    }

    void UpdateTimers()
    {
        if (IsInPortal() && inputY >= 1f)
        {
            _portal.Go();
        }

        // 코요테 타임: 땅에 닿으면 가득 채우고, 떨어지면 줄어든다
        if (IsGrounded()) coyoteCounter = coyoteTime;
        else coyoteCounter -= Time.deltaTime;

        // 점프 버퍼: 점프키를 누르면 잠시 기억해둔다
        if (jumpPressed) jumpBufferCounter = jumpBuffer;
        else jumpBufferCounter -= Time.deltaTime;
    }

    // ───────────── 공용 헬퍼 ─────────────

    public bool IsGrounded()
    {
        return Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
    }

    public bool IsInPortal()
    {
        return _portal != null;
    }

    public Ladder GetLadder()
    {
        Collider2D col = Physics2D.OverlapCircle(transform.position, 0.2f, ladderLayer);
        return col ? col.GetComponent<Ladder>() : null;
    }

    // 좌우 이동 (control: 1=지상 정상, airControl=공중)
    public void HandleHorizontalMovement(float control = 1f)
    {
        float targetSpeed = inputX * moveSpeed;
        float speedDiff = targetSpeed - rb.linearVelocity.x;
        float baseRate = Mathf.Abs(targetSpeed) > 0.01f ? acceleration : deceleration;
        float movement = speedDiff * baseRate * control * Time.fixedDeltaTime;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x + movement, rb.linearVelocity.y);

        HandleFlip();
    }

    public void HandleFlip()
    {
        if (inputX > 0 && !facingRight) Flip();
        else if (inputX < 0 && facingRight) Flip();
    }

    void Flip()
    {
        facingRight = !facingRight;
        Vector3 s = transform.localScale;
        s.x *= -1;
        transform.localScale = s;
    }

    public void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        coyoteCounter = 0; // 연속 점프 방지
        jumpBufferCounter = 0;
    }
    
    // 아래키 + 점프: 발밑이 한방향 발판이면 충돌을 잠깐 꺼서 뚫고 내려감.
    // 성공하면 true (일반 점프 대신 실행됨).
    public bool TryDropThrough()
    {
        if (inputY >= 0) return false; // 아래키 안 눌림

        // 발밑 발판 탐지
        Collider2D platform = Physics2D.OverlapCircle(
            groundCheck.position, groundCheckRadius, groundLayer);
        if (platform == null) return false;

        // 한방향 발판(PlatformEffector2D)일 때만 통과 허용
        if (platform.GetComponent<PlatformEffector2D>() == null) return false;

        coyoteCounter = 0;        // 뚫는 직후 공중에서 재점프 방지
        jumpBufferCounter = 0;
        StartCoroutine(DisableCollision(platform));
        return true;
    }

    IEnumerator DisableCollision(Collider2D platform)
    {
        Physics2D.IgnoreCollision(playerCol, platform, true);
        yield return new WaitForSeconds(dropThroughTime);
        Physics2D.IgnoreCollision(playerCol, platform, false);
    }

    // 메이플 점프 손맛: 올라갈 땐 가볍게, 내려올 땐 묵직하게
    public void ApplyBetterGravity()
    {
        if (rb.linearVelocity.y < 0)
        {
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y *
                                 (fallMultiplier - 1f) * Time.fixedDeltaTime;
        }
        else if (rb.linearVelocity.y > 0 && !jumpHeld)
        {
            rb.linearVelocity += Vector2.up * Physics2D.gravity.y *
                                 (lowJumpMultiplier - 1f) * Time.fixedDeltaTime;
        }
    }

    // 현재 물리/상태 값을 애니메이터 파라미터로 흘려보낸다.
    // 코드 FSM이 진짜 상태를 알고 있으므로 여기선 값만 전달하면 끝.
    void UpdateAnimator()
    {
        if (animator == null) return;

        animator.SetFloat("Speed", Mathf.Abs(rb.linearVelocity.x));
        animator.SetFloat("yVelocity", rb.linearVelocity.y);
        animator.SetBool("IsGrounded", IsGrounded());
        animator.SetBool("IsClimbing", CurrentState == climbState);

        // 사다리에서 멈추면 등반 애니도 멈추도록 (Climb 스테이트 Speed 배수에 연결)
        animator.SetFloat("ClimbSpeed", Mathf.Abs(inputY));
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Portal"))
        {
            _portal = other.gameObject.GetComponent<Portal>();
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Portal"))
        {
            _portal = null;
        }
    }

#if UNITY_EDITOR
    // 씬 뷰에서 groundCheck 범위를 시각적으로 확인
    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
#endif
}