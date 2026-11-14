using Godot;

namespace ZombieSandbox.Combat;

/// <summary>Everything about a bullet's impact.</summary>
public readonly record struct BulletHit(
    Vector3 Position,
    Vector3 Normal,
    Vector3 Velocity,
    float DistanceTravelled,
    AmmoData Ammo,
    Node? Shooter);

/// <summary>
/// Implemented by anything that reacts to being shot (targets now; characters with health in step 3).
/// Put it on the physics body the bullet's ray hits.
/// </summary>
public interface IHittable
{
    /// <summary>Returns true if this was a real target (counts toward the shooter's Aiming XP).</summary>
    bool OnBulletHit(in BulletHit hit);
}
