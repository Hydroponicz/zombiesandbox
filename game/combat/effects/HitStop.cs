using Godot;

namespace ZombieSandbox.Combat;

/// <summary>
/// A split-second freeze on a solid hit (the "weight" of melee). Slows the whole game for a few
/// real-time milliseconds, then restores it.
/// </summary>
public static class HitStop
{
    private static ulong _endMs;

    public static void Freeze(Node any, float seconds, float timeScale = 0.06f)
    {
        if (seconds <= 0f)
            return;
        ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        if (end <= _endMs && Engine.TimeScale <= timeScale)
            return;
        _endMs = end > _endMs ? end : _endMs;
        Engine.TimeScale = timeScale;
        any.GetTree().CreateTimer(seconds, processAlways: true, processInPhysics: false, ignoreTimeScale: true).Timeout += () =>
        {
            if (Time.GetTicksMsec() + 5 >= _endMs)
                Engine.TimeScale = 1.0;
        };
    }
}
