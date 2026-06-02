using UnityEngine;

// Student template: replace BuildTree() with an attacker or defender BT strategy.
public class StudentBTStrategy : MonoBehaviour
{
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;
    [SerializeField] private float attackDistance = 1.8f;
    [SerializeField] private float detectDistance = 5.0f;
    [SerializeField] private float facingAngle = 45f;
    [SerializeField] private float lowHealthRatio = 0.4f;
    [SerializeField] private float baitDuration = 1.5f;

    private BTNode root;
    private bool baitActive;
    private bool baitSawTargetAttack;
    private float baitEndTime;

    private void Awake()
    {
        FillDefaultReferences();
        BuildTree();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    private void Update()
    {
        if (!CanTick())
        {
            return;
        }

        root.Tick();
    }

    private void BuildTree()
    {
        // [고급 요소 1] 바쁘지 않을 때만 행동 트리 작동
        BTNode isNotBusy = new DecoratorNode(
            new ConditionNode(() => actionController.IsBusy),
            status => status == BTNodeStatus.Success ? BTNodeStatus.Failure : BTNodeStatus.Success
        );

        // ---------------------------------------------------------
        // [1순위] 마무리 돌진 (FinisherRush): 상대 체력이 낮으면 빠르게 붙어서 끝냅니다.
        // ---------------------------------------------------------
        BTNode finisherRush = new SequenceNode(
            new ConditionNode(() => target.CurrentHealthRatio <= lowHealthRatio),
            new ConditionNode(() => DistanceToTarget() <= detectDistance),
            new ActionNode(FinisherRush)
        );

        // ---------------------------------------------------------
        // [2순위] 유도 후 응징 (BaitAndPunish): 상대 공격을 빼고 빈틈에 반격합니다.
        // ---------------------------------------------------------
        BTNode baitAndPunish = new SequenceNode(
            new ConditionNode(() => DistanceToTarget() <= detectDistance),
            new ConditionNode(() => cooldownSystem.IsAttackReady()),
            new ConditionNode(() => baitActive || IsTargetAttackReady() || IsTargetAttacking()),
            new ActionNode(BaitAndPunish)
        );

        // ---------------------------------------------------------
        // [3순위] 심리전 공격 (RandomMixup): 랜덤은 타격 시에만 사용합니다!
        // ---------------------------------------------------------
        BTNode randomAttackMixup = new SequenceNode(
            // 사거리 내에 있고, 쿨타임이 돌았을 때 진입
            new ConditionNode(() => DistanceToTarget() <= attackDistance),
            new ConditionNode(() => cooldownSystem.IsAttackReady()),
            new ConditionNode(() => IsFacingTarget(facingAngle)),

            // [고급 요소 2] 주사위 굴리기: 어떻게 팰 것인가?
            new RandomSelectorNode(
                // 50% 확률: 자비 없이 즉시 공격 (Pressure)
                new ActionNode(() => {
                    actionController.Attack();
                    return BTNodeStatus.Success;
                }),
                // 50% 확률: 방어를 1번 튕겨서 간을 본 뒤 턴 넘기기 (Bait)
                new SequenceNode(
                    new ConditionNode(() => cooldownSystem.IsBlockReady()),
                    new ActionNode(() => {
                        actionController.Block(DirectionToTarget());
                        return BTNodeStatus.Success;
                    })
                )
            )
        );

        // ---------------------------------------------------------
        // [4순위] 전술적 이동 (부드러운 시선 처리 적용)
        // ---------------------------------------------------------
        BTNode tacticalMovement = new SequenceNode(
            new ConditionNode(() => DistanceToTarget() <= detectDistance),
            new SelectorNode(
                // 조건 1. 게걸음 (Circle Strafe)
                new SequenceNode(
                    new ConditionNode(() =>
                        (target.ActionController != null && target.ActionController.IsBlocking) ||
                        DistanceToTarget() <= attackDistance
                    ),
                    new ActionNode(() => {
                        Vector3 dir = DirectionToTarget();

                        // Face(dir) 대신 UpdateRotationLock(dir)을 사용하여 시선을 부드럽게 고정!
                        actionController.UpdateRotationLock(dir);

                        Vector3 sideDir = Vector3.Cross(Vector3.up, dir).normalized;
                        actionController.Move(sideDir);
                        return BTNodeStatus.Success;
                    })
                ),

                // 조건 2. 직진 압박
                new ActionNode(() => {
                    Vector3 dir = DirectionToTarget();

                    // 여기도 마찬가지로 부드럽게 고정
                    actionController.UpdateRotationLock(dir);

                    actionController.Move(dir);
                    return BTNodeStatus.Success;
                })
            )
        );

        // ---------------------------------------------------------
        // [최종 조립] root 변수에 완성된 트리를 반드시 연결해줘야 작동합니다!
        // ---------------------------------------------------------
        root = new SequenceNode(
            isNotBusy,
            new SelectorNode(
                finisherRush,      // 1순위: 저체력 상대 마무리
                baitAndPunish,     // 2순위: 공격 유도 후 반격
                randomAttackMixup, // 3순위: 타격 및 심리전
                tacticalMovement   // 4순위: 접근 및 회전 이동
            )
        );
    }

    private bool CanTick()
    {
        return root != null
            && self != null
            && target != null
            && actionController != null
            && !self.IsDead
            && !target.IsDead;
    }

    private Vector3 DirectionToTarget()
    {
        if (target == null)
        {
            return transform.forward;
        }

        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= 0.0001f ? transform.forward : offset.normalized;
    }

    private float DistanceToTarget()
    {
        if (target == null)
        {
            return float.MaxValue;
        }

        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private bool IsFacingTarget(float maxAngle)
    {
        Vector3 direction = DirectionToTarget();
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return Vector3.Angle(forward, direction) <= maxAngle;
    }

    private BTNodeStatus FinisherRush()
    {
        ResetBaitState();

        Vector3 dir = DirectionToTarget();
        actionController.Face(dir);

        if (IsTargetAttacking())
        {
            TryBlockOrDodge(dir);
            return BTNodeStatus.Success;
        }

        if (IsTargetBlocking() || IsTargetInvincible())
        {
            if (DistanceToTarget() > attackDistance)
            {
                actionController.Move(dir);
            }

            return BTNodeStatus.Success;
        }

        if (DistanceToTarget() > attackDistance)
        {
            actionController.Move(dir);
            return BTNodeStatus.Success;
        }

        if (cooldownSystem.IsAttackReady())
        {
            actionController.Attack();
        }

        return BTNodeStatus.Success;
    }

    private BTNodeStatus BaitAndPunish()
    {
        if (!baitActive)
        {
            baitActive = true;
            baitSawTargetAttack = false;
            baitEndTime = Time.time + baitDuration;
        }

        if (Time.time > baitEndTime || DistanceToTarget() > detectDistance)
        {
            ResetBaitState();
            return BTNodeStatus.Failure;
        }

        Vector3 dir = DirectionToTarget();
        actionController.UpdateRotationLock(dir);

        if (IsTargetAttacking())
        {
            baitSawTargetAttack = true;
            StrafeAroundTarget(dir);
            TryBlockOrDodge(dir);
            return BTNodeStatus.Running;
        }

        if (IsTargetBlocking())
        {
            ResetBaitState();
            return BTNodeStatus.Failure;
        }

        if (baitSawTargetAttack)
        {
            if (DistanceToTarget() > attackDistance || !IsFacingTarget(facingAngle))
            {
                actionController.Move(dir);
                return BTNodeStatus.Running;
            }

            ResetBaitState();
            actionController.Attack();
            return BTNodeStatus.Success;
        }

        if (DistanceToTarget() <= attackDistance)
        {
            StrafeAroundTarget(dir);
        }
        else
        {
            actionController.Move(dir);
        }

        return BTNodeStatus.Running;
    }

    private void StrafeAroundTarget(Vector3 dir)
    {
        Vector3 sideDir = Vector3.Cross(Vector3.up, dir).normalized;
        actionController.Move(sideDir);
    }

    private void TryBlockOrDodge(Vector3 dir)
    {
        if (cooldownSystem.IsBlockReady())
        {
            actionController.Block(dir);
            return;
        }

        if (cooldownSystem.IsDodgeReady())
        {
            actionController.Dodge(-dir);
        }
    }

    private void ResetBaitState()
    {
        baitActive = false;
        baitSawTargetAttack = false;
        baitEndTime = 0f;
    }

    private bool IsTargetAttacking()
    {
        return target.ActionController != null && target.ActionController.IsAttacking;
    }

    private bool IsTargetBlocking()
    {
        return target.ActionController != null && target.ActionController.IsBlocking;
    }

    private bool IsTargetInvincible()
    {
        return target.ActionController != null && target.ActionController.IsInvincible;
    }

    private bool IsTargetAttackReady()
    {
        return target.CooldownSystem != null && target.CooldownSystem.IsAttackReady();
    }

    private void FillDefaultReferences()
    {
        if (self == null)
        {
            self = GetComponent<CombatCharacter>();
        }

        if (actionController == null)
        {
            actionController = GetComponent<CombatActionController>();
        }

        if (cooldownSystem == null)
        {
            cooldownSystem = GetComponent<CooldownSystem>();
        }
    }
}
