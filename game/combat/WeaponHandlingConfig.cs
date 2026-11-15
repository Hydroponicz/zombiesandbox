using Godot;

namespace ZombieSandbox.Combat;

/// <summary>How a character handles weapons: sway, breathing, lowering, wall collision.</summary>
[GlobalClass]
public partial class WeaponHandlingConfig : Resource
{
    [ExportGroup("Sway (degrees)")]
    /// <summary>Sway while aiming, standing still, rested.</summary>
    [Export] public float AimedSwayDegrees { get; set; } = 0.35f;
    /// <summary>Sway multiplier at the hip compared to aimed.</summary>
    [Export] public float HipSwayMultiplier { get; set; } = 2.5f;
    [Export] public float CrouchSwayMultiplier { get; set; } = 0.7f;
    [Export] public float ProneSwayMultiplier { get; set; } = 0.4f;
    /// <summary>Extra sway at running speed (scaled by how fast you move).</summary>
    [Export] public float MovingSwayGain { get; set; } = 2f;
    /// <summary>Extra sway when out of breath (leg stamina empty). Scales with the square of breathlessness.</summary>
    [Export] public float BreathlessSwayGain { get; set; } = 3f;
    /// <summary>Sway also gets faster when out of breath.</summary>
    [Export] public float BreathlessSwaySpeedGain { get; set; } = 1.5f;

    [ExportGroup("Hold breath")]
    [Export(PropertyHint.Range, "0,1,0.05")] public float HoldBreathSwayMultiplier { get; set; } = 0.15f;
    /// <summary>Sway multiplier while gasping after running out of breath.</summary>
    [Export] public float GaspSwayMultiplier { get; set; } = 2.5f;
    [Export] public float GaspDuration { get; set; } = 2f;
    /// <summary>Seconds to refill breath from empty.</summary>
    [Export] public float BreathRefillSeconds { get; set; } = 4f;
    /// <summary>Breath (fraction) needed before holding it again after a gasp.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float BreathNeededAfterGasp { get; set; } = 0.5f;

    [ExportGroup("Recoil")]
    [Export] public float HipRecoilMultiplier { get; set; } = 1.6f;
    [Export] public float CrouchRecoilMultiplier { get; set; } = 0.85f;
    [Export] public float ProneRecoilMultiplier { get; set; } = 0.6f;
    /// <summary>After the first few shots of a burst, vertical kick eases to this fraction.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float SustainedFireVerticalMultiplier { get; set; } = 0.3f;
    /// <summary>Shots at full vertical kick before it starts easing.</summary>
    [Export] public int BurstClimbShots { get; set; } = 3;
    /// <summary>A pause longer than this (s) starts a new burst.</summary>
    [Export] public float BurstResetTime { get; set; } = 0.25f;

    [ExportGroup("Aiming XP")]
    /// <summary>Aiming XP for hitting a target up close.</summary>
    [Export] public float AimingXpPerHit { get; set; } = 1.5f;
    /// <summary>Extra XP multiplier per 100 m of distance (1 = double XP at 100 m).</summary>
    [Export] public float AimingXpRangeBonusPer100m { get; set; } = 1f;

    [ExportGroup("Lowering & walls")]
    /// <summary>Seconds to lower the weapon when starting to sprint.</summary>
    [Export] public float LowerTime { get; set; } = 0.2f;
    /// <summary>Most the weapon can be pulled back toward the body near a wall (m).</summary>
    [Export] public float MaxWallPullBack { get; set; } = 0.3f;
    /// <summary>Pulled back further than this, the weapon starts to lower and can't fire (m).</summary>
    [Export] public float WallPullBackBlocksAt { get; set; } = 0.12f;
}
