#if SELFTEST
using System;
using System.IO;
using BepInEx;
using Nivalis;
using UnityEngine;

namespace NivalisToolBelt;

/// <summary>
/// Scripted in-game test, compiled only with -p:SelfTest=true and active only while
/// BepInEx/config/toolbelt-selftest.txt exists (its content is the save to load).
/// Writes one status line per second to BepInEx/toolbelt-selftest.log so an external
/// script can press the real hotkeys and check the effect. F9 = screenshot, F10 = speed x3 / x1,
/// F11 = save to the toolbelt_test slot, F12 = load it.
/// </summary>
internal static class SelfTest
{
    private static readonly string FlagPath = Path.Combine(Paths.ConfigPath, "toolbelt-selftest.txt");
    private static readonly string LogPath = Path.Combine(Paths.BepInExRootPath, "toolbelt-selftest.log");

    private static bool? _active;
    private static string _saveName;
    private static float _readySince = -1f;
    private static bool _loadRequested;
    private static float _nextStatus;
    private static int _shots;
    private const string TestSlot = "toolbelt_test";

    public static void Tick(ToolBeltBehaviour menu)
    {
        if (_active == null)
        {
            _active = File.Exists(FlagPath);
            if (_active == true)
            {
                _saveName = File.ReadAllText(FlagPath).Trim();
                File.WriteAllText(LogPath, $"selftest start, save '{_saveName}'\n");
            }
        }
        if (_active != true) return;

        float now = Time.realtimeSinceStartup;

        if (!_loadRequested && _saveName.Length > 0 && Singleton<SerializationManager>.InstanceExist()
            && Singleton<GameSceneManager>.InstanceExist() && !Singleton<GameSceneManager>.Instance.IsLoading)
        {
            if (_readySince < 0f) _readySince = now;
            if (now - _readySince > 15f)
            {
                _loadRequested = true;
                Write($"loading save, exists={Singleton<SerializationManager>.Instance.DoesSaveExist(_saveName)}");
                Singleton<SerializationManager>.Instance.Load(_saveName);
            }
        }

        var input = UnityInput.Current;
        if (input.GetKeyDown(KeyCode.F9))
        {
            string shot = Path.Combine(Paths.BepInExRootPath, $"toolbelt-shot-{++_shots}.png");
            ScreenCapture.CaptureScreenshot(shot);
            Write($"screenshot {shot}");
        }
        if (input.GetKeyDown(KeyCode.F10))
        {
            Sandbox.SpeedMultiplier = Sandbox.SpeedMultiplier == 1f ? 3f : 1f;
            Write($"speed multiplier -> {Sandbox.SpeedMultiplier}");
        }

        if (input.GetKeyDown(KeyCode.F11))
        {
            Write($"saving {TestSlot}: {Singleton<SerializationManager>.Instance.Save(TestSlot, false)}");
        }
        if (input.GetKeyDown(KeyCode.F12))
        {
            Write($"loading {TestSlot}");
            Singleton<SerializationManager>.Instance.Load(TestSlot);
        }

        if (now < _nextStatus) return;
        _nextStatus = now + 1f;
        try
        {
            var c = Sandbox.Controller;
            string player = c == null
                ? "player=none"
                : $"state={c.State} pos={c.transform.position} yaw={c.Rotation.eulerAngles.y:0} camYaw={c.Camera.transform.eulerAngles.y:0} vel={c.Velocity} move={c.defaultMoveSpeed} grounded={c.isGrounded} rayGround={c.RaycastCheckGround(c.transform.position)}";
            string clock = "clock=none";
            if (Clock.Available)
            {
                var t = Singleton<TimeOfDayManager>.Instance;
                clock = $"clock='{Clock.Text}' secs={TimeOfDayManager.TotalGameSeconds} daySecs={TimeOfDayManager.TotalDaySeconds} frozen={Clock.Frozen} tick={t.timeTickMultiplier} devMult={t.Dev_CurrentGameTimeMultiplier()} devScale={t.Dev_GetTimeScale()} override={t.TimeScaleOverride} unityScale={Time.timeScale} curfew={Clock.InCurfew} curfewHours={CurfewManager.CURFEW_WARNING_START_TIME_HOUR}/{CurfewManager.CURFEW_START_TIME_HOUR}/{CurfewManager.CURFEW_END_TIME_HOUR}";
            }
            string area = Singleton<GameSceneManager>.InstanceExist() ? $"scene='{Singleton<GameSceneManager>.Instance.CurrentGameplaySceneName}' area='{Teleports.AreaName}' spots={Teleports.Listed.Count} pending={Teleports.Pending?.Name} loading={Singleton<GameSceneManager>.Instance.IsLoading} canTravel={(Singleton<TravelManager>.InstanceExist() ? Singleton<TravelManager>.Instance.CanTravel.ToString() : "?")} canMove={(c == null ? "?" : c.CanMove.ToString())} money={(c == null ? 0 : Singleton<PlayerManager>.Instance.LocalPlayer.Inventory.Money)}" : "";
            Write($"t={now:0} menu={menu.IsOpen} cursor={CursorModeManager.IsCursorActive} {player} {clock} {area}");
        }
        catch (Exception e)
        {
            Write($"status failed: {e}");
        }
    }

    private static void Write(string line) => File.AppendAllText(LogPath, line + "\n");
}
#endif
