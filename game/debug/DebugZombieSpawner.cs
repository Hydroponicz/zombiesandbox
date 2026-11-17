using Godot;
using ZombieSandbox.Core.Input;
using ZombieSandbox.Player;
using ZombieSandbox.World;

namespace ZombieSandbox.Debug;

/// <summary>Developer tools for AI testing: spawn zombies, fast-forward the clock.</summary>
public partial class DebugZombieSpawner : Node
{
    [Export] public PackedScene ZombieScene { get; set; } = null!;
    [Export] public float SpawnDistance { get; set; } = 20f;

    private static readonly double[] TimeScales = { 1, 60, 600 };
    private int _timeScaleIndex;

    public override void _UnhandledInput(InputEvent e)
    {
        if (!OS.IsDebugBuild())
            return;
        if (e.IsActionPressed(InputActions.DebugSpawnZombie))
            SpawnInFrontOfPlayer();
        else if (e.IsActionPressed(InputActions.DebugTimeScale))
        {
            _timeScaleIndex = (_timeScaleIndex + 1) % TimeScales.Length;
            WorldClock.Instance.TimeScale = TimeScales[_timeScaleIndex];
        }
        else if (e.IsActionPressed(InputActions.DebugSkipHours))
            WorldClock.Instance.AdvanceGameHours(3);
        else if (e.IsActionPressed(InputActions.DebugIgnorePlayer))
            ZombieSandbox.AI.Survivor.DebugIgnorePlayer = !ZombieSandbox.AI.Survivor.DebugIgnorePlayer;
    }

    private void SpawnInFrontOfPlayer()
    {
        if (GetTree().GetFirstNodeInGroup(PlayerController.Group) is not PlayerController player)
            return;
        Vector3 forward = -player.GlobalBasis.Z;
        Vector3 wanted = player.GlobalPosition + forward * SpawnDistance;
        Vector3 point = NavigationServer3D.MapGetClosestPoint(player.GetWorld3D().NavigationMap, wanted);
        var zombie = ZombieScene.Instantiate<Node3D>();
        GetTree().CurrentScene.AddChild(zombie);
        zombie.GlobalPosition = point + Vector3.Up * 0.05f;
        zombie.Rotation = new Vector3(0f, player.Rotation.Y, 0f); // Facing away from the player.
    }
}
