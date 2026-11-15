using Godot;

namespace ZombieSandbox.Health;

[GlobalClass]
public partial class HealthConfig : Resource
{
    [ExportGroup("Max HP")]
    [Export] public float HeadMax { get; set; } = 35f;
    [Export] public float ChestMax { get; set; } = 85f;
    [Export] public float StomachMax { get; set; } = 70f;
    [Export] public float ArmMax { get; set; } = 60f;
    [Export] public float LegMax { get; set; } = 65f;
    /// <summary>Damage to a destroyed part spreads to the rest of the body, multiplied by this.</summary>
    [Export] public float DestroyedPartSpreadMultiplier { get; set; } = 0.7f;

    [ExportGroup("Bullets")]
    [Export(PropertyHint.Range, "0,1,0.05")] public float BulletLightBleedChance { get; set; } = 0.35f;
    [Export(PropertyHint.Range, "0,1,0.05")] public float BulletHeavyBleedChance { get; set; } = 0.15f;
    [Export(PropertyHint.Range, "0,1,0.05")] public float BulletLimbFractureChance { get; set; } = 0.2f;

    [ExportGroup("Bleeding (HP per second on that part)")]
    [Export] public float LightBleedRate { get; set; } = 0.5f;
    [Export] public float HeavyBleedRate { get; set; } = 2f;

    [ExportGroup("Falls")]
    /// <summary>Drops shorter than this don't hurt (m).</summary>
    [Export] public float SafeFallHeight { get; set; } = 3f;
    /// <summary>Damage to each leg per meter above the safe height.</summary>
    [Export] public float FallLegDamagePerMeter { get; set; } = 14f;
    /// <summary>Fracture chance per leg per meter above the safe height.</summary>
    [Export] public float FallFractureChancePerMeter { get; set; } = 0.2f;
    /// <summary>Above this height the impact also hurts the chest and stomach (m).</summary>
    [Export] public float FallTorsoHeight { get; set; } = 6f;
    [Export] public float FallTorsoDamagePerMeter { get; set; } = 13f;

    [ExportGroup("Pain")]
    /// <summary>Pain added per point of damage.</summary>
    [Export] public float PainPerDamage { get; set; } = 0.017f;
    [Export] public float PainFadePerSecond { get; set; } = 0.05f;
    /// <summary>Lasting pain from each fracture / destroyed part.</summary>
    [Export] public float FracturePain { get; set; } = 0.25f;
    [Export] public float DestroyedPartPain { get; set; } = 0.3f;

    [ExportGroup("Effects")]
    [Export] public float FracturedLegSpeed { get; set; } = 0.55f;
    [Export] public float DestroyedLegSpeed { get; set; } = 0.4f;
    [Export] public float FracturedArmSway { get; set; } = 1.6f;
    [Export] public float DestroyedArmSway { get; set; } = 2.2f;
    /// <summary>Extra sway at full pain.</summary>
    [Export] public float PainSway { get; set; } = 1f;

    [ExportGroup("Medical XP")]
    [Export] public float MedicalXpPerDamage { get; set; } = 0.05f;

    public float MaxFor(BodyPart part) => part switch
    {
        BodyPart.Head => HeadMax,
        BodyPart.Chest => ChestMax,
        BodyPart.Stomach => StomachMax,
        BodyPart.LeftArm or BodyPart.RightArm => ArmMax,
        _ => LegMax,
    };
}
