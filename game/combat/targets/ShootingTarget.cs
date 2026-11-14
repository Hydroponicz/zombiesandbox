using Godot;

namespace ZombieSandbox.Combat;

/// <summary>Practice target. Optionally slides side to side to test tracking and aim assist.</summary>
public partial class ShootingTarget : AnimatableBody3D, IHittable
{
    /// <summary>Total side-to-side travel (m). 0 = stationary.</summary>
    [Export] public float MoveDistance { get; set; }
    [Export] public float MoveSpeed { get; set; } = 2f;

    public int Hits { get; private set; }

    private Vector3 _start;
    private float _time;

    public override void _Ready() => _start = GlobalPosition;

    public override void _PhysicsProcess(double delta)
    {
        if (MoveDistance <= 0f)
            return;
        _time += (float)delta;
        float offset = Mathf.PingPong(_time * MoveSpeed, MoveDistance) - MoveDistance * 0.5f;
        GlobalPosition = _start + GlobalBasis.X * offset;
    }

    public bool OnBulletHit(in BulletHit hit)
    {
        Hits++;
        return true;
    }
}
