using Godot;

namespace ZombieSandbox.Combat;

/// <summary>A cartridge type. Damage and penetration come with health (build step 3) and materials.</summary>
[GlobalClass]
public partial class AmmoData : Resource
{
    [Export] public string DisplayName { get; set; } = "5.56x45mm";
    [Export] public float MuzzleVelocity { get; set; } = 900f;
    /// <summary>Damage to a body part at full speed (falls off as the bullet slows down).</summary>
    [Export] public float Damage { get; set; } = 55f;
    /// <summary>Speed lost to air: velocity falls by roughly e^(-DragPerMeter × distance).</summary>
    [Export(PropertyHint.Range, "0,0.01,0.0001")] public float DragPerMeter { get; set; } = 0.0011f;
    [Export] public float BulletMassGrams { get; set; } = 4f;
    /// <summary>Bullets are removed after this long in flight.</summary>
    [Export] public float MaxFlightTime { get; set; } = 4f;
}
