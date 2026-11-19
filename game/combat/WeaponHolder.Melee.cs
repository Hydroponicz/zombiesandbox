using System;
using Godot;
using Godot.Collections;
using ZombieSandbox.AI;
using ZombieSandbox.Character;
using ZombieSandbox.Core.Audio;
using ZombieSandbox.Health;

namespace ZombieSandbox.Combat;

public enum MeleePhase
{
    Ready,
    /// <summary>Cocking the swing back.</summary>
    Windup,
    /// <summary>Holding attack after the wind-up: building a heavy swing.</summary>
    Charging,
    /// <summary>The swing itself: this is when it hits.</summary>
    Strike,
    Recovery,
    /// <summary>A blade stuck in a body: attack to start pulling it free.</summary>
    Stuck,
    Pulling,
    /// <summary>Stomping something on the ground (looking down).</summary>
    Stomp,
}

/// <summary>What a melee hit did, for the owner's feedback (camera, rumble, hit-stop).</summary>
public readonly record struct MeleeHitInfo(Vector3 Position, Vector3 Direction, Node? Target, BodyPart Part,
    float Strength, bool Killed, bool Wall, bool Stomp, bool Stuck);

/// <summary>
/// Melee for any character holding a <see cref="MeleeWeapon"/> (and stomping with any non-gun in hand).
/// Driven by the same TriggerHeld / PullTrigger as guns: tap = quick swing, hold = charged heavy swing,
/// looking down = stomp. Hits are rays swept along the swing arc against body-part hitboxes and walls.
/// </summary>
public partial class WeaponHolder
{
    private const float HeavyMinCharge = 0.15f;
    private const float StompDamage = 18f;
    private const float StompReach = 2.1f;
    private const float StompTime = 0.5f;
    private const float StompStamina = 6f;
    /// <summary>The aim must point this far down (sine of the angle) to stomp.</summary>
    private const float StompLookDown = -0.7f;

    public MeleePhase MeleeState { get; private set; } = MeleePhase.Ready;
    /// <summary>0..1 how charged the current heavy swing is.</summary>
    public float MeleeCharge { get; private set; }
    public bool IsMeleeBusy => MeleeState != MeleePhase.Ready;
    public bool IsChargingMelee => MeleeState == MeleePhase.Charging;
    public bool IsMeleeStuck => MeleeState is MeleePhase.Stuck or MeleePhase.Pulling;
    public bool StuckInHead { get; private set; }
    public float PullProgress => MeleeState == MeleePhase.Pulling && _phaseDuration > 0f ? _phaseTime / _phaseDuration : 0f;

    /// <summary>The owner's stamina (swings cost it). Null = no cost (NPCs).</summary>
    public Stamina? MeleeStamina { get; set; }
    public event Action<MeleeHitInfo>? MeleeHit;
    /// <summary>The weapon in hand broke (condition reached 0). The owner removes it.</summary>
    public event Action<MeleeWeapon>? MeleeBroke;

    /// <summary>A press that couldn't start a swing yet (still raising after a sprint) is remembered briefly.</summary>
    private float _attackBuffer;
    private float _phaseTime;
    private float _phaseDuration;
    private bool _meleeTriggerWas;
    private float _swingPrev;
    private bool _swingConnected;
    private bool _stompDone;
    private float _speedMultiplier = 1f;
    private float _damageMultiplier = 1f;
    private float _strikeCharge;
    private Node3D? _stuckTarget;
    private Vector3 _stuckLocal;
    private AudioStreamPlayer3D? _meleeAudio;
    private readonly PhysicsRayQueryParameters3D _meleeRay = new();

    private SkillId MeleeSkill => Melee?.Data.Kind == MeleeKind.Blade ? SkillId.Blade : SkillId.Blunt;

    // ---------- Per frame ----------

