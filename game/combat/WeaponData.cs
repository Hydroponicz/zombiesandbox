using Godot;

namespace ZombieSandbox.Combat;

/// <summary>
/// Per-weapon tuning. Poses are relative to the holder's aim pivot (the eye): -Z is forward,
/// +X is right, +Y is up.
/// </summary>
[GlobalClass]
public partial class WeaponData : Resource
{
    [Export] public string DisplayName { get; set; } = "Rifle";
    [Export] public float MassKg { get; set; } = 3.4f;

    [ExportGroup("Firing")]
    [Export] public AmmoData Ammo { get; set; } = null!;
    [Export] public float FireRateRpm { get; set; } = 750f;
    [Export] public bool SupportsSemi { get; set; } = true;
    [Export] public bool SupportsAuto { get; set; } = true;
    /// <summary>Distance (m) at which the bullet crosses the sight line.</summary>
    [Export] public float ZeroDistance { get; set; } = 100f;

    [ExportGroup("Aiming")]
    /// <summary>Seconds to go from hip to fully aimed.</summary>
    [Export] public float AdsTime { get; set; } = 0.3f;
    /// <summary>First-person field of view while aiming through these sights.</summary>
    [Export] public float AdsFieldOfView { get; set; } = 60f;
    /// <summary>Heavier/longer weapons sway more.</summary>
    [Export] public float SwayMultiplier { get; set; } = 1f;
    /// <summary>Seconds to bring the weapon up after sprinting before it can fire.</summary>
    [Export] public float RaiseTime { get; set; } = 0.25f;

    [ExportGroup("Poses")]
    [Export] public Vector3 HipPosition { get; set; } = new(0.2f, -0.2f, -0.12f);
    [Export] public Vector3 HipRotationDegrees { get; set; } = Vector3.Zero;
    /// <summary>Carried while sprinting or pushed against a wall: muzzle down and across the body.</summary>
    [Export] public Vector3 LoweredPosition { get; set; } = new(0.16f, -0.2f, -0.36f);
    [Export] public Vector3 LoweredRotationDegrees { get; set; } = new(-15f, 40f, -15f);

    public static Transform3D Pose(Vector3 position, Vector3 rotationDegrees) =>
        new(Basis.FromEuler(new Vector3(
            Mathf.DegToRad(rotationDegrees.X),
            Mathf.DegToRad(rotationDegrees.Y),
            Mathf.DegToRad(rotationDegrees.Z))), position);
}
