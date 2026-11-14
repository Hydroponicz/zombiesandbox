using Godot;
using Godot.Collections;
using ZombieSandbox.Character;

namespace ZombieSandbox.Combat;

/// <summary>
/// Holds and points a weapon for any character (player or NPC). The owner feeds it intent
/// (aim, hold breath, lowered) and an aim pivot every frame; the holder works out ADS blending,
/// sway, breathing and wall collision, and places the weapon.
///
/// The holder is top-level and positioned in world space every rendered frame, after the
/// owner's camera/aim has been updated (see ProcessPriority).
/// </summary>
public partial class WeaponHolder : Node3D
{
    [Export] public WeaponHandlingConfig Config { get; set; } = null!;
    [Export] public PackedScene? StartingWeapon { get; set; }

    // ---------- Intent, set by the owner ----------

    /// <summary>The owner wants to aim down sights.</summary>
    public bool WantsAim { get; set; }
    public bool WantsHoldBreath { get; set; }
    /// <summary>Carry the weapon lowered (sprinting).</summary>
    public bool Lowered { get; set; }
    /// <summary>0 = still, 1 = running speed or faster.</summary>
    public float MoveFraction { get; set; }
    /// <summary>Stance sway multiplier (crouch/prone steady the weapon).</summary>
    public float StanceSwayMultiplier { get; set; } = 1f;
    /// <summary>0 = rested, 1 = completely out of breath.</summary>
    public float Breathlessness { get; set; }
    /// <summary>World transform of the eye: origin at the eye, -Z where the owner aims.</summary>
    public Transform3D AimPivot { get; set; } = Transform3D.Identity;

    // ---------- State ----------

    public Weapon? Weapon { get; private set; }
    /// <summary>True when the owner wants to aim and nothing prevents it (sprinting, wall, raising).</summary>
    public bool IsAiming { get; private set; }
    /// <summary>0 = hip, 1 = fully aimed.</summary>
    public float AdsBlend { get; private set; }
    public bool CanFire { get; private set; }
    public bool IsHoldingBreath { get; private set; }
    public bool IsGasping { get; private set; }
    /// <summary>Breath left as a fraction of the maximum hold time.</summary>
    public float BreathFraction => _stats.HoldBreathSeconds > 0f ? _breathLeft / _stats.HoldBreathSeconds : 0f;
    /// <summary>How far a wall is pushing the weapon back toward the body (m).</summary>
    public float WallPullBack { get; private set; }
    /// <summary>Current sway amplitude in degrees (for debugging/tuning).</summary>
    public float SwayDegrees => _swayAmplitude;

    private CharacterStats _stats = null!;
    private float _lowerBlend;
    private float _readyTime;
    private float _breathLeft;
    private float _gaspLeft;
    private float _swayTime;
    private float _swayAmplitude;
    private readonly PhysicsRayQueryParameters3D _wallRay = new();

    public void Initialize(CharacterStats stats, Rid owner, uint collisionMask)
    {
        _stats = stats;
        _breathLeft = stats.HoldBreathSeconds;
        _wallRay.Exclude = new Array<Rid> { owner };
        _wallRay.CollisionMask = collisionMask;
    }

    public override void _Ready()
    {
        TopLevel = true;
        ProcessPriority = 10; // After the owner's camera has set the aim pivot this frame.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        if (StartingWeapon != null)
            Equip(StartingWeapon.Instantiate<Weapon>());
    }

    public void Equip(Weapon weapon)
    {
        Weapon?.QueueFree();
        Weapon = weapon;
        AddChild(weapon);
    }

