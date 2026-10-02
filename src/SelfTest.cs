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
/// script can press the real hotkeys and check the effect. F9 = screenshot, F10 = speed x3 / x1.
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

        if (now < _nextStatus) return;
        _nextStatus = now + 1f;
        try
        {
            var c = Sandbox.Controller;
            string player = c == null
                ? "player=none"
                : $"state={c.State} pos={c.transform.position} vel={c.Velocity} move={c.defaultMoveSpeed} sprint={c.sprintSpeed} scale={c._moveSpeedScale} max={c.MaxSpeed} grounded={c.isGrounded} rayGround={c.RaycastCheckGround(c.transform.position)} groundDist={c.GetGroundDistance()}";
            string clock = Sandbox.HasClock ? $"clock='{Sandbox.ClockText}' secs={TimeOfDayManager.TotalGameSeconds} frozen={Sandbox.ClockFrozen}" : "clock=none";
            Write($"t={now:0} menu={menu.IsOpen} cursor={CursorModeManager.IsCursorActive}/{Cursor.lockState}/{Cursor.visible}input={input.GetType().Name}{player} {clock}");
        }
        catch (Exception e)
        {
            Write($"status failed: {e}");
        }
    }

    private static void Write(string line) => File.AppendAllText(LogPath, line + "\n");
}
#endif
