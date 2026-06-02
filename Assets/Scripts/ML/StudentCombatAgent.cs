using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(CombatCharacter))]
[RequireComponent(typeof(CooldownSystem))]
[RequireComponent(typeof(CombatActionController))]
public class StudentCombatAgent : Agent
{
    public CombatCharacter self;
    public CombatCharacter opponent;
    public CombatActionController actionController;
    public CooldownSystem cooldownSystem;
    public EpisodeManager episodeManager;

    [Header("Combat Ranges")]
    [SerializeField] private float attackDistance = 1.8f;
    [SerializeField] private float detectDistance = 5.0f;
    [SerializeField] private float facingAngle = 45f;
    [SerializeField] private float lowHealthRatio = 0.4f;
    [SerializeField] private float baitDuration = 1.5f;

    [Header("Reward Tuning")]
    [SerializeField] private float stepPenalty = -0.001f;
    [SerializeField] private float damageDealtRewardScale = 0.02f;
    [SerializeField] private float damageTakenPenaltyScale = -0.025f;
    [SerializeField] private float winReward = 1.0f;
    [SerializeField] private float losePenalty = -1.0f;
    [SerializeField] private float invalidActionPenalty = -0.01f;
    [SerializeField] private float goodTacticalChoiceReward = 0.01f;
    [SerializeField] private float distanceImprovementRewardScale = 0.005f;


    // Action constants for a clear RL action space.
    // Make sure the Behavior Parameters action space in Unity Editor matches these constants.

    // Behavior Parameters must use Discrete Actions with two branches:
    // Branch 0 size 5: movement, Branch 1 size 5: offensive skill.

    private const int MoveNone = 0;
    private const int MoveToward = 1;
    private const int MoveAway = 2;
    private const int MoveStrafeLeft = 3;
    private const int MoveStrafeRight = 4;

    private const int SkillIdle = 0;
    private const int SkillAttack = 1;
    private const int SkillFinisherRush = 2;
    private const int SkillBaitAndPunish = 3;
    private const int SkillPressureAttack = 4;

    private float previousSelfHealth;
    private float previousOpponentHealth;
    private float previousDistance;
    private bool baitActive;
    private bool baitSawOpponentAttack;
    private float baitEndTime;

