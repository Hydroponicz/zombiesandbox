using Godot;
using Godot.Collections;
using ZombieSandbox.AI;
using ZombieSandbox.Character;
using ZombieSandbox.Core.Audio;

namespace ZombieSandbox.Combat;

public enum FireMode
{
    Semi,
    Auto,
}

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
    /// <summary>Stance recoil multiplier (crouch/prone brace the weapon).</summary>
    public float RecoilStanceMultiplier { get; set; } = 1f;
    /// <summary>The trigger is being pulled.</summary>
    public bool TriggerHeld { get; set; }

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
    public FireMode FireMode { get; private set; } = FireMode.Auto;
    public int ShotsFired { get; private set; }
    /// <summary>A rifle-butt shove is playing (can't fire).</summary>
    public bool IsShoving => _shoveTime >= 0f;
    private float _shoveTime = -1f;
    private const float ShoveDuration = 0.35f;
    /// <summary>Total recoil offset of the point of aim in degrees (X = up, Y = sideways): per-shot kick plus full-auto climb. Returns to zero.</summary>
    public Vector2 RecoilDegrees => _recoil + new Vector2(_climb, 0f);
    /// <summary>The part of the recoil the camera shows (the owner's camera applies it).</summary>
    public Vector2 CameraRecoilDegrees => Weapon == null ? Vector2.Zero
        : _recoil * Weapon.Data.CameraRecoilFraction + new Vector2(_climb * Weapon.Data.CameraClimbFraction, 0f);
    /// <summary>Shots in the current burst.</summary>
    public int BurstShots { get; private set; }

    private CharacterStats _stats = null!;
    private CharacterSkills _skills = null!;
    private Array<Rid> _ownerExclude = new();
    private uint _bulletMask;
    private float _zeroAngle;
    private float _fireCooldown;
    private bool _triggerWasHeld;
    private bool _pendingPull;
    private AudioStreamPlayer3D _shotAudio = null!;
    private Vector2 _recoil;
    private float _climbTarget;
    private float _climb;
    private Vector2 _recoilVelocity;
    private float _kickback;
    private float _kickbackVelocity;
    private float _sinceLastShot = 10f;
    private readonly RandomNumberGenerator _rng = new();
    private float _lowerBlend;
    private float _readyTime;
    private float _breathLeft;
    private float _gaspLeft;
    private float _swayTime;
    private float _swayAmplitude;
    private readonly PhysicsRayQueryParameters3D _wallRay = new();

    /// <param name="ownerExclude">The owner's body and hitboxes (bullets and wall checks ignore them).</param>
    /// <param name="worldMask">What the weapon bumps into (walls).</param>
    /// <param name="bulletMask">What bullets can hit (world + hitboxes).</param>
    public void Initialize(CharacterStats stats, CharacterSkills skills, IMagazineStorage? pouch, Array<Rid> ownerExclude, uint worldMask, uint bulletMask)
    {
        _stats = stats;
        _skills = skills;
        Pouch = pouch;
        _ownerExclude = ownerExclude;
        _bulletMask = bulletMask;
        _breathLeft = stats.HoldBreathSeconds;
        _wallRay.Exclude = ownerExclude;
        _wallRay.CollisionMask = worldMask;
    }

    /// <summary>Extra sway from injuries (fractured arms, pain). Set by the owner.</summary>
    public float InjurySwayMultiplier { get; set; } = 1f;

    public override void _Ready()
    {
        TopLevel = true;
        ProcessPriority = 10; // After the owner's camera has set the aim pivot this frame.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _shotAudio = new AudioStreamPlayer3D { Stream = PlaceholderSounds.Gunshot, MaxPolyphony = 8, UnitSize = 12f, MaxDistance = 400f };
        AddChild(_shotAudio);
        CreateHandlingAudio();
        if (StartingWeapon != null)
            Equip(StartingWeapon.Instantiate<Weapon>());
    }

    public void Equip(Weapon weapon)
    {
        Weapon?.QueueFree();
        Weapon = weapon;
        AddChild(weapon);

        // Zero the sights: tilt the bore up just enough that the bullet crosses the sight line at the zero distance.
        WeaponData data = weapon.Data;
        if (data.Ammo != null)
        {
            float sightHeight = weapon.SightEye.Position.Y - weapon.Muzzle.Position.Y;
            float gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
            _zeroAngle = Ballistics.SolveZeroAngle(data.Ammo, sightHeight, data.ZeroDistance, gravity);
        }
        FireMode = data.SupportsAuto ? FireMode.Auto : FireMode.Semi;
        LoadStartingMagazine(data);
    }

    /// <summary>
    /// Register a trigger press. Latched until the next frame, so a click shorter than one
    /// frame still fires (TriggerHeld alone could miss it).
    /// </summary>
    public void PullTrigger() => _pendingPull = true;

    /// <summary>Play the shove animation. Interrupts reloading / checking (hands are needed).</summary>
    public void PlayShove()
    {
        CancelReload();
        CancelMagazineCheck();
        _shoveTime = 0f;
    }

    public void CycleFireMode()
    {
        if (Weapon == null || !(Weapon.Data.SupportsSemi && Weapon.Data.SupportsAuto))
            return;
        FireMode = FireMode == FireMode.Auto ? FireMode.Semi : FireMode.Auto;
    }

    /// <summary>Called by <see cref="Ballistics"/> when one of this holder's bullets lands.</summary>
    /// <summary>One of this holder's bullets landed (true = it hit something that takes damage).</summary>
    public event System.Action<bool>? ShotLanded;

    public void OnShotLanded(float distance, bool hitTarget)
    {
        ShotLanded?.Invoke(hitTarget);
        if (hitTarget && _skills != null)
            _skills.AddXp(SkillId.Aiming, Config.AimingXpPerHit * (1f + Config.AimingXpRangeBonusPer100m * distance / 100f));
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

        UpdateReload(dt);
        UpdateMagazineCheck(dt);
        UpdateMalfunctionHandling(dt);
        _busyBlend = Mathf.MoveToward(_busyBlend, IsBusy ? 1f : 0f, dt / Config.ReloadPoseBlendTime);

        IsAiming = WantsAim && raised && wallBlock < 0.5f && !IsBusy;
        float adsSpeed = 1f / data.AdsTime * (MoveFraction > 0.75f ? 0.6f : 1f);
        AdsBlend = Mathf.MoveToward(AdsBlend, IsAiming ? 1f : 0f, adsSpeed * dt);

        UpdateBreath(dt);
        UpdateSway(dt, data);
        UpdateRecoil(dt, data);

        // Pose: hip ↔ aimed, then toward lowered when sprinting or jammed against a wall.
        Transform3D hip = WeaponData.Pose(data.HipPosition, data.HipRotationDegrees);
        Transform3D aimed = new(Basis.Identity, -Weapon.SightEye.Position);
        Transform3D ready = hip.InterpolateWith(aimed, Mathf.SmoothStep(0f, 1f, AdsBlend));
        Transform3D lowered = WeaponData.Pose(data.LoweredPosition, data.LoweredRotationDegrees);
        float lowerAmount = Mathf.Max(Mathf.SmoothStep(0f, 1f, _lowerBlend), wallBlock);
        Transform3D local = ready.InterpolateWith(lowered, lowerAmount);
        if (_busyBlend > 0f)
            local = local.InterpolateWith(WeaponData.Pose(data.ReloadPosition, data.ReloadRotationDegrees), Mathf.SmoothStep(0f, 1f, _busyBlend));

        // Sway plus the part of the recoil the camera doesn't already show.
        Vector2 gunRecoil = RecoilDegrees - CameraRecoilDegrees;
        Transform3D sway = new(Basis.FromEuler(new Vector3(
            SwayPitch() + Mathf.DegToRad(gunRecoil.X),
            SwayYaw() + Mathf.DegToRad(gunRecoil.Y),
            0f)), Vector3.Zero);
        Transform3D aim = AimPivot * sway;

        UpdateWallPullBack(dt, aim, ready);
        local.Origin += Vector3.Back * (WallPullBack + _kickback);

        // Rifle-butt shove: thrust the weapon forward and down, then back.
        if (_shoveTime >= 0f)
        {
            _shoveTime += dt;
            float thrust = Mathf.Sin(Mathf.Pi * Mathf.Clamp(_shoveTime / ShoveDuration, 0f, 1f));
            local = new Transform3D(Basis.FromEuler(new Vector3(Mathf.DegToRad(-25f) * thrust, Mathf.DegToRad(15f) * thrust, Mathf.DegToRad(30f) * thrust)), Vector3.Zero)
                * local;
            local.Origin += new Vector3(-0.05f, -0.05f, -0.28f) * thrust;
            if (_shoveTime >= ShoveDuration)
                _shoveTime = -1f;
        }

        GlobalTransform = aim * local;
        CanFire = raised && _lowerBlend <= 0f && wallBlock <= 0f && !IsBusy && _busyBlend <= 0f && _shoveTime < 0f;

        UpdateTrigger(dt, data);
    }

    private void UpdateTrigger(float dt, WeaponData data)
    {
        bool pulled = (TriggerHeld && !_triggerWasHeld) || _pendingPull;
        _pendingPull = false;
        _triggerWasHeld = TriggerHeld;
        float interval = 60f / data.FireRateRpm;

        // Holding full-auto may carry a little negative cooldown so the rate stays exact at low frame rates.
        bool firingAuto = TriggerHeld && FireMode == FireMode.Auto && CanFire;
        _fireCooldown = Mathf.Max(_fireCooldown - dt, firingAuto ? -interval : 0f);

        if (!CanFire || data.Ammo == null)
            return;

        // Jammed or empty chamber: the trigger just clicks (once per pull, not continuously in full auto).
        if (IsJammed || !Chambered)
        {
            if (pulled)
                PlayHandling(PlaceholderSounds.DryClick);
            return;
        }

        if (FireMode == FireMode.Semi)
        {
            if (pulled && _fireCooldown <= 0f)
            {
                Shoot(data);
                _fireCooldown = interval;
            }
        }
        else
        {
            if (pulled && _fireCooldown <= 0f && !TriggerHeld)
            {
                Shoot(data); // A click shorter than a frame still fires one round.
                _fireCooldown = interval;
            }
            while (TriggerHeld && _fireCooldown <= 0f && Chambered && !IsJammed)
            {
                Shoot(data);
                _fireCooldown += interval;
            }
        }
    }

    private void Shoot(WeaponData data)
    {
        Malfunction malfunction = RollMalfunction(data);
        if (malfunction == Malfunction.Misfire)
        {
            // A dud: the hammer falls, nothing happens. The bad round stays in the chamber.
            Malfunction = malfunction;
            MalfunctionKnown = false;
            PlayHandling(PlaceholderSounds.DryClick);
            return;
        }

        Transform3D muzzle = Weapon!.Muzzle.GlobalTransform;
        Vector3 direction = (muzzle.Basis * new Vector3(0f, Mathf.Sin(_zeroAngle), -Mathf.Cos(_zeroAngle))).Normalized();
        Transform3D sightEye = Weapon.SightEye.GlobalTransform;

        Ballistics.Instance.Fire(new Shot(
            Origin: muzzle.Origin,
            Velocity: direction * data.Ammo.MuzzleVelocity,
            Ammo: data.Ammo,
            SafeOrigin: AimPivot.Origin,
            SightOrigin: sightEye.Origin,
            SightDirection: -sightEye.Basis.Z,
            Shooter: this,
            Exclude: _ownerExclude,
            CollisionMask: _bulletMask));

        ShotsFired++;
        GameNoise.Emit(muzzle.Origin, GameNoise.RifleShot, NoiseKind.Gunshot, GetParent());
        ApplyWear(data);
        if (malfunction == Malfunction.None)
        {
            CycleAfterShot();
        }
        else
        {
            // The shot went off but the action didn't cycle properly: nothing new is chambered.
            Malfunction = malfunction;
            MalfunctionKnown = false;
            Chambered = false;
            if (malfunction == Malfunction.DoubleFeed && Magazine is { IsEmpty: false })
                Magazine.Rounds--; // The mangled round is lost.
        }
        _shotAudio.GlobalPosition = muzzle.Origin;
        _shotAudio.Play();
        ApplyRecoil(data);
    }

    /// <summary>
    /// Kick the gun: an impulse into a critically damped spring, so it snaps up and settles all
    /// the way back to the point of aim. In full auto the impulses overlap, so the gun climbs and
    /// then bounces around a raised point; vertical kick also eases after the first few shots.
    /// </summary>
    private void ApplyRecoil(WeaponData data)
    {
        BurstShots = _sinceLastShot > Config.BurstResetTime ? 1 : BurstShots + 1;
        _sinceLastShot = 0f;

        float easing = Mathf.Clamp((BurstShots - Config.BurstClimbShots) / 3f, 0f, 1f);
        float climb = Mathf.Lerp(1f, Config.SustainedFireVerticalMultiplier, easing);
        float widen = 1f + 0.3f * Mathf.Min(BurstShots / 5f, 1f);
        float multiplier = Mathf.Lerp(Config.HipRecoilMultiplier, 1f, AdsBlend) * RecoilStanceMultiplier * _stats.RecoilMultiplier;

        var kick = new Vector2(
            data.VerticalRecoilDegrees * climb * _rng.RandfRange(0.85f, 1.15f),
            data.HorizontalRecoilDegrees * widen * _rng.RandfRange(-1f, 1f)) * multiplier;

        // For a critically damped spring, an impulse of kick·ω·e peaks at exactly `kick`.
        // Full auto walks the point of aim up a little more each shot until it levels off.
        if (BurstShots > 1)
            _climbTarget = Mathf.Min(_climbTarget + data.ClimbPerShotDegrees * multiplier, data.MaxClimbDegrees * multiplier);

        float omega = Mathf.Sqrt(data.RecoilRecoveryStiffness);
        _recoilVelocity += kick * omega * Mathf.E;
        _kickbackVelocity += data.KickbackMeters * multiplier * KickbackOmega * Mathf.E;
    }

    private const float KickbackOmega = 25f;

    private void UpdateRecoil(float dt, WeaponData data)
    {
        _sinceLastShot += dt;

        // The climb eases toward its target while firing and settles all the way back once you stop.
        float interval = 60f / data.FireRateRpm;
        if (_sinceLastShot > interval * 1.5f)
            _climbTarget = 0f;
        float rate = _climbTarget > _climb ? 12f : 1f / data.ClimbRecoveryTime;
        _climb = Mathf.Lerp(_climb, _climbTarget, 1f - Mathf.Exp(-rate * dt));

        float k = data.RecoilRecoveryStiffness;
        float c = 2f * Mathf.Sqrt(k);
        const float kk = KickbackOmega * KickbackOmega;
        const float ck = 2f * KickbackOmega;

        // Small fixed steps keep the springs stable at any frame rate.
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 240f)));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            _recoilVelocity += (-k * _recoil - c * _recoilVelocity) * h;
            _recoil += _recoilVelocity * h;
            _kickbackVelocity += (-kk * _kickback - ck * _kickbackVelocity) * h;
            _kickback += _kickbackVelocity * h;
        }
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
            * data.SwayMultiplier
            * InjurySwayMultiplier;
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
