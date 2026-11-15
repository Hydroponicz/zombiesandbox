using System.Collections.Generic;
using Godot;

namespace ZombieSandbox.AI;

/// <summary>
/// Something AI can see: the player now, NPCs later. Register with <see cref="SenseTargets"/>.
/// </summary>
public interface ISenseTarget
{
    /// <summary>Point to check line of sight against (around the upper chest/head).</summary>
    Vector3 SightPoint { get; }
    /// <summary>Feet position (for navigation).</summary>
    Vector3 GroundPosition { get; }
    /// <summary>How easy to spot: 1 = standing and moving normally; lower crouched/prone/still.</summary>
    float Visibility { get; }
    /// <summary>Physics bodies to ignore in line-of-sight checks (the target's own colliders).</summary>
    Godot.Collections.Array<Rid> SightExclude { get; }
    bool IsAlive { get; }
}

public static class SenseTargets
{
    public static readonly List<ISenseTarget> All = new();
}
