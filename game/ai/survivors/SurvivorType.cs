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
    [Export] public int Blunt { get; set; } = 3;
    [Export] public int Blade { get; set; } = 3;

    [ExportGroup("Melee")]
    /// <summary>
    /// Melee weapons this kind of survivor may carry: each one spawns with one picked at random
    /// (empty = none, shove only). A survivor placed in a level can override it.
    /// </summary>
    [Export] public Godot.Collections.Array<PackedScene> MeleeWeapons { get; set; } = new();
    /// <summary>A zombie this close: switch to the melee weapon (m).</summary>
    [Export] public float MeleeSwitchRange { get; set; } = 2.5f;
    /// <summary>Chance a swing is a charged heavy one.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float HeavySwingChance { get; set; } = 0.35f;

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
    /// <summary>Won't push or search for enemies further than this from its home spot (m).</summary>
    [Export] public float LeashRange { get; set; } = 35f;

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
    /// <summary>
    /// Runs from a losing fight when the group's upper hand drops below this (solo ≈0.2, a fresh pair
    /// ≈0.45). Anything at or below -50 never runs (bandits).
    /// </summary>
    [Export] public float FleeThreshold { get; set; } = -99f;

    [ExportGroup("Movement (m/s)")]
    [Export] public float RunSpeed { get; set; } = 3.4f;
    [Export] public float WalkSpeed { get; set; } = 1.8f;
    [Export] public float CrouchSpeed { get; set; } = 1.4f;
}
