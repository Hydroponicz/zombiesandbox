using System;
using Godot;
using Godot.Collections;
using ZombieSandbox.Character;
using ZombieSandbox.Core;

namespace ZombieSandbox.Health;

/// <summary>
/// Body-part health for any character (player, zombie, NPC, training dummy).
/// Damage arrives per body part; destroyed parts pass damage on to the rest of the body;
/// bleeding drains over time; fractures and pain change how the body works.
/// </summary>
public partial class BodyHealth : Node, ISaveState
{
    [Export] public HealthConfig Config { get; set; } = null!;

    private readonly float[] _hp = new float[BodyParts.Count];
    private readonly BleedLevel[] _bleed = new BleedLevel[BodyParts.Count];
    private readonly bool[] _fractured = new bool[BodyParts.Count];
    private readonly RandomNumberGenerator _rng = new();
    private CharacterSkills? _skills;

    public bool IsDead { get; private set; }
    /// <summary>0..1. Builds with damage, fractures and destroyed parts; fades slowly.</summary>
    public float Pain { get; private set; }

    /// <summary>A hit landed (not bleeding ticks): part, damage.</summary>
    public event Action<BodyPart, float>? Damaged;
    public event Action<BodyPart>? Fractured;
    public event Action? Died;

    public override void _Ready() => HealAll();

    /// <summary>Optional: skills to train Medical from taking damage.</summary>
    public void Initialize(CharacterSkills? skills) => _skills = skills;

    public float Hp(BodyPart part) => _hp[(int)part];
    public float MaxHp(BodyPart part) => Config.MaxFor(part);
    public float Fraction(BodyPart part) => _hp[(int)part] / Config.MaxFor(part);
    public bool IsDestroyed(BodyPart part) => _hp[(int)part] <= 0f;
    public BleedLevel Bleeding(BodyPart part) => _bleed[(int)part];
    public bool IsFractured(BodyPart part) => _fractured[(int)part];

    public bool IsBleeding => System.Array.Exists(_bleed, b => b != BleedLevel.None);
    public bool IsBleedingHeavily => System.Array.Exists(_bleed, b => b == BleedLevel.Heavy);

    /// <summary>0..1 total health across the body.</summary>
    public float OverallFraction
    {
        get
        {
            float hp = 0f, max = 0f;
            foreach (BodyPart part in BodyParts.All)
            {
                hp += _hp[(int)part];
                max += Config.MaxFor(part);
            }
            return hp / max;
        }
    }

    /// <summary>0 = fine, 1 = about to die (lowest of overall, head and chest).</summary>
    public float Danger => 1f - Mathf.Min(OverallFraction, Mathf.Min(Fraction(BodyPart.Head), Fraction(BodyPart.Chest)));

    // ---------- Effects on the body ----------

    public float SpeedMultiplier
    {
        get
        {
            float speed = 1f;
            foreach (BodyPart leg in new[] { BodyPart.LeftLeg, BodyPart.RightLeg })
            {
                if (IsDestroyed(leg))
                    speed *= Config.DestroyedLegSpeed;
                else if (IsFractured(leg))
                    speed *= Config.FracturedLegSpeed;
            }
            return speed;
        }
    }

    public bool CanSprint => !IsDestroyed(BodyPart.LeftLeg) && !IsDestroyed(BodyPart.RightLeg)
        && !IsFractured(BodyPart.LeftLeg) && !IsFractured(BodyPart.RightLeg);

    public float SwayMultiplier
    {
        get
        {
            float sway = 1f;
            foreach (BodyPart arm in new[] { BodyPart.LeftArm, BodyPart.RightArm })
            {
                if (IsDestroyed(arm))
                    sway *= Config.DestroyedArmSway;
                else if (IsFractured(arm))
                    sway *= Config.FracturedArmSway;
            }
            return sway * (1f + Config.PainSway * Pain);
        }
    }

    // ---------- Damage ----------

    /// <summary>
    /// Damage a body part. Chances are rolled for bleeding and (limbs only) a fracture.
    /// Damage to a destroyed part, or beyond what a part has left, spreads to the rest of the body.
    /// </summary>
    public void ApplyDamage(BodyPart part, float amount, DamageSource source,
        float lightBleedChance = 0f, float heavyBleedChance = 0f, float fractureChance = 0f)
    {
        if (IsDead || amount <= 0f)
            return;

        DamagePart(part, amount);

        if (source != DamageSource.Bleeding)
        {
            float roll = _rng.Randf();
            if (roll < heavyBleedChance)
                SetBleed(part, BleedLevel.Heavy);
            else if (roll < heavyBleedChance + lightBleedChance)
                SetBleed(part, BleedLevel.Light);

            if (BodyParts.IsLimb(part) && !_fractured[(int)part] && _rng.Randf() < fractureChance)
                Fracture(part);

            Pain = Mathf.Min(1f, Pain + amount * Config.PainPerDamage);
            _skills?.AddXp(SkillId.Medical, amount * Config.MedicalXpPerDamage);
            Damaged?.Invoke(part, amount);
        }

        CheckDeath();
    }

