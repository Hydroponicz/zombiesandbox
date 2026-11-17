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
    private static readonly Dictionary<string, (Vector3 Position, double Time)> Shots = new();
    private static readonly Dictionary<string, Node> Movers = new();

    private static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>A member heard an enemy shooting from roughly here.</summary>
    public static void ReportShots(string group, Vector3 position)
    {
        if (group != "")
            Shots[group] = (position, Now);
    }

    public static Vector3? RecentShots(string group, double maxAgeSeconds) =>
        group != "" && Shots.TryGetValue(group, out var s) && Now - s.Time <= maxAgeSeconds ? s.Position : null;

    /// <summary>
    /// Only one member moves up at a time (searching or pushing); the rest watch and cover him.
    /// True if this one may go.
    /// </summary>
    public static bool TryClaimMover(string group, Node member)
    {
        if (group == "")
            return true;
        if (Movers.TryGetValue(group, out Node? current) && current != member && GodotObject.IsInstanceValid(current))
            return false;
        Movers[group] = member;
        return true;
    }

    public static void ReleaseMover(string group, Node member)
    {
        if (group != "" && Movers.TryGetValue(group, out Node? current) && current == member)
            Movers.Remove(group);
    }

    /// <summary>The member currently moving up (searching or pushing), if any.</summary>
    public static Node? Mover(string group) =>
        group != "" && Movers.TryGetValue(group, out Node? current) && GodotObject.IsInstanceValid(current) ? current : null;

    // Shared combat memory, used to judge who has the upper hand.
    private static readonly Dictionary<string, Combat> CombatMemory = new();

    public sealed class Combat
    {
        public double EnemyHitTime = -100.0;
        public double AllyHitTime = -100.0;
        public double EnemyReloadTime = -100.0;
        public double LastPushTime = -100.0;
        /// <summary>Most members alive at once: how many the group has lost.</summary>
        public int MostMembers;
    }

    public static Combat Memory(string group)
    {
        if (!CombatMemory.TryGetValue(group, out Combat? memory))
        {
            memory = new Combat();
            CombatMemory[group] = memory;
        }
        return memory;
    }

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

    public static void Clear()
    {
        Latest.Clear();
        Shots.Clear();
        Movers.Clear();
        CombatMemory.Clear();
    }
}