    public override void Initialize()
    {
        FillDefaultReferences();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    public override void OnEpisodeBegin()
    {
        // Reset or initialize values needed at the start of each episode.
        FillDefaultReferences();

        if (episodeManager != null) episodeManager.ResetEpisode();
        
        previousSelfHealth = self != null ? self.CurrentHealth : 0f;
        previousOpponentHealth = opponent != null ? opponent.CurrentHealth : 0f;
        previousDistance = DistanceToOpponent();

        ResetBaitState();  
        
    }

        private void ResetBaitState()
    {
        baitActive = false;
        baitSawOpponentAttack = false;
        baitEndTime = 0f;
    }

    private Vector3 DirectionToOpponent()
    {
        if (opponent == null)
        {
            return transform.forward;
        }

        Vector3 offset = opponent.transform.position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= 0.0001f ? transform.forward : offset.normalized;
    }
    private float DistanceToOpponent()
    {
        if (opponent == null)
        {
            return detectDistance;
        }

        Vector3 offset = opponent.transform.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private bool IsFacingOpponent(float maxAngle)
    {
        Vector3 direction = DirectionToOpponent();
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return Vector3.Angle(forward, direction) <= maxAngle;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Add observations for the agent to learn from.
        // Example:
        // sensor.AddObservation(self.CurrentHealthRatio);
        // Make sure the Behavior Parameters observation space in Unity Editor matches the number of observations added here.
        
        FillDefaultReferences();

        bool hasSelf = self != null;
        bool hasOpponent = opponent != null;
        Vector3 direction = DirectionToOpponent();
        float distance = DistanceToOpponent();
        float normalizedDistance = detectDistance <= 0f ? 1f : Mathf.Clamp01(distance / detectDistance);

        // Convert world direction to local space
        Vector3 localDirection = transform.InverseTransformDirection(direction);
        Vector3 flatForward = transform.forward;
        Vector3 flatRight = transform.right;
        flatForward.y = 0f;
        flatRight.y = 0f;
        flatForward.Normalize();
        flatRight.Normalize();

        CombatActionController opponentAction = hasOpponent ? opponent.ActionController : null;
        CooldownSystem opponentCooldown = hasOpponent ? opponent.CooldownSystem : null;

        // Behavior Parameters vector observation space size: 21.
        //  0 self health ratio,      1 opponent health ratio,  2 local opponent dir x,     3 local opponent dir z,    4 normalized distance
        //  5 forward dot opp dir,    6 right dot opp dir,      7 self attack ready,        8 self block ready,        9 self dodge ready
        // 10 opponent attack ready, 11 opponent block ready,  12 opponent dodge ready,    13 self attacking,         14 self blocking
        // 15 self invincible,       16 opponent attacking,    17 opponent blocking,       18 opponent invincible,    19 in attack range,       20 in detect range
       
        sensor.AddObservation(hasSelf ? self.CurrentHealthRatio : 0f);
        sensor.AddObservation(hasOpponent ? opponent.CurrentHealthRatio : 0f);

        sensor.AddObservation(localDirection.x);
        sensor.AddObservation(localDirection.z);
        sensor.AddObservation(normalizedDistance);
        sensor.AddObservation(Vector3.Dot(flatForward, direction)); // How much the opponent is in front (positive) or behind (negative).
        sensor.AddObservation(Vector3.Dot(flatRight, direction)); // How much the opponent is to the right (positive) or left (negative).

        sensor.AddObservation(cooldownSystem != null && cooldownSystem.IsAttackReady() ? 1f : 0f);
        sensor.AddObservation(cooldownSystem != null && cooldownSystem.IsBlockReady() ? 1f : 0f);
        sensor.AddObservation(cooldownSystem != null && cooldownSystem.IsDodgeReady() ? 1f : 0f);

        sensor.AddObservation(opponentCooldown != null && opponentCooldown.IsAttackReady() ? 1f : 0f);
        sensor.AddObservation(opponentCooldown != null && opponentCooldown.IsBlockReady() ? 1f : 0f);
        sensor.AddObservation(opponentCooldown != null && opponentCooldown.IsDodgeReady() ? 1f : 0f);

        sensor.AddObservation(actionController != null && actionController.IsAttacking ? 1f : 0f);
        sensor.AddObservation(actionController != null && actionController.IsBlocking ? 1f : 0f);
        sensor.AddObservation(actionController != null && actionController.IsInvincible ? 1f : 0f);

        sensor.AddObservation(opponentAction != null && opponentAction.IsAttacking ? 1f : 0f);
        sensor.AddObservation(opponentAction != null && opponentAction.IsBlocking ? 1f : 0f);
        sensor.AddObservation(opponentAction != null && opponentAction.IsInvincible ? 1f : 0f);

        sensor.AddObservation(distance <= attackDistance ? 1f : 0f);
        sensor.AddObservation(distance <= detectDistance ? 1f : 0f);

    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Get default component references
        FillDefaultReferences();

        // Validate references and state before processing actions.
        if (self == null || opponent == null || actionController == null)
        {
            AddReward(invalidActionPenalty);
            EndEpisode();
            return;
        }

        // End episode if opponent is dead.
        if (self.IsDead || opponent.IsDead)
        {
            ApplyTerminalReward();
            EndEpisode();
            return;
        }

        // End episode if episode manager signals done (e.g., time limit).
        if (episodeManager != null && episodeManager.IsEpisodeDone())
        {
            AddReward(losePenalty * 0.2f);
            EndEpisode();
            return;
        }

        // Read branch 0 for movement and branch 1 for offensive skill.
        int movementAction = actions.DiscreteActions.Length > 0 ? actions.DiscreteActions[0] : MoveNone;
        int skillAction = actions.DiscreteActions.Length > 1 ? actions.DiscreteActions[1] : SkillIdle;

        // TODO: Convert actions into movement or combat commands.
        if (!actionController.IsBusy)
        {
            ApplyMovement(movementAction);
            ApplyOffensiveSkill(skillAction);
        }
        else if (movementAction != MoveNone || skillAction != SkillIdle)
        {
            AddReward(invalidActionPenalty * 0.25f);
        }

        // TODO: Add rewards or penalties based on the result.
        AddReward(stepPenalty);
        AddHealthChangeReward();
        AddDistanceChangeReward(skillAction);

        // TODO: End the episode when needed.
        if (self.IsDead || opponent.IsDead)
        {
            ApplyTerminalReward();
            EndEpisode();
            return;
        }

        previousDistance = DistanceToOpponent();
        previousSelfHealth = self.CurrentHealth;
        previousOpponentHealth = opponent.CurrentHealth;

    }


    private void AddHealthChangeReward()
    {
        float selfDamageTaken = Mathf.Max(0f, previousSelfHealth - self.CurrentHealth);
        float opponentDamageTaken = Mathf.Max(0f, previousOpponentHealth - opponent.CurrentHealth);

        AddReward(opponentDamageTaken * damageDealtRewardScale);
        AddReward(selfDamageTaken * damageTakenPenaltyScale);
    }

    private void AddDistanceChangeReward(int skillAction)
    {
        float currentDistance = DistanceToOpponent();
        float distanceDelta = previousDistance - currentDistance;
        bool shouldApproach =
            skillAction == SkillAttack ||
            skillAction == SkillFinisherRush ||
            skillAction == SkillPressureAttack;

        if (shouldApproach && currentDistance > attackDistance && currentDistance <= detectDistance)
        {
            AddReward(distanceDelta * distanceImprovementRewardScale);
        }

        if (currentDistance > detectDistance)
        {
            AddReward(invalidActionPenalty * 0.25f);
        }
    }

    private void ApplyTerminalReward()
    {
        if (opponent != null && opponent.IsDead && self != null && !self.IsDead)
        {
            AddReward(winReward);
        }
        else if (self != null && self.IsDead && opponent != null && !opponent.IsDead)
        {
            AddReward(losePenalty);
        }
    }

    private void ApplyMovement(int movementAction)
    {
        Vector3 dir = DirectionToOpponent();
        Vector3 sideDir = Vector3.Cross(Vector3.up, dir).normalized;

        switch (movementAction)
        {
            case MoveNone:
                break;

            case MoveToward:
                actionController.Move(dir);
                break;

            case MoveAway:
                actionController.Move(-dir);
                break;

            case MoveStrafeLeft:
                actionController.Move(-sideDir);
                break;

            case MoveStrafeRight:
                actionController.Move(sideDir);
                break;

            default:
                AddReward(invalidActionPenalty);
                break;
        }
    }

    private void ApplyOffensiveSkill(int skillAction)
    {
        switch (skillAction)
        {
            case SkillIdle:
                if (DistanceToOpponent() > detectDistance)
                {
                    AddReward(goodTacticalChoiceReward * 0.25f);
                }
                break;

            case SkillAttack:
                TryBasicAttack();
                break;

            case SkillFinisherRush:
                FinisherRush();
                break;

            case SkillBaitAndPunish:
                BaitAndPunish();
                break;

            case SkillPressureAttack:
                PressureAttack();
                break;

            default:
                AddReward(invalidActionPenalty);
                break;
        }
    }

    private void TryBasicAttack()
    {
        Vector3 dir = DirectionToOpponent();
        actionController.Face(dir);

        if (DistanceToOpponent() <= attackDistance
            && IsFacingOpponent(facingAngle)
            && cooldownSystem != null
            && cooldownSystem.IsAttackReady())
        {
            actionController.Attack();
            return;
        }

        AddReward(invalidActionPenalty);
    }

    private void FinisherRush()
    {
        Vector3 dir = DirectionToOpponent();
        actionController.Face(dir);

        if (opponent.CurrentHealthRatio <= lowHealthRatio)
        {
            AddReward(goodTacticalChoiceReward);
        }
        else
        {
            AddReward(invalidActionPenalty);
        }

        if (IsOpponentAttacking())
        {
            TryBlockOrDodge(dir);
            return;
        }

        if (IsOpponentBlocking() || IsOpponentInvincible())
        {
            if (DistanceToOpponent() > attackDistance)
            {
                actionController.Move(dir);
            }

            return;
        }

        if (DistanceToOpponent() > attackDistance)
        {
            actionController.Move(dir);
            return;
        }

        if (cooldownSystem != null && cooldownSystem.IsAttackReady())
        {
            actionController.Attack();
        }
    }

    private void PressureAttack()
    {
        Vector3 dir = DirectionToOpponent();
        actionController.Face(dir);

        if (IsOpponentBlocking() || IsOpponentInvincible())
        {
            AddReward(invalidActionPenalty);
            return;
        }

        AddReward(goodTacticalChoiceReward * 0.5f);

        if (DistanceToOpponent() > attackDistance)
        {
            actionController.Move(dir);
            return;
        }

        if (cooldownSystem != null && cooldownSystem.IsAttackReady() && IsFacingOpponent(facingAngle))
        {
            actionController.Attack();
        }
    }

    private void BaitAndPunish()
    {
        Vector3 dir = DirectionToOpponent();

        if (!baitActive)
        {
            baitActive = true;
            baitSawOpponentAttack = false;
            baitEndTime = Time.time + baitDuration;
        }

        if (Time.time > baitEndTime || DistanceToOpponent() > detectDistance)
        {
            ResetBaitState();
            AddReward(invalidActionPenalty);
            return;
        }

        if (IsOpponentAttacking())
        {
            baitSawOpponentAttack = true;
            TryBlockOrDodge(dir);
            AddReward(goodTacticalChoiceReward);
            return;
        }

        if (IsOpponentBlocking())
        {
            ResetBaitState();
            AddReward(invalidActionPenalty);
            return;
        }

        if (baitSawOpponentAttack)
        {
            if (DistanceToOpponent() <= attackDistance
                && IsFacingOpponent(facingAngle)
                && cooldownSystem != null
                && cooldownSystem.IsAttackReady())
            {
                ResetBaitState();
                actionController.Attack();
                AddReward(goodTacticalChoiceReward);
                return;
            }

            actionController.Move(dir);
            return;
        }

        Vector3 sideDir = Vector3.Cross(Vector3.up, dir).normalized;
        actionController.Move(sideDir);
    }

    private void TryBlockOrDodge(Vector3 dir)
    {
        if (cooldownSystem != null && cooldownSystem.IsBlockReady())
        {
            actionController.Block(dir);
            return;
        }

        if (cooldownSystem != null && cooldownSystem.IsDodgeReady())
        {
            actionController.Dodge(-dir);
        }
    }

    private bool IsOpponentAttacking()
    {
        return opponent != null
            && opponent.ActionController != null
            && opponent.ActionController.IsAttacking;
    }

    private bool IsOpponentBlocking()
    {
        return opponent != null
            && opponent.ActionController != null
            && opponent.ActionController.IsBlocking;
    }

    private bool IsOpponentInvincible()
    {
        return opponent != null
            && opponent.ActionController != null
            && opponent.ActionController.IsInvincible;
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

        if (episodeManager == null)
        {
            episodeManager = FindFirstObjectByType<EpisodeManager>();
        }
    }
}