    /// <summary>Runs whenever no gun is in hand.</summary>
    private void UpdateMelee(float dt)
    {
        bool held = TriggerHeld;
        bool pressed = _pendingPull || (held && !_meleeTriggerWas);
        _pendingPull = false;
        _meleeTriggerWas = held;
        _attackBuffer = pressed ? 0.35f : _attackBuffer - dt;

        if (IsMeleeStuck && !SenseTargetValid(_stuckTarget))
            FreeStuck(extraWear: 0f);

        switch (MeleeState)
        {
            case MeleePhase.Ready:
                if (_attackBuffer <= 0f || IsSwitching || _lowerBlend > 0.3f || Lowered)
                    break;
                _attackBuffer = 0f;
                if (LookingDown())
                    StartStomp();
                else if (Melee != null)
                    StartWindup();
                break;

            case MeleePhase.Windup:
                _phaseTime += dt;
                if (_phaseTime >= _phaseDuration)
                {
                    if (held)
                    {
                        MeleeState = MeleePhase.Charging;
                        _phaseTime = 0f;
                        MeleeCharge = 0f;
                    }
                    else
                    {
                        BeginStrike(0f);
                    }
                }
                break;

            case MeleePhase.Charging:
                _phaseTime += dt;
                MeleeCharge = Mathf.Clamp(_phaseTime / (Melee!.Data.ChargeTime * _speedMultiplier), 0f, 1f);
                if (!held)
                    BeginStrike(MeleeCharge >= HeavyMinCharge ? MeleeCharge : 0f);
                break;

            case MeleePhase.Strike:
                _phaseTime += dt;
                float s = Mathf.Min(_phaseTime / _phaseDuration, 1f);
                if (!_swingConnected)
                    SweepArc(_swingPrev, s);
                _swingPrev = s;
                if (MeleeState == MeleePhase.Strike && s >= 1f)
                    StartRecovery(_swingConnected ? 0f : Melee!.Data.MissPenalty);
                break;

            case MeleePhase.Recovery:
                _phaseTime += dt;
                if (_phaseTime >= _phaseDuration)
                    MeleeState = MeleePhase.Ready;
                break;

            case MeleePhase.Stuck:
                if (StuckPoint().DistanceTo(AimPivot.Origin) > 2.6f)
                    FreeStuck(extraWear: 2f); // Wrenched out by walking away / the body falling.
                else if (pressed)
                {
                    MeleeState = MeleePhase.Pulling;
                    _phaseTime = 0f;
                    float skill = 1.15f - 0.04f * _skills.GetLevel(SkillId.Blade);
                    _phaseDuration = (StuckInHead ? Melee!.Data.PullFreeHeadTime : Melee!.Data.PullFreeBodyTime) * skill;
                }
                break;

            case MeleePhase.Pulling:
                _phaseTime += dt;
                if (StuckPoint().DistanceTo(AimPivot.Origin) > 2.6f)
                    FreeStuck(extraWear: 2f);
                else if (_phaseTime >= _phaseDuration)
                    FreeStuck(extraWear: 0f);
                break;

            case MeleePhase.Stomp:
                _phaseTime += dt;
                if (!_stompDone && _phaseTime >= StompTime * 0.45f)
                {
                    _stompDone = true;
                    StompRay();
                }
                if (_phaseTime >= StompTime)
                    MeleeState = MeleePhase.Ready;
                break;
        }
    }

    /// <summary>Called when the weapon in hand changes: whatever was going on stops.</summary>
    private void ResetMelee()
    {
        MeleeState = MeleePhase.Ready;
        MeleeCharge = 0f;
        _stuckTarget = null;
        _meleeTriggerWas = TriggerHeld;
    }

    private bool LookingDown() => (-AimPivot.Basis.Z.Normalized()).Y < StompLookDown;

    private static bool SenseTargetValid(Node3D? node) => node != null && GodotObject.IsInstanceValid(node) && node.IsInsideTree();

    // ---------- Swinging ----------

    private void StartWindup()
    {
        MeleeData data = Melee!.Data;
        int skill = _skills.GetLevel(MeleeSkill);
        bool tired = MeleeStamina != null && (MeleeStamina.IsExhausted || MeleeStamina.Fraction < 0.2f);
        // Skill makes every part of the swing quicker; being out of breath makes it slower.
        _speedMultiplier = (1.15f - 0.03f * skill) * (tired ? 1.3f : 1f);
        _damageMultiplier = 0.8f + 0.04f * skill;
        MeleeState = MeleePhase.Windup;
        _phaseTime = 0f;
        _phaseDuration = data.WindupTime * _speedMultiplier;
        MeleeCharge = 0f;
    }

