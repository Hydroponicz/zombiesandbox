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

        string view = rig.IsViewFirstPerson ? "First person" : (rig.IsRightShoulder ? "Third person (right)" : "Third person (left)");
        string stance = _player.Stance == _player.WantedStance
            ? _player.Stance.ToString()
            : $"{_player.Stance} (wants {_player.WantedStance}, blocked)";

        _text.Clear();
        _text.AppendLine($"Device: {GameInput.Instance.LastDevice}    View: {view}");
        _text.AppendLine($"Stance: {stance}    Speed: {_player.HorizontalSpeed:0.00} m/s    Sprinting: {(_player.IsSprinting ? "YES" : "no")}    Lean: {_player.Lean:+0.00;-0.00;0}");
        _text.AppendLine($"Stamina: {stamina.Current:0}/{stamina.Max:0}{(stamina.IsExhausted ? "   EXHAUSTED" : "")}    Jump fatigue: {_player.JumpFatigue:0.00}");
        _text.AppendLine(skills.IsMaxLevel(SkillId.Fitness)
            ? $"Fitness: Lv {skills.GetLevel(SkillId.Fitness)} (max)"
            : $"Fitness: Lv {skills.GetLevel(SkillId.Fitness)}   XP {skills.GetXp(SkillId.Fitness):0.0} / {skills.XpForNextLevel(SkillId.Fitness):0}");
        _text.AppendLine($"Aiming: Lv {skills.GetLevel(SkillId.Aiming)}    Strength: Lv {skills.GetLevel(SkillId.Strength)}    Reloading: Lv {skills.GetLevel(SkillId.Reloading)}    Maintenance: Lv {skills.GetLevel(SkillId.Maintenance)}");
        Combat.WeaponHolder w = _player.Weapons;
        string breath = w.IsGasping ? "GASPING" : (w.IsHoldingBreath ? "HOLDING" : "normal");
        _text.AppendLine($"Weapon: ADS {w.AdsBlend:0.00}{(w.IsAiming ? " (aiming)" : "")}    Can fire: {(w.CanFire ? "yes" : "NO")}    Sway: {w.SwayDegrees:0.00}°    Breath: {breath} {w.BreathFraction:P0}    Wall: {w.WallPullBack:0.00} m{(_player.IsPeeking ? "    PEEKING" : "")}");
        string mag = w.Magazine != null ? $"{w.Magazine.Rounds}/{w.Magazine.Capacity}" : "none";
        var pouch = new System.Text.StringBuilder();
        foreach (Combat.MagazineItem m in _player.Pouch.Magazines)
            pouch.Append(m.Rounds).Append(' ');
        string busy = w.Reloading != Combat.ReloadKind.None ? $"    RELOADING ({w.Reloading}) {w.ReloadProgress:P0}" : (w.IsCheckingMagazine ? "    CHECKING MAG" : "");
        _text.AppendLine($"Ammo (dev only): mag {mag}{(w.Chambered ? " +1" : " (chamber empty)")}    pouch [ {pouch}]{busy}");
        string jam = w.IsJammed ? $"JAMMED: {w.Malfunction}{(w.MalfunctionKnown ? " (known)" : " (unknown)")}" : "no jam";
        string action = w.IsHandlingMalfunction ? $"    {w.MalfunctionActionName} {w.MalfunctionActionProgress:P0}" : "";
        _text.AppendLine($"Condition (dev only): {w.Weapon?.Condition:0.00}%    Heat: {w.Heat:0.0}    {jam}{action}");
        var hp = new System.Text.StringBuilder();
        ZombieSandbox.Health.BodyHealth h = _player.Health;
        foreach (ZombieSandbox.Health.BodyPart part in ZombieSandbox.Health.BodyParts.All)
        {
            string shortName = part switch
            {
                ZombieSandbox.Health.BodyPart.LeftArm => "LA",
                ZombieSandbox.Health.BodyPart.RightArm => "RA",
                ZombieSandbox.Health.BodyPart.LeftLeg => "LL",
                ZombieSandbox.Health.BodyPart.RightLeg => "RL",
                _ => part.ToString()[..1],
            };
            hp.Append(shortName).Append(' ').Append(Mathf.Ceil(h.Hp(part)).ToString("0"));
            if (h.Bleeding(part) != ZombieSandbox.Health.BleedLevel.None) hp.Append(h.Bleeding(part) == ZombieSandbox.Health.BleedLevel.Heavy ? "!!" : "!");
            if (h.IsFractured(part)) hp.Append('X');
            hp.Append("  ");
        }
        _text.AppendLine($"Health (dev only): {hp}   pain {h.Pain:0.00}   danger {h.Danger:0.00}   Medical Lv {skills.GetLevel(SkillId.Medical)}{(h.IsDead ? "   DEAD" : "")}");
        Combat.Ballistics ballistics = Combat.Ballistics.Instance;
        string assist = rig.AimAssistTarget != null ? "    Aim assist: ON TARGET" : "";
        _text.AppendLine($"Fire mode: {w.FireMode}    Shots: {w.ShotsFired}    In flight: {ballistics.BulletsInFlight}    Trails: {(ballistics.ShowTrails ? "on" : "off")}    Recoil: {w.RecoilDegrees.X:+0.00;-0.00;0.00}° up {w.RecoilDegrees.Y:+0.00;-0.00;0.00}° side (burst {w.BurstShots}){assist}");
        if (ballistics.LastImpact is { } hit)
            _text.AppendLine($"Last hit: {hit.Distance:0.0} m   {hit.Speed:0} m/s   {hit.FlightTime * 1000f:0} ms   {Mathf.Round(hit.DropFromSightLine * 1000f) / 10f:+0.0;-0.0;0.0} cm vs sight line{(hit.HitTarget ? "   TARGET" : "")}");
        _text.AppendLine();
        _text.AppendLine("[DEV] Ctrl+1/2 = Fitness -/+   Ctrl+3/4 = Aiming -/+   Ctrl+5/6 = Strength -/+   Ctrl+7/8 = Reloading -/+   Ctrl+9/0 = Maintenance -/+   Ctrl+K = condition   Ctrl+J = jam next shot   Ctrl+R = refill ammo   Ctrl+H = hurt self   Ctrl+N = break leg   Ctrl+G = heal   Ctrl+-/= = Medical -/+   H = health screen   Ctrl+T = bullet trails    Esc = release mouse, click = recapture");
        _info.Text = _text.ToString();
    }
}
