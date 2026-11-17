using System.Collections.Generic;
using Godot;

namespace ZombieSandbox.AI;

/// <summary>
/// Shared knowledge for a small group of survivors (2–4): when one spots an enemy, the others learn
/// where it is. Keyed by the group's id. (Factions and attitudes come in piece B.)
/// </summary>
public static class SurvivorGroup
{
    public readonly record struct Sighting(Vector3 Position, double Time, ISenseTarget Target);

    private static readonly Dictionary<string, Sighting> Latest = new();

    public static void Report(string group, ISenseTarget target, Vector3 position)
    {
        if (group == "")
            return;
        Latest[group] = new Sighting(position, Time.GetTicksMsec() / 1000.0, target);
    }

    /// <summary>The group's most recent sighting, if any within <paramref name="maxAgeSeconds"/>.</summary>
    public static Sighting? Recent(string group, double maxAgeSeconds)
    {
        if (group == "" || !Latest.TryGetValue(group, out Sighting sighting))
            return null;
        return Time.GetTicksMsec() / 1000.0 - sighting.Time <= maxAgeSeconds ? sighting : null;
    }

    public static void Clear() => Latest.Clear();
}
