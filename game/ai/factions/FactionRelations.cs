using System;
using System.Collections.Generic;
using Godot;
using ZombieSandbox.Core;
using ZombieSandbox.World;

namespace ZombieSandbox.AI;

/// <summary>
/// Autoload: every faction's current attitude toward the player, when it was angered, and whether a
/// friendly faction has just forgiven a hit. Persistent (keyed by faction id).
/// </summary>
public partial class FactionRelations : Node, IPersistent
{
    public static FactionRelations Instance { get; private set; } = null!;

    /// <summary>A faction's attitude toward the player changed (faction id).</summary>
    public static event Action<string>? Changed;

    public string PersistentId => "faction_relations";

    private sealed class Relation
    {
        public FactionData Faction = null!;
        public Attitude Current;
        public double AngeredAtHours = -1.0;
        public double ForgivenAtHours = -1000.0;
    }

    private readonly System.Collections.Generic.Dictionary<string, Relation> _relations = new();

    public override void _EnterTree() => Instance = this;

    private static double Hours => WorldClock.Instance?.GameHours ?? 0.0;

    private Relation Get(FactionData faction)
    {
        if (!_relations.TryGetValue(faction.Id, out Relation? relation))
        {
            relation = new Relation { Faction = faction, Current = faction.StartingAttitude };
            _relations[faction.Id] = relation;
            ApplyPending(relation);
        }
        relation.Faction = faction;
        // Grudges fade: back to the starting attitude after a few in-game weeks.
        if (relation.Current == Attitude.Hostile && faction.StartingAttitude != Attitude.Hostile
            && relation.AngeredAtHours >= 0.0 && Hours - relation.AngeredAtHours > faction.CalmDownDays * 24.0)
        {
            relation.Current = faction.StartingAttitude;
            relation.AngeredAtHours = -1.0;
            Changed?.Invoke(faction.Id);
        }
        return relation;
    }

    public Attitude AttitudeOf(FactionData faction) => Get(faction).Current;

    /// <summary>The player attacked the faction (or pushed a member too far): it turns hostile.</summary>
    public void Anger(FactionData faction)
    {
        Relation relation = Get(faction);
        relation.AngeredAtHours = Hours;
        if (relation.Current == Attitude.Hostile)
            return;
        relation.Current = Attitude.Hostile;
        Changed?.Invoke(faction.Id);
    }

    /// <summary>Friendlies let one accidental hit go per in-game hour. True if this one is forgiven.</summary>
    public bool TryForgiveHit(FactionData faction)
    {
        Relation relation = Get(faction);
        if (relation.Current != Attitude.Friendly || Hours - relation.ForgivenAtHours < 1.0)
            return false;
        relation.ForgivenAtHours = Hours;
        return true;
    }

    public static bool AreEnemies(FactionData? a, FactionData? b)
    {
        if (a == null || b == null)
            return a != b;
        return a.Id != b.Id && (a.HostileToEveryone || b.HostileToEveryone);
    }

    /// <summary>Developer: every faction back to its starting attitude.</summary>
    public void ResetAll()
    {
        foreach (Relation relation in _relations.Values)
        {
            relation.Current = relation.Faction.StartingAttitude;
            relation.AngeredAtHours = -1.0;
            relation.ForgivenAtHours = -1000.0;
            Changed?.Invoke(relation.Faction.Id);
        }
    }

    /// <summary>For the developer overlay.</summary>
    public IEnumerable<(string Name, Attitude Attitude)> All()
    {
        foreach (Relation relation in _relations.Values)
            yield return (relation.Faction.DisplayName, Get(relation.Faction).Current);
    }

    public Godot.Collections.Dictionary SaveState()
    {
        var factions = new Godot.Collections.Dictionary();
        foreach ((string id, Relation relation) in _relations)
        {
            factions[id] = new Godot.Collections.Dictionary
            {
                ["attitude"] = (int)relation.Current,
                ["angered_at"] = relation.AngeredAtHours,
                ["forgiven_at"] = relation.ForgivenAtHours,
            };
        }
        return new Godot.Collections.Dictionary { ["factions"] = factions };
    }

    /// <summary>Restores attitudes for factions already known; others apply when first met.</summary>
    public void LoadState(Godot.Collections.Dictionary state)
    {
        if (!state.TryGetValue("factions", out Variant value))
            return;
        foreach ((Variant key, Variant entry) in value.AsGodotDictionary())
        {
            Godot.Collections.Dictionary data = entry.AsGodotDictionary();
            _pending[key.AsString()] = ((Attitude)data["attitude"].AsInt32(), data["angered_at"].AsDouble(), data["forgiven_at"].AsDouble());
        }
        foreach (Relation relation in _relations.Values)
            ApplyPending(relation);
    }

    private readonly System.Collections.Generic.Dictionary<string, (Attitude, double, double)> _pending = new();

    private void ApplyPending(Relation relation)
    {
        if (!_pending.Remove(relation.Faction.Id, out var saved))
            return;
        (relation.Current, relation.AngeredAtHours, relation.ForgivenAtHours) = saved;
    }
}
