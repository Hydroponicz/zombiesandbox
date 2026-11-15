using Godot;
using ZombieSandbox.Combat;

namespace ZombieSandbox.Health;

/// <summary>
/// One body part's hit volume. Bullets hit these (physics layer "hitboxes"), never the movement
/// collider. Finds the character's <see cref="BodyHealth"/> (a sibling-of-ancestor node named "Health").
/// </summary>
public partial class Hitbox : StaticBody3D, IHittable
{
    [Export] public BodyPart Part { get; set; }

    private BodyHealth? _health;

    public BodyHealth? Health => _health ??= FindHealth();

    private BodyHealth? FindHealth()
    {
        for (Node? node = GetParent(); node != null; node = node.GetParent())
        {
            if (node.GetNodeOrNull<BodyHealth>("Health") is { } health)
                return health;
        }
        return null;
    }

    public bool OnBulletHit(in BulletHit hit)
    {
        BodyHealth? health = Health;
        if (health == null || health.IsDead)
            return false;
        // Damage falls off with the bullet's remaining speed.
        float speedFactor = Mathf.Clamp(hit.Velocity.Length() / hit.Ammo.MuzzleVelocity, 0.3f, 1f);
        health.ApplyBulletHit(Part, hit.Ammo.Damage * speedFactor);
        return true;
    }
}
