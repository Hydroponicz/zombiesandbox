using Godot;

namespace ZombieSandbox.Combat;

public enum MeleeKind
{
    Blunt,
    Blade,
}

/// <summary>
/// What a melee weapon is and how it fights. Times and damage are at Blunt/Blade skill 0 with a rested
/// character; skill, stamina and Strength scale them (see WeaponHolder.Melee).
/// </summary>
[GlobalClass]
public partial class MeleeData : Resource
{
    [Export] public string DisplayName { get; set; } = "Melee weapon";
    [Export] public MeleeKind Kind { get; set; } = MeleeKind.Blunt;

    [ExportGroup("Damage")]
    /// <summary>Damage of a quick swing to the body part it lands on.</summary>
    [Export] public float Damage { get; set; } = 14f;
    /// <summary>Extra multiplier on head hits (a kitchen knife is weak against skulls).</summary>
    [Export] public float HeadMultiplier { get; set; } = 1f;
    /// <summary>A fully charged heavy swing does this much more damage.</summary>
    [Export] public float HeavyMultiplier { get; set; } = 1.8f;
    [Export(PropertyHint.Range, "0,1,0.05")] public float LightBleedChance { get; set; }
    [Export(PropertyHint.Range, "0,1,0.05")] public float HeavyBleedChance { get; set; }
    /// <summary>Chance to break an arm or leg it hits (more on a heavy swing).</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float FractureChance { get; set; }

    [ExportGroup("Swing (seconds)")]
    /// <summary>From the eye (m).</summary>
    [Export] public float Reach { get; set; } = 1.8f;
    [Export] public float WindupTime { get; set; } = 0.28f;
    [Export] public float StrikeTime { get; set; } = 0.12f;
    [Export] public float RecoveryTime { get; set; } = 0.35f;
    /// <summary>Added to recovery on a miss: the swing carries through.</summary>
    [Export] public float MissPenalty { get; set; } = 0.25f;
    /// <summary>Holding attack this long or longer charges a heavy swing; full charge at ChargeTime.</summary>
    [Export] public float ChargeTime { get; set; } = 0.9f;
    [Export] public float StaminaCost { get; set; } = 10f;

    [ExportGroup("Impact")]
    /// <summary>How hard it pushes a zombie back (1 = a full rifle-butt shove).</summary>
    [Export] public float Knockback { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,1,0.05")] public float KnockdownChance { get; set; } = 0.1f;
    [Export(PropertyHint.Range, "0,1,0.05")] public float HeavyKnockdownChance { get; set; } = 0.5f;

    [ExportGroup("Wear")]
    /// <summary>Condition (%) lost per hit; the weapon breaks at 0.</summary>
    [Export] public float WearPerHit { get; set; } = 1.2f;

    [ExportGroup("Getting stuck (blades)")]
    [Export(PropertyHint.Range, "0,1,0.05")] public float StickChanceBody { get; set; }
    [Export(PropertyHint.Range, "0,1,0.05")] public float StickChanceHead { get; set; }
    [Export] public float PullFreeBodyTime { get; set; } = 0.6f;
    [Export] public float PullFreeHeadTime { get; set; } = 1.1f;

    [ExportGroup("Pose (relative to the eye, metres / degrees)")]
    [Export] public Vector3 HoldPosition { get; set; } = new(0.28f, -0.32f, -0.45f);
    [Export] public Vector3 HoldRotationDegrees { get; set; } = new(-20f, 10f, -15f);
    [Export] public Vector3 LoweredPosition { get; set; } = new(0.3f, -0.6f, -0.3f);
    [Export] public Vector3 LoweredRotationDegrees { get; set; } = new(-70f, 20f, -10f);
    // The swing's key poses: grip position, and the direction the weapon points from the grip
    // (x right, y up, -z forward from the eye).
    /// <summary>Cocked back over the right shoulder.</summary>
    [Export] public Vector3 WindupPosition { get; set; } = new(0.36f, -0.12f, -0.32f);
    [Export] public Vector3 WindupDirection { get; set; } = new(0.55f, 0.8f, 0.2f);
    /// <summary>Mid-swing: straight at what you're aiming at.</summary>
    [Export] public Vector3 StrikePosition { get; set; } = new(0.08f, -0.22f, -0.42f);
    [Export] public Vector3 StrikeDirection { get; set; } = new(-0.05f, 0.1f, -1f);
    /// <summary>End of the swing, low on the left.</summary>
    [Export] public Vector3 FollowThroughPosition { get; set; } = new(-0.28f, -0.38f, -0.38f);
    [Export] public Vector3 FollowThroughDirection { get; set; } = new(-0.85f, -0.35f, -0.4f);
}
