using Godot;

namespace ZombieSandbox.AI;

/// <summary>
/// Stats for a kind of zombie. Shamblers now; mutated runners later use the same component with
/// a different type resource (see design doc, step 4 vision).
/// </summary>
[GlobalClass]
public partial class ZombieType : Resource
{
    [ExportGroup("Movement (m/s)")]
    /// <summary>Each zombie picks a chase speed in this range.</summary>
    [Export] public float ChaseSpeedMin { get; set; } = 1.0f;
    [Export] public float ChaseSpeedMax { get; set; } = 1.6f;
    [Export] public float WanderSpeed { get; set; } = 0.45f;
    [Export] public float CrawlSpeed { get; set; } = 0.35f;
    [Export] public float Acceleration { get; set; } = 4f;
    [Export] public float TurnSpeedDegrees { get; set; } = 220f;

    [ExportGroup("Senses")]
    [Export] public float SightRangeDay { get; set; } = 25f;
    [Export] public float SightRangeNight { get; set; } = 8f;
    [Export] public float FieldOfViewDegrees { get; set; } = 120f;
    [Export] public float HearingMultiplier { get; set; } = 1f;

    [ExportGroup("Behavior")]
    [Export] public float WanderRadius { get; set; } = 10f;
    /// <summary>Seconds spent searching around where the target was last seen.</summary>
    [Export] public float SearchSeconds { get; set; } = 20f;
    /// <summary>Seconds spent looking around at the source of a sound.</summary>
    [Export] public float InvestigateSeconds { get; set; } = 8f;
    /// <summary>Getting shot slows it down briefly.</summary>
    [Export] public float StaggerSeconds { get; set; } = 0.4f;
    /// <summary>Stops this close to its target (attacks come in piece B).</summary>
    [Export] public float ReachDistance { get; set; } = 1.1f;
}
