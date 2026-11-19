using Godot;

namespace ZombieSandbox.Combat;

/// <summary>
/// A split-second freeze on a solid hit (the "weight" of melee): slows the whole game for a few
/// real-time milliseconds, then restores it. The restore is checked against the real clock every
/// rendered frame, so it can't get stuck in slow motion.
/// </summary>
public static partial class HitStop
{
    private static ulong _endMs;
    private static Ticker? _ticker;

    public static void Freeze(Node any, float seconds, float timeScale = 0.06f)
    {
        if (seconds <= 0f)
            return;
        ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        if (end > _endMs)
            _endMs = end;
        Engine.TimeScale = timeScale;
        if (_ticker == null || !GodotObject.IsInstanceValid(_ticker))
        {
            _ticker = new Ticker();
            any.GetTree().Root.CallDeferred(Node.MethodName.AddChild, _ticker);
        }
    }

    /// <summary>Restores normal speed once the freeze is over (runs every frame, even while paused or slowed).</summary>
    private sealed partial class Ticker : Node
    {
        public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

        public override void _Process(double delta)
        {
            if (Engine.TimeScale < 1.0 && Time.GetTicksMsec() >= _endMs)
                Engine.TimeScale = 1.0;
        }
    }
}
