using Godot;

namespace ZombieSandbox.World;

/// <summary>
/// Drives a level's sun and sky from the <see cref="WorldClock"/>: the sun arcs across the sky
/// during daylight, and light fades to a dim moonlight at night.
/// </summary>
public partial class DayNightCycle : Node
{
    [Export] public NodePath SunPath { get; set; } = "../Sun";
    [Export] public NodePath EnvironmentPath { get; set; } = "../WorldEnvironment";
    [Export] public float DaySunEnergy { get; set; } = 1f;
    [Export] public float NightLightEnergy { get; set; } = 0.06f;
    [Export] public float DayAmbient { get; set; } = 1f;
    [Export] public float NightAmbient { get; set; } = 0.05f;

    private DirectionalLight3D _sun = null!;
    private Environment? _environment;
    private ProceduralSkyMaterial? _sky;

    public override void _Ready()
    {
        _sun = GetNode<DirectionalLight3D>(SunPath);
        _environment = GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment;
        _sky = _environment?.Sky?.SkyMaterial as ProceduralSkyMaterial;
    }

    public override void _Process(double delta)
    {
        WorldClock clock = WorldClock.Instance;
        float daylight = clock.Daylight;
        float hour = clock.Hour;

        // Sun elevation: rises at sunrise, highest midway, sets at sunset. At night the light
        // stays above the horizon as dim "moonlight" so the world isn't pitch black.
        float dayProgress = Mathf.InverseLerp(WorldClock.Sunrise, WorldClock.Sunset, hour);
        float elevation = Mathf.Lerp(8f, 70f, Mathf.Sin(Mathf.Clamp(dayProgress, 0f, 1f) * Mathf.Pi));
        float azimuth = Mathf.Lerp(-100f, 100f, Mathf.Clamp(dayProgress, 0f, 1f));
        if (daylight <= 0.01f)
        {
            elevation = 35f;
            azimuth = 160f;
        }
        _sun.Rotation = new Vector3(Mathf.DegToRad(-elevation), Mathf.DegToRad(azimuth), 0f);
        _sun.LightEnergy = Mathf.Lerp(NightLightEnergy, DaySunEnergy, daylight);
        _sun.LightColor = new Color(1f, 1f, 1f).Lerp(new Color(0.55f, 0.65f, 1f), 1f - daylight);

        if (_environment != null)
        {
            _environment.AmbientLightEnergy = Mathf.Lerp(NightAmbient, DayAmbient, daylight);
            _environment.BackgroundEnergyMultiplier = Mathf.Lerp(0.04f, 1f, daylight);
        }
        if (_sky != null)
            _sky.EnergyMultiplier = Mathf.Lerp(0.05f, 1f, daylight);
    }
}
