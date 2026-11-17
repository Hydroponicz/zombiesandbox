using Godot;

namespace ZombieSandbox.AI;

/// <summary>
/// What kind of survivor this is: skills (which drive their real weapon handling), senses and combat
/// temperament. Difficulty comes from these numbers — NPCs use the same weapon rules as the player.
/// </summary>
[GlobalClass]
public partial class SurvivorType : Resource
{
    [Export] public string DisplayName { get; set; } = "Bandit";

    [ExportGroup("Skills (0–10)")]
    [Export] public int Aiming { get; set; } = 2;
    [Export] public int Strength { get; set; } = 3;
    [Export] public int Reloading { get; set; } = 4;
    [Export] public int Maintenance { get; set; } = 3;
    [Export] public int Fitness { get; set; } = 4;

    [ExportGroup("Senses")]
    [Export] public float SightRangeDay { get; set; } = 60f;
    [Export] public float SightRangeNight { get; set; } = 15f;
    [Export] public float FieldOfViewDegrees { get; set; } = 130f;
    /// <summary>Seconds to notice someone at the edge of sight range (much faster up close).</summary>
    [Export] public float DetectionTime { get; set; } = 2.5f;

    [ExportGroup("Combat")]
    /// <summary>Seconds to settle the aim on a newly seen target (human reaction + aiming).</summary>
    [Export] public float AimSettleTime { get; set; } = 1.8f;
    /// <summary>Starting aim error when a target appears (m at the target, before settling).</summary>
    [Export] public float InitialAimError { get; set; } = 2.0f;
    /// <summary>Hand wobble that never settles, re-rolled each shot (m at 20 m; worse when suppressed).</summary>
    [Export] public float AimWobble { get; set; } = 0.5f;
    [Export] public float ShotIntervalMin { get; set; } = 0.6f;
    [Export] public float ShotIntervalMax { get; set; } = 1.3f;
    /// <summary>Seconds popped up shooting, then seconds hidden, when in low cover.</summary>
    [Export] public float ExposeSeconds { get; set; } = 1.8f;
    [Export] public float HideSeconds { get; set; } = 2.5f;
    /// <summary>How far to look for cover (m).</summary>
    [Export] public float CoverSearchRadius { get; set; } = 10f;
    /// <summary>Health danger (0..1) at which it falls back.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float RetreatDanger { get; set; } = 0.55f;
    /// <summary>Zombies closer than this get dealt with first (m).</summary>
    [Export] public float ZombieThreatRange { get; set; } = 20f;
    [Export] public float SearchSeconds { get; set; } = 25f;

    [ExportGroup("Aggression")]
    /// <summary>
    /// How much of an upper hand (see DESIGN.md: numbers, landed hits, enemy reloading, enemy missing)
    /// the group needs before one member pushes the enemy's position. Lower = more aggressive.
    /// </summary>
    [Export] public float PushThreshold { get; set; } = 0.6f;
    /// <summary>Seconds the group waits after a push before the next one.</summary>
    [Export] public float PushCooldown { get; set; } = 6f;
    /// <summary>Seconds between covering shots at the enemy's hiding spot while a mate moves.</summary>
    [Export] public float CoveringFireIntervalMin { get; set; } = 0.9f;
    [Export] public float CoveringFireIntervalMax { get; set; } = 1.8f;

    [ExportGroup("Movement (m/s)")]
    [Export] public float RunSpeed { get; set; } = 3.4f;
    [Export] public float WalkSpeed { get; set; } = 1.8f;
    [Export] public float CrouchSpeed { get; set; } = 1.4f;
}
