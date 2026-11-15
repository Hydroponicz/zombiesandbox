using System;
using Godot;
using Godot.Collections;
using ZombieSandbox.Core;

namespace ZombieSandbox.World;

/// <summary>
/// Autoload. Game time. One game day lasts <see cref="RealSecondsPerGameDay"/> real seconds
/// (3 hours): daylight 06:00–22:00 is 2 real hours, night 22:00–06:00 is 1 real hour.
/// Seasons will change sunrise/sunset later.
/// </summary>
public partial class WorldClock : Node, ISaveState
{
    public const double RealSecondsPerGameDay = 3 * 60 * 60;
    public const double GameSecondsPerDay = 24 * 60 * 60;
    public const float Sunrise = 6f;
    public const float Sunset = 22f;
    /// <summary>Hours of dawn/dusk on each side of sunrise/sunset.</summary>
    public const float Twilight = 1f;

    public static WorldClock Instance { get; private set; } = null!;

    /// <summary>Game seconds since the start of day 1, 00:00.</summary>
    public double GameSeconds { get; private set; } = 8 * 60 * 60; // Start on day 1 at 08:00.
    /// <summary>Developer fast-forward multiplier.</summary>
    public double TimeScale { get; set; } = 1;

    public int Day => (int)(GameSeconds / GameSecondsPerDay) + 1;
    /// <summary>0–24, fractional.</summary>
    public float Hour => (float)(GameSeconds % GameSecondsPerDay / 3600.0);
    public double GameHours => GameSeconds / 3600.0;

    /// <summary>0 = night, 1 = full daylight, smooth through dawn and dusk.</summary>
    public float Daylight
    {
        get
        {
            float h = Hour;
            float dawn = Mathf.SmoothStep(Sunrise - Twilight, Sunrise + Twilight, h);
            float dusk = 1f - Mathf.SmoothStep(Sunset - Twilight, Sunset + Twilight, h);
            return Mathf.Min(dawn, dusk);
        }
    }

    public override void _EnterTree() => Instance = this;

    public override void _Process(double delta)
    {
        GameSeconds += delta * GameSecondsPerDay / RealSecondsPerGameDay * TimeScale;
    }

    public void AdvanceGameHours(double hours) => GameSeconds += hours * 3600.0;

    public string TimeText => $"Day {Day}, {(int)Hour:00}:{(int)(Hour % 1f * 60f):00}";

    public Dictionary SaveState() => new() { ["gameSeconds"] = GameSeconds };

    public void LoadState(Dictionary state) => GameSeconds = state["gameSeconds"].AsDouble();
}
