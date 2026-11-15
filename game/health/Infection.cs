using Godot;
using Godot.Collections;
using ZombieSandbox.Core;
using ZombieSandbox.World;

namespace ZombieSandbox.Health;

public enum InfectionStage
{
    None,
    /// <summary>Infected, no symptoms yet. The player can't tell.</summary>
    Incubating,
    Fever,
    Sick,
}

/// <summary>
/// The zombie virus. Exposure (any wound from the infected) has an 85–95% chance to infect.
/// Never shown as "infected": the player only feels symptoms — fever, then sickness that drains
/// the body until death, 2–3 game days after infection. Runs on game time (<see cref="WorldClock"/>).
/// </summary>
public partial class Infection : Node, ISaveState
{
    [Export] public float IncubationHoursMin { get; set; } = 6f;
    [Export] public float IncubationHoursMax { get; set; } = 12f;
    [Export] public float FeverHoursMin { get; set; } = 14f;
    [Export] public float FeverHoursMax { get; set; } = 20f;
    [Export] public float DeathAfterHoursMin { get; set; } = 48f;
    [Export] public float DeathAfterHoursMax { get; set; } = 72f;

    public bool IsInfected { get; private set; }
    public InfectionStage Stage { get; private set; } = InfectionStage.None;
    /// <summary>0..1 how far the illness has gone (0 until symptoms start).</summary>
    public float Severity { get; private set; }

    private BodyHealth _health = null!;
    private readonly RandomNumberGenerator _rng = new();
    private double _infectedAt;
    private double _feverAt;
    private double _sickAt;
    private double _deathAt;
    private double _lastHours = -1;

    public override void _Ready()
    {
        _rng.Randomize();
        _health = GetNode<BodyHealth>("../Health");
        _health.Died += () => _lastHours = -1;
    }

    /// <summary>Contact with the infected (a wound or blood). Rolls the infection chance.</summary>
    public void Expose(float chance)
    {
        if (IsInfected || _health.IsDead || _rng.Randf() >= chance)
            return;
        Infect();
    }

    public void Infect()
    {
        if (IsInfected)
            return;
        IsInfected = true;
        Stage = InfectionStage.Incubating;
        _infectedAt = WorldClock.Instance.GameHours;
        _feverAt = _infectedAt + _rng.RandfRange(IncubationHoursMin, IncubationHoursMax);
        _sickAt = _feverAt + _rng.RandfRange(FeverHoursMin, FeverHoursMax);
        _deathAt = _infectedAt + _rng.RandfRange(DeathAfterHoursMin, DeathAfterHoursMax);
        _deathAt = Mathf.Max(_deathAt, _sickAt + 6.0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsInfected || _health.IsDead)
            return;
        double now = WorldClock.Instance.GameHours;
        double elapsed = _lastHours < 0 ? 0 : now - _lastHours;
        _lastHours = now;

        if (now < _feverAt)
        {
            Stage = InfectionStage.Incubating;
            Severity = 0f;
        }
        else if (now < _sickAt)
        {
            Stage = InfectionStage.Fever;
            Severity = 0.3f * (float)((now - _feverAt) / (_sickAt - _feverAt));
        }
        else
        {
            Stage = InfectionStage.Sick;
            Severity = 0.3f + 0.7f * Mathf.Clamp((float)((now - _sickAt) / (_deathAt - _sickAt)), 0f, 1f);

            // The body gives out: the chest is drained so it fails right at the time of death.
            double hoursLeft = _deathAt - now;
            if (hoursLeft <= 0)
            {
                _health.Kill("You succumbed to the infection");
                return;
            }
            _health.PendingDeathCause = "You succumbed to the infection";
            float chest = _health.Hp(BodyPart.Chest);
            float drain = (float)(chest * Mathf.Min(elapsed / hoursLeft, 1.0));
            if (drain > 0f)
                _health.ApplyDamage(BodyPart.Chest, drain, DamageSource.Infection);
        }

        _health.ExtraPainFloor = Stage switch { InfectionStage.Fever => 0.15f, InfectionStage.Sick => 0.3f + 0.3f * Severity, _ => 0f };
    }

    /// <summary>What the character feels (never says "infected").</summary>
    public string SymptomsText => Stage switch
    {
        InfectionStage.Fever => "Feverish, aching",
        InfectionStage.Sick => Severity > 0.7f ? "Burning up, can barely stand" : "Very sick, nauseous",
        _ => "",
    };

    /// <summary>Extra weapon sway from fever shakes.</summary>
    public float SwayMultiplier => 1f + Severity * 0.8f;

    /// <summary>Less stamina as the illness takes hold.</summary>
    public float StaminaMultiplier => 1f - Severity * 0.5f;

    public void Cure()
    {
        _health.PendingDeathCause = "";
        IsInfected = false;
        Stage = InfectionStage.None;
        Severity = 0f;
        _health.ExtraPainFloor = 0f;
    }

    public Dictionary SaveState() => new()
    {
        ["infected"] = IsInfected,
        ["infectedAt"] = _infectedAt,
        ["feverAt"] = _feverAt,
        ["sickAt"] = _sickAt,
        ["deathAt"] = _deathAt,
    };

    public void LoadState(Dictionary state)
    {
        IsInfected = state["infected"].AsBool();
        _infectedAt = state["infectedAt"].AsDouble();
        _feverAt = state["feverAt"].AsDouble();
        _sickAt = state["sickAt"].AsDouble();
        _deathAt = state["deathAt"].AsDouble();
        _lastHours = -1;
    }
}
