using Godot;

namespace ZombieSandbox.Combat;

/// <summary>Placeholder impact visuals (blood, dust) until real effects: a one-shot particle burst.</summary>
public static class ImpactEffects
{
    private static StandardMaterial3D? _blood;
    private static StandardMaterial3D? _dust;

    public static void Blood(Node parent, Vector3 position, Vector3 direction, float amount = 1f) =>
        Burst(parent, position, direction, _blood ??= Material(new Color(0.45f, 0.02f, 0.02f)), (int)(18 * amount), 2.6f);

    public static void Dust(Node parent, Vector3 position, Vector3 direction) =>
        Burst(parent, position, direction, _dust ??= Material(new Color(0.55f, 0.53f, 0.5f)), 10, 1.6f);

    private static StandardMaterial3D Material(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = false,
    };

    private static void Burst(Node parent, Vector3 position, Vector3 direction, Material material, int amount, float speed)
    {
        var particles = new CpuParticles3D
        {
            OneShot = true,
            Emitting = false,
            Amount = Mathf.Max(amount, 1),
            Lifetime = 0.6f,
            Explosiveness = 0.95f,
            Mesh = new BoxMesh { Size = new Vector3(0.025f, 0.025f, 0.025f), Material = material },
            Direction = direction.LengthSquared() > 0.001f ? direction.Normalized() : Vector3.Up,
            Spread = 35f,
            InitialVelocityMin = speed * 0.5f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0f, -9.8f, 0f),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.4f,
        };
        parent.AddChild(particles);
        particles.GlobalPosition = position;
        particles.Emitting = true;
        particles.GetTree().CreateTimer(1.2).Timeout += particles.QueueFree;
    }
}
