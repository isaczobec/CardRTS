using System;

/// <summary>
/// Global C# events that cross scene/system boundaries.
/// Subscribe with += and always unsubscribe with -= (OnDisable / OnDestroy).
/// </summary>
public static class GameEvents
{
    /// <summary>
    /// Fired once when the game is starting — after the world has been generated
    /// and rendered but before the first simulation tick. Raised on both host and
    /// pure clients via TickManager.StartGame().
    /// </summary>
    public static event Action OnGameStarting;

    public static void FireGameStarting() => OnGameStarting?.Invoke();
}
