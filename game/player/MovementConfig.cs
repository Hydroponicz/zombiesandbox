using Godot;

namespace ZombieSandbox.Player;

/// <summary>Base movement tuning. Skills/injuries modify these through CharacterStats.</summary>
[GlobalClass]
public partial class MovementConfig : Resource
{
    [ExportGroup("Speeds (m/s)")]
    [Export] public float WalkSpeed { get; set; } = 2.0f;
    [Export] public float RunSpeed { get; set; } = 3.5f;
    [Export] public float SprintSpeed { get; set; } = 5.5f;
    [Export] public float CrouchSpeed { get; set; } = 1.5f;
    [Export] public float ProneSpeed { get; set; } = 0.6f;

    [ExportGroup("Momentum (m/s²)")]
    [Export] public float Acceleration { get; set; } = 14f;
    [Export] public float Deceleration { get; set; } = 16f;
    /// <summary>Lower than Acceleration: changing direction at a sprint is slow.</summary>
    [Export] public float SprintAcceleration { get; set; } = 7f;
    /// <summary>Fraction of normal acceleration available in the air.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float AirControl { get; set; } = 0.15f;

    [ExportGroup("Steps")]
    /// <summary>Tallest ledge (curb, stair) the character walks up without jumping. Also used to stick to the ground going down stairs.</summary>
    [Export] public float MaxStepHeight { get; set; } = 0.3f;

    [ExportGroup("Jump & gravity")]
    /// <summary>How high a jump lifts your feet (m). Jump speed is calculated from this and gravity.</summary>
    [Export] public float JumpHeight { get; set; } = 0.55f;
    /// <summary>A jump pressed this long before landing still happens on landing (s).</summary>
    [Export] public float JumpBufferTime { get; set; } = 0.15f;
    /// <summary>With no stamina left, a jump reaches this fraction of full height.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float JumpHeightWithoutStamina { get; set; } = 0.4f;

    [ExportGroup("Jump spam penalty")]
    /// <summary>Jump fatigue (0..1) added by each jump. Fatigue makes jumps lower and landings harsher.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float JumpFatiguePerJump { get; set; } = 0.4f;
    /// <summary>Jump fatigue lost per second while on the ground.</summary>
    [Export] public float JumpFatigueRecoveryPerSecond { get; set; } = 0.4f;
    /// <summary>Jump height at full fatigue, as a fraction of normal.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float JumpHeightAtFullFatigue { get; set; } = 0.6f;
    /// <summary>Horizontal speed kept on landing: rested → fully fatigued.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float LandingSpeedKeptRested { get; set; } = 0.85f;
    [Export(PropertyHint.Range, "0,1,0.05")] public float LandingSpeedKeptFatigued { get; set; } = 0.3f;
    /// <summary>After landing, acceleration is reduced for this long: rested → fully fatigued (s).</summary>
    [Export] public float LandingRecoveryRested { get; set; } = 0.2f;
    [Export] public float LandingRecoveryFatigued { get; set; } = 0.8f;
    /// <summary>Acceleration multiplier while recovering from a landing.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float LandingRecoveryAcceleration { get; set; } = 0.35f;
    /// <summary>Gravity for characters, relative to real gravity. Above 1 so jumps and falls feel heavy, not floaty.</summary>
    [Export(PropertyHint.Range, "1,3,0.1")] public float GravityMultiplier { get; set; } = 1.6f;

    [ExportGroup("Sprint rules")]
    /// <summary>Forward stick/keys needed to start a sprint (0..1).</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float SprintStartForward { get; set; } = 0.5f;
    /// <summary>A toggled sprint cancels when forward input drops below this.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float SprintCancelForward { get; set; } = 0.3f;

    [ExportGroup("Gamepad")]
    /// <summary>Stick tilt that reaches walking speed. Full tilt = run.</summary>
    [Export(PropertyHint.Range, "0.1,0.95,0.05")] public float GamepadWalkTilt { get; set; } = 0.6f;

    [ExportGroup("Lean")]
    /// <summary>How far the head moves sideways at full lean (m).</summary>
    [Export] public float LeanOffset { get; set; } = 0.35f;
    /// <summary>Full leans per second (1 / time to reach full lean).</summary>
    [Export] public float LeanSpeed { get; set; } = 6f;
    /// <summary>Size of the head when checking whether a lean fits next to a wall.</summary>
    [Export] public float LeanHeadRadius { get; set; } = 0.15f;

    [ExportGroup("Cover")]
    /// <summary>Height you rise to when peeking over low cover from a crouch (m).</summary>
    [Export] public float PeekHeight { get; set; } = 1.55f;
    /// <summary>How close cover must be in front of you for auto-lean / peeking (m).</summary>
    [Export] public float CoverCheckDistance { get; set; } = 0.9f;

    [ExportGroup("Stances (m)")]
    [Export] public float Radius { get; set; } = 0.3f;
    [Export] public float StandHeight { get; set; } = 1.8f;
    [Export] public float CrouchHeight { get; set; } = 1.2f;
    [Export] public float ProneHeight { get; set; } = 0.6f;
    [Export] public float EyeOffsetFromTop { get; set; } = 0.15f;
    /// <summary>Height change speed between stand and crouch (m/s).</summary>
    [Export] public float CrouchTransitionSpeed { get; set; } = 3.5f;
    /// <summary>Height change speed into/out of prone (m/s). Slower: getting down takes effort.</summary>
    [Export] public float ProneTransitionSpeed { get; set; } = 1.6f;
}