    private void BeginStrike(float charge)
    {
        MeleeData data = Melee!.Data;
        // Stamina: heavier swings and weaker characters pay more; not enough stamina = a weak swing.
        float strengthFactor = 1.15f - 0.05f * _skills.GetLevel(SkillId.Strength);
        float cost = data.StaminaCost * (1f + 0.6f * charge) * strengthFactor;
        float paid = MeleeStamina?.SpendAvailable(cost) ?? cost;
        float staminaFactor = cost > 0f ? Mathf.Lerp(0.55f, 1f, paid / cost) : 1f;
        _damageMultiplier *= staminaFactor;

        _strikeCharge = charge;
        MeleeState = MeleePhase.Strike;
        _phaseTime = 0f;
        _phaseDuration = data.StrikeTime * _speedMultiplier;
        _swingPrev = 0f;
        _swingConnected = false;
        PlayMelee(PlaceholderSounds.MeleeWhoosh, 0.9f + 0.2f * (1f - charge));
    }

    private void StartRecovery(float penalty)
    {
        MeleeState = MeleePhase.Recovery;
        _phaseTime = 0f;
        _phaseDuration = (Melee!.Data.RecoveryTime + penalty) * _speedMultiplier;
    }

    /// <summary>Direction of the blade at arc fraction s: from up-right to down-left across the aim.</summary>
    private Vector3 ArcDirection(float s, float pitchOffsetDegrees)
    {
        float yaw = Mathf.DegToRad(Mathf.Lerp(55f, -55f, s));
        float pitch = Mathf.DegToRad(Mathf.Lerp(12f, -18f, s) + pitchOffsetDegrees);
        return (AimPivot.Basis * (Basis.FromEuler(new Vector3(pitch, yaw, 0f)) * Vector3.Forward)).Normalized();
    }

    private void SweepArc(float from, float to)
    {
        float reach = Melee!.Data.Reach;
        int steps = Mathf.Max(1, Mathf.CeilToInt((to - from) / 0.04f));
        for (int i = 1; i <= steps; i++)
        {
            float s = Mathf.Lerp(from, to, (float)i / steps);
            // Two rays a few degrees apart vertically: a blade, not a laser.
            for (int r = 0; r < 2; r++)
            {
                Vector3 direction = ArcDirection(s, r == 0 ? 3f : -4f);
                if (CastMelee(AimPivot.Origin, direction, reach, stomp: false))
                    return;
            }
        }
    }

    // ---------- Stomping ----------

    private void StartStomp()
    {
        MeleeState = MeleePhase.Stomp;
        _phaseTime = 0f;
        _stompDone = false;
        float cost = StompStamina * (1.15f - 0.05f * _skills.GetLevel(SkillId.Strength));
        float paid = MeleeStamina?.SpendAvailable(cost) ?? cost;
        _damageMultiplier = Mathf.Lerp(0.55f, 1f, cost > 0f ? paid / cost : 1f);
        _strikeCharge = 0f;
    }

