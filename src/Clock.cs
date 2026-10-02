using Nivalis;
using UnityEngine;

namespace NivalisToolBelt;

/// <summary>In-game clock tools, built on the game's <see cref="TimeOfDayManager"/>.</summary>
internal static class Clock
{
    private const int SecondsInHour = 3600;
    private const int SecondsInDay = 24 * SecondsInHour;

    // The game's own tick rate, captured per manager so the multiplier can be undone.
    private static TimeOfDayManager _speedOwner;
    private static float _baseTickMultiplier;
    private static float _speedMultiplier = 1f;

    private static TimeOfDayManager Manager =>
        Singleton<TimeOfDayManager>.InstanceExist() ? Singleton<TimeOfDayManager>.Instance : null;

    public static bool Available => Manager != null;

    public static string Text => $"Day {TimeOfDayManager.GameplayGameDay}, {TimeOfDayManager.ClockHour:00}:{TimeOfDayManager.ClockMinute:00}";

    public static bool Frozen
    {
        get
        {
            var manager = Manager;
            return manager != null && manager.IsPaused;
        }
        set
        {
            var manager = Manager;
            if (manager == null || manager.IsPaused == value) return;
            if (value) manager.Dev_Pause();
            else manager.Dev_UnPause();
        }
    }

    /// <summary>How fast the clock runs compared to the game's normal pace. Does not change game speed.</summary>
    public static float SpeedMultiplier
    {
        get => _speedMultiplier;
        set
        {
            _speedMultiplier = Mathf.Max(0.01f, value);
            ApplySpeed();
        }
    }

    public static void Tick()
    {
        // The manager is recreated on load and comes back with the game's tick rate.
        if (_speedMultiplier != 1f && Manager != _speedOwner) ApplySpeed();
    }

    private static void ApplySpeed()
    {
        var manager = Manager;
        if (manager == null) return;
        if (manager != _speedOwner)
        {
            _speedOwner = manager;
            _baseTickMultiplier = manager.timeTickMultiplier;
        }
        manager.timeTickMultiplier = _baseTickMultiplier * _speedMultiplier;
    }

    /// <summary>True during the game's curfew: the day is over and the clock stands still until the player sleeps.</summary>
    public static bool InCurfew => Singleton<CurfewManager>.InstanceExist() && CurfewManager.IsCurfewInEffect;

    public static int CurfewHour => CurfewManager.CURFEW_START_TIME_HOUR;

    public static void SkipHours(int hours) => Skip(hours * SecondsInHour);

    /// <summary>Advances the clock to the next time it shows <paramref name="hour"/>:00. Never goes back.</summary>
    public static void SkipTo(int hour) => Skip(SecondsUntil(hour));

    /// <summary>Whether <see cref="SkipTo"/> for this hour would be cut short by the curfew.</summary>
    public static bool CurfewComesBefore(int hour) => SecondsUntil(hour) > SecondsUntil(CurfewHour);

    private static int SecondsUntil(int hour)
    {
        int delta = hour * SecondsInHour - TimeOfDayManager.TotalDaySeconds;
        return delta > 0 ? delta : delta + SecondsInDay;
    }

    // The curfew ends the game day (summary screen, then the clock stops until the player sleeps).
    // Jumping past its start still triggers all that but leaves the clock hours ahead of it,
    // so a skip stops at the curfew and lets the game take over from there.
    private static void Skip(int seconds)
    {
        var manager = Manager;
        if (manager == null || InCurfew) return;
        manager.AddTime(Mathf.Min(seconds, SecondsUntil(CurfewHour)));
    }
}
