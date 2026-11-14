using Godot;
using Godot.Collections;
using ZombieSandbox.Combat;
using ZombieSandbox.Player;

namespace ZombieSandbox.UI;

/// <summary>
/// Over-the-shoulder aiming has no sights to look through, so it shows a small dot where the
/// barrel actually points (sway, obstacles and all). Hidden at all other times: there is no crosshair.
/// </summary>
public partial class AimDot : Control
{
    private const float MaxRange = 500f;

    [Export] public float Radius { get; set; } = 2.5f;
    [Export] public Color DotColor { get; set; } = new(1f, 1f, 1f, 0.9f);

    private PlayerController _player = null!;
    private readonly PhysicsRayQueryParameters3D _ray = new();
    private Vector2 _screenPosition;
    private bool _visible;

    public override void _Ready()
    {
        _player = (PlayerController)Owner;
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessPriority = 20; // After the camera and weapon have moved this frame.
    }

    public override void _Process(double delta)
    {
        bool wasVisible = _visible;
        _visible = false;

        WeaponHolder weapons = _player.Weapons;
        CameraRig rig = _player.CameraRig;
        if (rig.ShowAimDot && weapons.Weapon != null)
        {
            Transform3D muzzle = weapons.Weapon.Muzzle.GlobalTransform;
            _ray.From = muzzle.Origin;
            _ray.To = muzzle.Origin - muzzle.Basis.Z.Normalized() * MaxRange;
            _ray.CollisionMask = _player.CollisionMask;
            _ray.Exclude ??= new Array<Rid> { _player.GetRid() };
            Dictionary hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(_ray);
            Vector3 point = hit.Count > 0 ? hit["position"].AsVector3() : _ray.To;

            Camera3D camera = rig.Camera;
            if (!camera.IsPositionBehind(point))
            {
                _screenPosition = camera.UnprojectPosition(point);
                _visible = true;
            }
        }

        if (_visible || wasVisible)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_visible)
            return;
        DrawCircle(_screenPosition, Radius + 1f, new Color(0f, 0f, 0f, 0.6f));
        DrawCircle(_screenPosition, Radius, DotColor);
    }
}