    /// <summary>
    /// A stomp is a foot, not a laser: it lands on whatever body part is closest to the spot you're looking
    /// at on the ground, within a step of your feet.
    /// </summary>
    private void StompRay()
    {
        if (GetParent() is not Node3D owner)
            return;
        Vector3 aim = (-AimPivot.Basis.Z).Normalized();
        _meleeRay.From = AimPivot.Origin;
        _meleeRay.To = AimPivot.Origin + aim * StompReach * 1.5f;
        _meleeRay.CollisionMask = 1u;
        _meleeRay.Exclude = _ownerExclude;
        Dictionary ground = GetWorld3D().DirectSpaceState.IntersectRay(_meleeRay);
        Vector3 spot = ground.Count > 0 ? ground["position"].AsVector3() : AimPivot.Origin + aim * StompReach;
        Vector3 feet = owner.GlobalPosition;
        Vector3 flat = new(spot.X - feet.X, 0f, spot.Z - feet.Z);
        if (flat.Length() > 1.2f)
            spot = new Vector3(feet.X, spot.Y, feet.Z) + flat.Normalized() * 1.2f;

        _stompShape ??= new SphereShape3D { Radius = 0.55f };
        _stompQuery.Shape = _stompShape;
        _stompQuery.Transform = new Transform3D(Basis.Identity, spot + Vector3.Up * 0.2f);
        _stompQuery.CollisionMask = HitboxRig.HitboxLayer;
        _stompQuery.Exclude = _ownerExclude;
        Hitbox? best = null;
        float bestDistance = float.MaxValue;
        foreach (Dictionary result in GetWorld3D().DirectSpaceState.IntersectShape(_stompQuery, 16))
        {
            if (result["collider"].AsGodotObject() is not Hitbox box || box.Health is not { IsDead: false })
                continue;
            // Stomping is aimed at heads: a head near the spot wins over an arm beside it.
            float distance = box.GlobalPosition.DistanceTo(spot) - (box.Part == BodyPart.Head ? 0.3f : 0f);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = box;
            }
        }
        if (best != null)
            HitBody(best, best.Health!, best.GlobalPosition, Vector3.Down, stomp: true);
    }

    private SphereShape3D? _stompShape;
    private readonly PhysicsShapeQueryParameters3D _stompQuery = new();

    // ---------- Hits ----------

    /// <summary>One ray of a swing or stomp. True if it hit something (body or wall).</summary>
    private bool CastMelee(Vector3 origin, Vector3 direction, float reach, bool stomp)
    {
        _meleeRay.From = origin;
        _meleeRay.To = origin + direction * reach;
        // World and body hit zones only, never the movement colliders around characters.
        _meleeRay.CollisionMask = 1u | HitboxRig.HitboxLayer;
        _meleeRay.Exclude = _ownerExclude;
        Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_meleeRay);
        if (hit.Count == 0)
            return false;

        Vector3 position = hit["position"].AsVector3();
        if (hit["collider"].AsGodotObject() is Hitbox hitbox)
        {
            if (hitbox.Health is not { IsDead: false } health)
                return false; // A corpse: swing on through.
            // The arc decides WHO gets hit; your aim decides WHERE (aim at the head to hit the head).
            if (!stomp && AimedPart(hitbox, reach) is { } aimed)
            {
                hitbox = aimed.Box;
                position = aimed.Position;
            }
            HitBody(hitbox, health, position, direction, stomp);
            return true;
        }
        if (!stomp)
            HitWall(position, hit["normal"].AsVector3());
        return !stomp;
    }

    /// <summary>The hitbox of the same character that lies straight along the aim, if any.</summary>
    private (Hitbox Box, Vector3 Position)? AimedPart(Hitbox struck, float reach)
    {
        Node3D? target = CharacterOf(struck);
        _meleeRay.From = AimPivot.Origin;
        _meleeRay.To = AimPivot.Origin - AimPivot.Basis.Z.Normalized() * (reach + 0.2f);
        Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_meleeRay);
        if (hit.Count > 0 && hit["collider"].AsGodotObject() is Hitbox aimed && CharacterOf(aimed) == target)
            return (aimed, hit["position"].AsVector3());
        return null;
    }

    private void HitBody(Hitbox hitbox, BodyHealth health, Vector3 position, Vector3 direction, bool stomp)
    {
        _swingConnected = true;
        Node? owner = GetParent();
        Node3D? target = CharacterOf(hitbox);
        BodyPart part = hitbox.Part;
        MeleeData? data = Melee?.Data;
        float charge = _strikeCharge;

        float damage;
        float light = 0f, heavy = 0f, fracture = 0f;
        if (stomp || data == null)
        {
            damage = StompDamage * _damageMultiplier;
            fracture = part is BodyPart.LeftLeg or BodyPart.RightLeg or BodyPart.LeftArm or BodyPart.RightArm ? 0.25f : 0f;
        }
        else
        {
            damage = data.Damage * _damageMultiplier * Mathf.Lerp(1f, data.HeavyMultiplier, charge);
            if (part == BodyPart.Head)
                damage *= data.HeadMultiplier;
            light = data.LightBleedChance;
            heavy = data.HeavyBleedChance * (1f + charge);
            bool limb = part is BodyPart.LeftLeg or BodyPart.RightLeg or BodyPart.LeftArm or BodyPart.RightArm;
            fracture = limb ? Mathf.Min(1f, data.FractureChance * (1f + charge)) : 0f;
        }

        health.NoteAttacker(owner);
        health.ApplyDamage(part, damage, DamageSource.Melee, light, heavy, fracture);
        bool killed = health.IsDead || health.IsDying;

        // Push, stagger, maybe knock down.
        float strength = stomp ? 0.3f : (data?.Knockback ?? 0.5f) * (1f + 0.5f * charge) * _damageMultiplier;
        if (target is Zombie zombie && !zombie.Health.IsDead)
        {
            float knockdownChance = data == null ? 0f : Mathf.Lerp(data.KnockdownChance, data.HeavyKnockdownChance, charge);
            Vector3 push = new Vector3(direction.X, 0f, direction.Z);
            if (push.LengthSquared() < 0.001f)
                push = zombie.GlobalPosition - (GetParent() as Node3D)!.GlobalPosition;
            zombie.ApplyShove(push, strength, !stomp && _rng.Randf() < knockdownChance);
        }

        // Feedback: blood, sound, noise.
        ImpactEffects.Blood(GetTree().CurrentScene, position, -direction + Vector3.Up * 0.3f, killed ? 1.6f : 1f);
        PlayMelee(data?.Kind == MeleeKind.Blade && !stomp ? PlaceholderSounds.MeleeBlade : PlaceholderSounds.MeleeBlunt, 1f - 0.15f * charge);
        GameNoise.Emit(position, GameNoise.MeleeHit, NoiseKind.Melee, owner);

        // Practice and wear.
        if (Melee != null && !stomp)
        {
            _skills.AddXp(MeleeSkill, killed ? 6f : 2f);
            Wear(1f + charge);
        }

        // Blades can bite in and stay there.
        bool stuck = false;
        if (Melee != null && !stomp && data!.Kind == MeleeKind.Blade && target != null)
        {
            float chance = (part == BodyPart.Head ? data.StickChanceHead : data.StickChanceBody)
                * (1.2f - 0.07f * _skills.GetLevel(SkillId.Blade)) * (1f + 0.5f * charge);
            if (_rng.Randf() < chance)
            {
                stuck = true;
                _stuckTarget = target;
                _stuckLocal = target.ToLocal(position);
                StuckInHead = part == BodyPart.Head;
                MeleeState = MeleePhase.Stuck;
            }
        }

        MeleeHit?.Invoke(new MeleeHitInfo(position, direction, target, part, Mathf.Clamp(0.6f + charge, 0.6f, 1.6f), killed, false, stomp, stuck));
        if (!stuck && !stomp && Melee != null)
            StartRecovery(0f);
    }

    private void HitWall(Vector3 position, Vector3 normal)
    {
        _swingConnected = true;
        ImpactEffects.Dust(GetTree().CurrentScene, position, normal);
        PlayMelee(PlaceholderSounds.MeleeWall, 1f);
        GameNoise.Emit(position, GameNoise.MeleeWallHit, NoiseKind.Melee, GetParent());
        Wear(1.5f);
        MeleeHit?.Invoke(new MeleeHitInfo(position, normal, null, BodyPart.Chest, 0.5f, false, true, false, false));
        if (Melee != null)
            StartRecovery(Melee.Data.MissPenalty); // Bounced off: jarring.
    }

    private void Wear(float amount)
    {
        if (Melee is not { } weapon)
            return;
        float skill = 1.2f - 0.06f * _skills.GetLevel(MeleeSkill);
        weapon.Condition = Mathf.Max(0f, weapon.Condition - weapon.Data.WearPerHit * amount * skill);
        if (weapon.Condition <= 0f)
        {
            ResetMelee();
            MeleeBroke?.Invoke(weapon);
        }
    }

    private static Node3D? CharacterOf(Node node)
    {
        for (Node? n = node; n != null; n = n.GetParent())
        {
            if (n is CharacterBody3D body)
                return body;
        }
        return null;
    }

    // ---------- Stuck ----------

    private Vector3 StuckPoint() => SenseTargetValid(_stuckTarget) ? _stuckTarget!.ToGlobal(_stuckLocal) : AimPivot.Origin;

    private void FreeStuck(float extraWear)
    {
        _stuckTarget = null;
        StuckInHead = false;
        MeleeState = MeleePhase.Recovery;
        _phaseTime = 0f;
        _phaseDuration = 0.25f;
        if (extraWear > 0f)
            Wear(extraWear);
    }

    // ---------- Pose ----------

    /// <summary>Melee weapon pose for the current phase (lowered while sprinting or switching).</summary>
    private void PoseMelee(MeleeData data)
    {
        Transform3D ready = WeaponData.Pose(data.HoldPosition, data.HoldRotationDegrees);
        Transform3D windup = PointedPose(data.WindupPosition, data.WindupDirection);
        Transform3D middle = PointedPose(data.StrikePosition, data.StrikeDirection);
        Transform3D through = PointedPose(data.FollowThroughPosition, data.FollowThroughDirection);
        Transform3D low = WeaponData.Pose(data.LoweredPosition, data.LoweredRotationDegrees);
        float t = _phaseDuration > 0f ? Mathf.Clamp(_phaseTime / _phaseDuration, 0f, 1f) : 1f;

        if (IsMeleeStuck)
        {
            // The grip stays in the hands, the blade points at where it's buried.
            Vector3 grip = AimPivot * new Vector3(0.15f, -0.3f, -0.35f);
            Vector3 toPoint = StuckPoint() - grip;
            if (toPoint.LengthSquared() < 0.0001f)
                toPoint = -AimPivot.Basis.Z;
            var basis = new Basis(new Quaternion(Vector3.Up, toPoint.Normalized()));
            if (MeleeState == MeleePhase.Pulling)
                grip += AimPivot.Basis * new Vector3(Mathf.Sin(_phaseTime * 45f) * 0.015f, 0f, 0.04f * t);
            GlobalTransform = new Transform3D(basis, grip);
            return;
        }

        Transform3D local = MeleeState switch
        {
            MeleePhase.Windup => ready.InterpolateWith(windup, Mathf.SmoothStep(0f, 1f, t)),
            MeleePhase.Charging => windup * new Transform3D(Basis.Identity, new Vector3(0f, -0.04f * MeleeCharge, 0.06f * MeleeCharge)),
            // Through the middle (where the aim is) on the way from the shoulder to low-left.
            MeleePhase.Strike => t < 0.5f ? windup.InterpolateWith(middle, t * 2f) : middle.InterpolateWith(through, t * 2f - 1f),
            MeleePhase.Recovery => through.InterpolateWith(ready, Mathf.SmoothStep(0f, 1f, t)),
            MeleePhase.Stomp => low,
            _ => ready.InterpolateWith(low, Mathf.SmoothStep(0f, 1f, _lowerBlend)),
        };
        GlobalTransform = AimPivot * local;
    }

    /// <summary>A pose given by the grip position and the direction the weapon points.</summary>
    private static Transform3D PointedPose(Vector3 position, Vector3 direction) =>
        new(new Basis(new Quaternion(Vector3.Up, direction.Normalized())), position);

    private void PlayMelee(AudioStream sound, float pitch)
    {
        if (_meleeAudio == null)
        {
            _meleeAudio = new AudioStreamPlayer3D { MaxPolyphony = 4, UnitSize = 4f, MaxDistance = 50f };
            AddChild(_meleeAudio);
        }
        _meleeAudio.Stream = sound;
        _meleeAudio.PitchScale = Mathf.Clamp(pitch * _rng.RandfRange(0.92f, 1.08f), 0.5f, 2f);
        _meleeAudio.GlobalPosition = AimPivot.Origin;
        _meleeAudio.Play();
    }
}
