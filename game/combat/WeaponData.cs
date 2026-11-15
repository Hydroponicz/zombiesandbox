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

    [ExportGroup("Magazines & reloading (times at Reloading skill 0)")]
    [Export] public int MagazineCapacity { get; set; } = 30;
    [Export] public PackedScene? DroppedMagazineScene { get; set; }
    /// <summary>Old mag back into the pouch, new mag in (s).</summary>
    [Export] public float TacticalReloadTime { get; set; } = 4.2f;
    /// <summary>Old mag dropped on the ground, new mag in (s).</summary>
    [Export] public float FastReloadTime { get; set; } = 3.0f;
    /// <summary>Extra time to rack the charging handle when the gun was empty (s).</summary>
    [Export] public float ChargingHandleTime { get; set; } = 1.3f;
    [Export] public float MagazineCheckTime { get; set; } = 1.5f;
    /// <summary>Pose while reloading / checking the magazine (gun canted, magazine toward you).</summary>
    [Export] public Vector3 ReloadPosition { get; set; } = new(0.06f, -0.24f, -0.3f);
    [Export] public Vector3 ReloadRotationDegrees { get; set; } = new(-8f, 18f, 38f);

    [ExportGroup("Condition & malfunctions (times at Maintenance skill 0)")]
    /// <summary>Condition lost per shot, in percent (before the Maintenance skill and heat).</summary>
    [Export] public float WearPerShot { get; set; } = 0.01f;
    /// <summary>Average rounds between jams at a given condition. Interpolated between these points.</summary>
    [Export] public float RoundsPerJamAt100 { get; set; } = 5000f;
    [Export] public float RoundsPerJamAt80 { get; set; } = 2000f;
    [Export] public float RoundsPerJamAt70 { get; set; } = 1200f;
    [Export] public float RoundsPerJamAt30 { get; set; } = 100f;
    [Export] public float RoundsPerJamAt0 { get; set; } = 25f;
    /// <summary>Look the weapon over: learn its condition and what kind of jam it has (s).</summary>
    [Export] public float InspectTime { get; set; } = 1.2f;
    /// <summary>Rack the charging handle to clear a simple jam (s).</summary>
    [Export] public float TapRackTime { get; set; } = 1.2f;
    /// <summary>Magazine out, rack several times, magazine back in (s).</summary>
    [Export] public float DoubleFeedFixTime { get; set; } = 4f;

    [ExportGroup("Recoil")]
    /// <summary>Upward kick per shot while aiming (degrees).</summary>
    [Export] public float VerticalRecoilDegrees { get; set; } = 0.9f;
    /// <summary>Maximum random sideways kick per shot while aiming (degrees).</summary>
    [Export] public float HorizontalRecoilDegrees { get; set; } = 0.22f;
    /// <summary>How hard the gun springs back to the point of aim. Higher = snappier return.</summary>
    [Export] public float RecoilRecoveryStiffness { get; set; } = 300f;
    /// <summary>Fraction of the kick the camera follows (the rest is the gun moving in view).</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float CameraRecoilFraction { get; set; } = 0.3f;
    /// <summary>Full auto: how much the point of aim climbs per shot after the first (degrees).</summary>
    [Export] public float ClimbPerShotDegrees { get; set; } = 0.1f;
    /// <summary>Full auto: the climb levels off here (degrees).</summary>
    [Export] public float MaxClimbDegrees { get; set; } = 0.75f;
    /// <summary>Seconds for the climb to settle back after the burst (time constant).</summary>
    [Export] public float ClimbRecoveryTime { get; set; } = 0.3f;
    /// <summary>Fraction of the climb the camera follows. High, so you see your sights walk up.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float CameraClimbFraction { get; set; } = 0.85f;
    /// <summary>How far the gun shoves back into the shoulder per shot (m).</summary>
    [Export] public float KickbackMeters { get; set; } = 0.025f;

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
