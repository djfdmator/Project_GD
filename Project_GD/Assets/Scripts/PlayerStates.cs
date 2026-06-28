using UnityEngine;

// ─────────────────────────────────────────────
//  상태 베이스 클래스
//  MonoBehaviour가 아니라서 한 파일에 모아둘 수 있다.
// ─────────────────────────────────────────────
public abstract class PlayerState
{
    protected PlayerController player;

    public PlayerState(PlayerController player)
    {
        this.player = player;
    }

    public virtual void Enter() { }          // 상태 진입 시 1회
    public virtual void LogicUpdate() { }    // Update에서 호출 (입력/전환 판단)
    public virtual void PhysicsUpdate() { }  // FixedUpdate에서 호출 (물리)
    public virtual void Exit() { }           // 상태 이탈 시 1회
}


// ───────────── Idle: 멈춰 서 있는 상태 ─────────────
public class IdleState : PlayerState
{
    public IdleState(PlayerController player) : base(player) { }

    public override void PhysicsUpdate()
    {
        player.HandleHorizontalMovement(); // 감속으로 부드럽게 정지
    }

    public override void LogicUpdate()
    {
        if (TryJump()) return;
        if (TryClimb()) return;

        // 발판에서 벗어나면 공중 상태로
        if (!player.IsGrounded())
        {
            player.ChangeState(player.airState);
            return;
        }
        // 좌우 입력이 있으면 이동 상태로
        if (Mathf.Abs(player.inputX) > 0.1f)
        {
            player.ChangeState(player.moveState);
        }
    }

    // Idle/Move가 공유하는 전환 로직 ─ 점프
    bool TryJump()
    {
        if (player.jumpBufferCounter > 0 /*&& player.coyoteCounter > 0*/)
        {
            // 아래키 + 점프 = 한방향 발판 뚫고 내려가기 (성공 시 일반 점프 생략)
            if (!player.TryDropThrough())
                player.Jump();

            player.ChangeState(player.airState);
            return true;
        }
        return false;
    }

    // Idle/Move가 공유하는 전환 로직 ─ 사다리 잡기
    bool TryClimb()
    {
        Ladder ladder = player.GetLadder();
        if (ladder != null && Mathf.Abs(player.inputY) > 0.1f)
        {
            player.climbState.SetLadder(ladder);
            player.ChangeState(player.climbState);
            return true;
        }
        return false;
    }
}


// ───────────── Move: 좌우 이동 상태 ─────────────
public class MoveState : PlayerState
{
    public MoveState(PlayerController player) : base(player) { }

    public override void PhysicsUpdate()
    {
        player.HandleHorizontalMovement();
    }

    public override void LogicUpdate()
    {
        // 점프
        if (player.jumpBufferCounter > 0 /*&& player.coyoteCounter > 0*/)
        {
            if (!player.TryDropThrough())
                player.Jump();

            player.ChangeState(player.airState);
            return;
        }
        // 사다리
        Ladder ladder = player.GetLadder();
        if (ladder != null && Mathf.Abs(player.inputY) > 0.1f)
        {
            player.climbState.SetLadder(ladder);
            player.ChangeState(player.climbState);
            return;
        }
        // 낙하
        if (!player.IsGrounded())
        {
            player.ChangeState(player.airState);
            return;
        }
        // 입력이 멈추면 Idle로
        if (Mathf.Abs(player.inputX) < 0.1f)
        {
            player.ChangeState(player.idleState);
        }
    }
}


// ───────────── Air: 점프 / 낙하 (공중) 상태 ─────────────
//  점프 로직이 전부 여기 들어있다.
public class AirState : PlayerState
{
    public AirState(PlayerController player) : base(player) { }

    public override void PhysicsUpdate()
    {
        player.HandleHorizontalMovement(player.airControl); // 공중 제어
        player.ApplyBetterGravity();                        // 점프 손맛
    }

    public override void LogicUpdate()
    {
        // 공중에서 사다리 잡기
        Ladder ladder = player.GetLadder();
        if (ladder != null && Mathf.Abs(player.inputY) > 0.1f)
        {
            player.climbState.SetLadder(ladder);
            player.ChangeState(player.climbState);
            return;
        }

        // 착지 판정 (하강 중일 때만)
        if (player.IsGrounded() && player.rb.linearVelocity.y <= 0.01f)
        {
            if (Mathf.Abs(player.inputX) > 0.1f)
                player.ChangeState(player.moveState);
            else
                player.ChangeState(player.idleState);
        }
    }
}


// ───────────── Climb: 사다리 타기 상태 ─────────────
public class ClimbState : PlayerState
{
    private Ladder ladder;

    public ClimbState(PlayerController player) : base(player) { }

    public void SetLadder(Ladder ladder)
    {
        this.ladder = ladder;
    }

    public override void Enter()
    {
        player.rb.gravityScale = 0f;       // 중력 끄기
        player.rb.linearVelocity = Vector2.zero;

        // 사다리 중심으로 x 스냅 (메이플처럼 딱 붙는 느낌)
        Vector3 pos = player.transform.position;
        player.transform.position = new Vector3(ladder.centerX, pos.y, pos.z);
    }

    public override void PhysicsUpdate()
    {
        // 좌우 속도 죽이고 상하로만 이동
        player.rb.linearVelocity = new Vector2(0f, player.inputY * player.climbSpeed);
    }

    public override void LogicUpdate()
    {
        // 점프키로 사다리에서 튕겨 나오기
        if (player.jumpPressed)
        {
            player.ChangeState(player.airState); // Exit에서 중력 복구됨
            player.rb.linearVelocity = new Vector2(player.inputX * player.moveSpeed, 5f);
            return;
        }

        // 사다리 끝에 도달하면 자동으로 내려서기
        float y = player.transform.position.y;
        bool reachedTop    = y >= ladder.topY    && player.inputY > 0;
        bool reachedBottom = y <= ladder.bottomY && player.inputY < 0;
        if (reachedTop || reachedBottom)
        {
            player.ChangeState(player.idleState);
        }
    }

    public override void Exit()
    {
        player.rb.gravityScale = player.defaultGravity; // 중력 복구
    }
}
