using System.Text;
using Godot;
using ZombieSandbox.Health;

namespace ZombieSandbox.Combat;

/// <summary>
/// Practice target with real body-part hitboxes and health. Falls over when killed, stands back up
/// healed after a few seconds. The overhead readout (developer builds) shows exact HP per part.
/// </summary>
public partial class TrainingDummy : Node3D
{
    [Export] public float RespawnSeconds { get; set; } = 4f;

    private BodyHealth _health = null!;
    private Node3D _visual = null!;
    private Label3D _readout = null!;
    private float _deadTime;
    private readonly StringBuilder _text = new();

    public override void _Ready()
    {
        _health = GetNode<BodyHealth>("Health");
        _visual = GetNode<Node3D>("Visual");
        _readout = GetNode<Label3D>("Readout");
        _readout.Visible = OS.IsDebugBuild();
    }

    public override void _Process(double delta)
    {
        if (_health.IsDead)
        {
            _deadTime += (float)delta;
            _visual.Rotation = new Vector3(Mathf.DegToRad(-80f) * Mathf.Min(_deadTime * 3f, 1f), 0f, 0f);
            if (_deadTime >= RespawnSeconds)
            {
                _health.HealAll();
                _visual.Rotation = Vector3.Zero;
                _deadTime = 0f;
            }
        }

        if (_readout.Visible)
        {
            _text.Clear();
            foreach (BodyPart part in BodyParts.All)
            {
                _text.Append(BodyParts.Name(part)).Append(' ').Append(Mathf.Ceil(_health.Hp(part)).ToString("0"));
                if (_health.Bleeding(part) != BleedLevel.None)
                    _text.Append(_health.Bleeding(part) == BleedLevel.Heavy ? " BLEED!!" : " bleed");
                if (_health.IsFractured(part))
                    _text.Append(" BROKEN");
                _text.Append('\n');
            }
            if (_health.IsDead)
                _text.Append("DEAD");
            _readout.Text = _text.ToString();
        }
    }
}
