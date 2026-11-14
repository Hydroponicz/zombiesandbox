using System.Text;
using Godot;
using ZombieSandbox.Character;
using ZombieSandbox.Core.Input;
using ZombieSandbox.Player;

namespace ZombieSandbox.Debug;

/// <summary>Developer overlay for testing movement. Only shown in debug builds.</summary>
public partial class DebugHud : CanvasLayer
{
    private Label _info = null!;
    private PlayerController? _player;
    private readonly StringBuilder _text = new();

    public override void _Ready()
    {
        _info = GetNode<Label>("Info");
        Visible = OS.IsDebugBuild();
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;

        _player ??= GetTree().GetFirstNodeInGroup(PlayerController.Group) as PlayerController;
        if (_player == null)
            return;

        CharacterSkills skills = _player.Skills;
        Stamina stamina = _player.Stamina;
        CameraRig rig = _player.CameraRig;

        string view = rig.IsFirstPerson ? "First person" : (rig.IsRightShoulder ? "Third person (right)" : "Third person (left)");
        string stance = _player.Stance == _player.WantedStance
            ? _player.Stance.ToString()
            : $"{_player.Stance} (wants {_player.WantedStance}, blocked)";

        _text.Clear();
        _text.AppendLine($"Device: {GameInput.Instance.LastDevice}    View: {view}");
        _text.AppendLine($"Stance: {stance}    Speed: {_player.HorizontalSpeed:0.00} m/s    Sprinting: {(_player.IsSprinting ? "YES" : "no")}    Lean: {_player.Lean:+0.00;-0.00;0}");
        _text.AppendLine($"Stamina: {stamina.Current:0}/{stamina.Max:0}{(stamina.IsExhausted ? "   EXHAUSTED" : "")}");
        _text.AppendLine(skills.IsMaxLevel(SkillId.Fitness)
            ? $"Fitness: Lv {skills.GetLevel(SkillId.Fitness)} (max)"
            : $"Fitness: Lv {skills.GetLevel(SkillId.Fitness)}   XP {skills.GetXp(SkillId.Fitness):0.0} / {skills.XpForNextLevel(SkillId.Fitness):0}");
        _text.AppendLine();
        _text.AppendLine("[DEV] F3/F4 = Fitness +/-    Esc = release mouse, click = recapture");
        _info.Text = _text.ToString();
    }
}