    public override void _Process(double delta)
    {
        if (Weapon == null || _stats == null)
            return;

        float dt = (float)delta;
        WeaponData data = Weapon.Data;

        // Lowering for sprint, then a short raise before the weapon is ready again.
        _lowerBlend = Mathf.MoveToward(_lowerBlend, Lowered ? 1f : 0f, dt / Config.LowerTime);
        _readyTime = Lowered ? 0f : _readyTime + dt;
        bool raised = _readyTime >= data.RaiseTime;

        float wallBlock = Mathf.SmoothStep(Config.WallPullBackBlocksAt, Config.MaxWallPullBack, WallPullBack);

        IsAiming = WantsAim && raised && wallBlock < 0.5f;
        float adsSpeed = 1f / data.AdsTime * (MoveFraction > 0.75f ? 0.6f : 1f);
        AdsBlend = Mathf.MoveToward(AdsBlend, IsAiming ? 1f : 0f, adsSpeed * dt);

        UpdateBreath(dt);
        UpdateSway(dt, data);

        // Pose: hip ↔ aimed, then toward lowered when sprinting or jammed against a wall.
        Transform3D hip = WeaponData.Pose(data.HipPosition, data.HipRotationDegrees);
        Transform3D aimed = new(Basis.Identity, -Weapon.SightEye.Position);
        Transform3D ready = hip.InterpolateWith(aimed, Mathf.SmoothStep(0f, 1f, AdsBlend));
        Transform3D lowered = WeaponData.Pose(data.LoweredPosition, data.LoweredRotationDegrees);
        float lowerAmount = Mathf.Max(Mathf.SmoothStep(0f, 1f, _lowerBlend), wallBlock);
        Transform3D local = ready.InterpolateWith(lowered, lowerAmount);

        Transform3D sway = new(Basis.FromEuler(new Vector3(SwayPitch(), SwayYaw(), 0f)), Vector3.Zero);
        Transform3D aim = AimPivot * sway;

        UpdateWallPullBack(dt, aim, ready);
        local.Origin += Vector3.Back * WallPullBack;

        GlobalTransform = aim * local;
        CanFire = raised && _lowerBlend <= 0f && wallBlock <= 0f;
    }

    private void UpdateBreath(float dt)
    {
        float max = _stats.HoldBreathSeconds;

        if (IsGasping)
        {
            _gaspLeft -= dt;
            if (_gaspLeft <= 0f)
                IsGasping = false;
        }

        // Once you let go (or gasp), you need some breath back before holding it again.
        bool canStart = IsHoldingBreath || _breathLeft >= max * Config.BreathNeededAfterGasp;
        IsHoldingBreath = WantsHoldBreath && AdsBlend > 0.5f && !IsGasping && _breathLeft > 0f && canStart;

        if (IsHoldingBreath)
        {
            _breathLeft -= dt;
            if (_breathLeft <= 0f)
            {
                _breathLeft = 0f;
                IsHoldingBreath = false;
                IsGasping = true;
                _gaspLeft = Config.GaspDuration;
            }
        }
        else if (!IsGasping)
        {
            _breathLeft = Mathf.Min(_breathLeft + max / Config.BreathRefillSeconds * dt, max);
        }
    }

    private void UpdateSway(float dt, WeaponData data)
    {
        float amplitude = Config.AimedSwayDegrees
            * Mathf.Lerp(Config.HipSwayMultiplier, 1f, AdsBlend)
            * StanceSwayMultiplier
            * (1f + Config.MovingSwayGain * MoveFraction)
            * (1f + Config.BreathlessSwayGain * Breathlessness * Breathlessness)
            * _stats.AimSwayMultiplier
            * data.SwayMultiplier;
        if (IsHoldingBreath)
            amplitude *= Config.HoldBreathSwayMultiplier;
        else if (IsGasping)
            amplitude *= Config.GaspSwayMultiplier;

        // Ease amplitude changes so sway never pops.
        _swayAmplitude = Mathf.Lerp(_swayAmplitude, amplitude, 1f - Mathf.Exp(-5f * dt));
        float speed = (1f + Config.BreathlessSwaySpeedGain * Breathlessness) * (IsHoldingBreath ? 0.4f : 1f);
        _swayTime += dt * speed;
    }

    // Two sine waves per axis at unrelated frequencies: a slow, breathing-like drift that never repeats exactly.
    private float SwayYaw() => Mathf.DegToRad(_swayAmplitude) * (0.6f * Mathf.Sin(_swayTime * 0.9f) + 0.4f * Mathf.Sin(_swayTime * 2.3f + 1.3f));

    private float SwayPitch() => Mathf.DegToRad(_swayAmplitude) * (0.6f * Mathf.Sin(_swayTime * 1.4f + 0.5f) + 0.4f * Mathf.Sin(_swayTime * 0.6f + 2f));

    /// <summary>Pull the weapon back toward the body if the muzzle would go into a wall.</summary>
    private void UpdateWallPullBack(float dt, Transform3D aim, Transform3D readyLocal)
    {
        float muzzleReach = -(readyLocal * Weapon!.Muzzle.Position).Z + 0.03f;
        Vector3 forward = -aim.Basis.Z.Normalized();
        _wallRay.From = aim.Origin;
        _wallRay.To = aim.Origin + forward * muzzleReach;
        Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_wallRay);

        float target = 0f;
        if (hit.Count > 0)
        {
            float distance = aim.Origin.DistanceTo(hit["position"].AsVector3());
            target = Mathf.Clamp(muzzleReach - distance, 0f, Config.MaxWallPullBack);
        }
        WallPullBack = Mathf.MoveToward(WallPullBack, target, 2.5f * dt);
    }
}