    /// <summary>A bullet hit on a body part.</summary>
    public void ApplyBulletHit(BodyPart part, float damage) =>
        ApplyDamage(part, damage, DamageSource.Bullet,
            Config.BulletLightBleedChance, Config.BulletHeavyBleedChance, Config.BulletLimbFractureChance);

    /// <summary>Landing after a drop of <paramref name="height"/> meters.</summary>
    public void ApplyFall(float height)
    {
        float over = height - Config.SafeFallHeight;
        if (over <= 0f)
            return;
        float fracture = over * Config.FallFractureChancePerMeter;
        ApplyDamage(BodyPart.LeftLeg, over * Config.FallLegDamagePerMeter, DamageSource.Fall, 0f, 0f, fracture);
        ApplyDamage(BodyPart.RightLeg, over * Config.FallLegDamagePerMeter, DamageSource.Fall, 0f, 0f, fracture);
        float torso = height - Config.FallTorsoHeight;
        if (torso > 0f)
        {
            ApplyDamage(BodyPart.Stomach, torso * Config.FallTorsoDamagePerMeter, DamageSource.Fall);
            ApplyDamage(BodyPart.Chest, torso * Config.FallTorsoDamagePerMeter, DamageSource.Fall);
        }
    }

    private void DamagePart(BodyPart part, float amount)
    {
        int i = (int)part;
        float spread;
        if (_hp[i] <= 0f)
        {
            spread = amount;
        }
        else
        {
            _hp[i] -= amount;
            spread = _hp[i] < 0f && !BodyParts.IsVital(part) ? -_hp[i] : 0f;
            if (_hp[i] < 0f)
                _hp[i] = 0f;
        }

        if (spread <= 0f)
            return;

        // Destroyed (or overkilled) part: the rest of the body takes it, spread evenly.
        int alive = 0;
        foreach (BodyPart other in BodyParts.All)
        {
            if (other != part && _hp[(int)other] > 0f)
                alive++;
        }
        if (alive == 0)
            return;
        float share = spread * Config.DestroyedPartSpreadMultiplier / alive;
        foreach (BodyPart other in BodyParts.All)
        {
            int o = (int)other;
            if (other != part && _hp[o] > 0f)
                _hp[o] = Mathf.Max(0f, _hp[o] - share);
        }
    }

    private void SetBleed(BodyPart part, BleedLevel level)
    {
        if (level > _bleed[(int)part])
            _bleed[(int)part] = level;
    }

    public void Fracture(BodyPart part)
    {
        if (!BodyParts.IsLimb(part) || _fractured[(int)part])
            return;
        _fractured[(int)part] = true;
        Fractured?.Invoke(part);
    }

    private void CheckDeath()
    {
        if (IsDead || (!IsDestroyed(BodyPart.Head) && !IsDestroyed(BodyPart.Chest)))
            return;
        IsDead = true;
        Died?.Invoke();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsDead)
            return;
        float dt = (float)delta;

        foreach (BodyPart part in BodyParts.All)
        {
            BleedLevel bleed = _bleed[(int)part];
            if (bleed != BleedLevel.None)
                ApplyDamage(part, (bleed == BleedLevel.Heavy ? Config.HeavyBleedRate : Config.LightBleedRate) * dt, DamageSource.Bleeding);
        }

        // Pain fades toward a floor set by lasting injuries.
        float floor = 0f;
        foreach (BodyPart part in BodyParts.All)
        {
            if (_fractured[(int)part])
                floor += Config.FracturePain;
            if (IsDestroyed(part))
                floor += Config.DestroyedPartPain;
        }
        floor = Mathf.Min(floor, 0.8f);
        Pain = Pain > floor ? Mathf.Max(floor, Pain - Config.PainFadePerSecond * dt) : floor;
    }

    // ---------- Healing (medical items arrive with inventory) ----------

    /// <summary>Full heal: HP, bleeding, fractures, pain (developer tool / respawning dummies).</summary>
    public void HealAll()
    {
        foreach (BodyPart part in BodyParts.All)
        {
            int i = (int)part;
            _hp[i] = Config.MaxFor(part);
            _bleed[i] = BleedLevel.None;
            _fractured[i] = false;
        }
        Pain = 0f;
        IsDead = false;
    }

    // ---------- Persistence ----------

    public Dictionary SaveState()
    {
        var parts = new Dictionary();
        foreach (BodyPart part in BodyParts.All)
        {
            int i = (int)part;
            parts[part.ToString()] = new Dictionary { ["hp"] = _hp[i], ["bleed"] = (int)_bleed[i], ["fractured"] = _fractured[i] };
        }
        return new Dictionary { ["parts"] = parts, ["pain"] = Pain, ["dead"] = IsDead };
    }

    public void LoadState(Dictionary state)
    {
        Dictionary parts = state["parts"].AsGodotDictionary();
        foreach (BodyPart part in BodyParts.All)
        {
            if (!parts.TryGetValue(part.ToString(), out Variant v))
                continue;
            Dictionary p = v.AsGodotDictionary();
            int i = (int)part;
            _hp[i] = p["hp"].AsSingle();
            _bleed[i] = (BleedLevel)p["bleed"].AsInt32();
            _fractured[i] = p["fractured"].AsBool();
        }
        Pain = state["pain"].AsSingle();
        IsDead = state["dead"].AsBool();
    }
}
