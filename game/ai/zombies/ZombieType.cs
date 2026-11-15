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

    [ExportGroup("Attacks")]
    /// <summary>Lunge wind-up before a swipe/bite lands (s): the window to back off or shove.</summary>
    [Export] public float AttackWindup { get; set; } = 0.6f;
    [Export] public float AttackCooldown { get; set; } = 1.4f;
    /// <summary>Chance a landed attack turns into a grab.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float GrabChance { get; set; } = 0.35f;
    /// <summary>While holding on, it attacks this often (s) — mostly bites.</summary>
    [Export] public float GrabAttackInterval { get; set; } = 1.3f;
    [ExportSubgroup("Wounds: damage / bleed chances / infection chance")]
    [Export] public float ScratchDamage { get; set; } = 3f;
    [Export] public float LacerationDamage { get; set; } = 8f;
    [Export] public float BiteDamage { get; set; } = 14f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ScratchInfection { get; set; } = 0.85f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float LacerationInfection { get; set; } = 0.9f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float BiteInfection { get; set; } = 0.95f;
    /// <summary>Knocked down by a shove: how long until it gets back up (s).</summary>
    [Export] public float KnockdownSeconds { get; set; } = 2.5f;

    [ExportGroup("Behavior")]
    [Export] public float WanderRadius { get; set; } = 10f;
    /// <summary>Seconds spent searching around where the target was last seen.</summary>
    [Export] public float SearchSeconds { get; set; } = 20f;
    /// <summary>After reaching the last known position, guess where the target went from how it was moving.</summary>
    [Export] public bool PredictsMovement { get; set; } = true;
    /// <summary>Furthest the guess reaches beyond the last known position (m).</summary>
    [Export] public float MaxPredictionDistance { get; set; } = 15f;
    /// <summary>Seconds spent looking around at the source of a sound.</summary>
    [Export] public float InvestigateSeconds { get; set; } = 8f;
    /// <summary>Getting shot slows it down briefly.</summary>
    [Export] public float StaggerSeconds { get; set; } = 0.4f;
    /// <summary>Stops this close to its target (attacks come in piece B).</summary>
    [Export] public float ReachDistance { get; set; } = 1.1f;
}
