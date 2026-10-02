using Nivalis;
using UnityEngine;
using ControllerState = Nivalis.PlayerCharacterController.ControllerState;

namespace NivalisToolBelt;

/// <summary>
/// Sandbox features. Everything here drives hooks the game already ships
/// (its own no-clip controller state and time manager), nothing is simulated.
/// </summary>
internal static class Sandbox
{
    private const int SecondsInHour = 3600;

    // The game's own speeds, captured per controller so the multiplier can be undone.
    private static PlayerCharacterController _speedOwner;
    private static float _baseMoveSpeed;
    private static float _baseSprintSpeed;
    private static float _speedMultiplier = 1f;

    private const float FallRescueSpeed = 30f;
    private static PlayerCharacterController _safeOwner;
    private static Vector3 _safePosition;
    private static Quaternion _safeRotation;
    private static bool _hasSafeSpot;

    /// <summary>The player's controller, or null while no save is loaded (title screen, loading).</summary>
    public static PlayerCharacterController Controller
    {
        get
        {
            // Singleton<T>.Instance may create the manager, so ask first.
            if (!Singleton<PlayerManager>.InstanceExist()) return null;
            var player = Singleton<PlayerManager>.Instance.LocalPlayer;
            if (player == null) return null;
            var character = player.Character;
            return character == null ? null : character.Controller;
        }
    }

    public static bool InGame => Controller != null;

    public static bool Fly
    {
        get
        {
            var controller = Controller;
            return controller != null && controller.State == ControllerState.NoClip;
        }
        set
        {
            var controller = Controller;
            if (controller == null || Fly == value) return;
            controller.ToggleNoClip();
            if (Fly != value) controller.State = value ? ControllerState.NoClip : ControllerState.Normal;
            Plugin.Logger.LogInfo($"Fly / ghost mode {(Fly ? "on" : "off")}");
            if (!Fly && _hasSafeSpot && controller == _safeOwner && !controller.RaycastCheckGround(controller.transform.position))
                ReturnToSafeSpot(controller);
        }
    }

    public static float SpeedMultiplier
    {
        get => _speedMultiplier;
        set
        {
            _speedMultiplier = Mathf.Max(0.1f, value);
            ApplySpeed();
        }
    }

    public static void Tick()
    {
        var controller = Controller;
        if (controller == null) return;
        // The player object is recreated on load / area change and comes back with the game's speeds.
        if (_speedMultiplier != 1f && controller != _speedOwner) ApplySpeed();
        TrackSafeSpot(controller);
    }

    // Outside no-clip the game keeps the player on walkable ground, so nothing catches a fall:
    // leaving fly mode over water or inside geometry drops the player out of the world for good.
    // Remember the last spot the player stood on and put them back there when fly mode ends with
    // nothing underneath, or (as a net for anything that check misses) once they are in free fall.
    private static void TrackSafeSpot(PlayerCharacterController controller)
    {
        if (controller != _safeOwner)
        {
            _safeOwner = controller;
            _hasSafeSpot = false;
        }
        if (controller.State != ControllerState.Normal) return;

        if (controller.isGrounded)
        {
            _safePosition = controller.transform.position;
            _safeRotation = controller.Rotation;
            _hasSafeSpot = true;
        }
        else if (_hasSafeSpot && controller.Velocity.y < -FallRescueSpeed)
        {
            ReturnToSafeSpot(controller);
        }
    }

    private static void ReturnToSafeSpot(PlayerCharacterController controller)
    {
        controller.StopMovement();
        controller.TeleportPlayer(_safePosition, _safeRotation, true);
        Plugin.Logger.LogInfo($"No ground below, returned the player to {_safePosition}");
    }

    private static void ApplySpeed()
    {
        var controller = Controller;
        if (controller == null) return;
        if (controller != _speedOwner)
        {
            _speedOwner = controller;
            _baseMoveSpeed = controller.defaultMoveSpeed;
            _baseSprintSpeed = controller.sprintSpeed;
        }
        controller.defaultMoveSpeed = _baseMoveSpeed * _speedMultiplier;
        controller.sprintSpeed = _baseSprintSpeed * _speedMultiplier;
    }

    private static TimeOfDayManager Time =>
        Singleton<TimeOfDayManager>.InstanceExist() ? Singleton<TimeOfDayManager>.Instance : null;

    public static bool HasClock => Time != null;

    public static string ClockText => $"Day {TimeOfDayManager.GameplayGameDay}, {TimeOfDayManager.ClockHour:00}:{TimeOfDayManager.ClockMinute:00}";

    public static bool ClockFrozen
    {
        get
        {
            var time = Time;
            return time != null && time.IsPaused;
        }
        set
        {
            var time = Time;
            if (time == null || time.IsPaused == value) return;
            if (value) time.Dev_Pause();
            else time.Dev_UnPause();
        }
    }

    public static void SkipHours(int hours)
    {
        Time?.AddTime(hours * SecondsInHour);
    }
}
