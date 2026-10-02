using System;
using System.Globalization;
using Il2CppInterop.Runtime;
using Nivalis;
using UnityEngine;

namespace NivalisToolBelt;

/// <summary>Hotkeys and the IMGUI menu. Added to BepInEx's persistent object by <see cref="Plugin"/>.</summary>
public class ToolBeltBehaviour : MonoBehaviour
{
    private const int WindowId = 0x4E544231; // "NTB1"
    private const float Width = 300f;
    private const float Pad = 12f;
    private const float RowHeight = 24f;
    private const float RowGap = 4f;
    private const float TitleBar = 24f;

    private Rect _window = new Rect(40f, 150f, Width, 200f);
    private GUI.WindowFunction _drawWindow;
    private bool _open;
    private bool _ownsCursor;
    private bool _guiFailed;
    private float _y;

    public ToolBeltBehaviour(IntPtr ptr) : base(ptr) { }

    internal bool IsOpen => _open;

    private void Awake()
    {
        _drawWindow = DelegateSupport.ConvertDelegate<GUI.WindowFunction>(new Action<int>(DrawWindow));
    }

    private void Update()
    {
        try
        {
            if (Settings.MenuKey.Value.IsDown()) SetOpen(!_open);
            if (Settings.FlyKey.Value.IsDown()) Sandbox.Fly = !Sandbox.Fly;
            Sandbox.Tick();
            if (_open) HoldCursor();
#if SELFTEST
            SelfTest.Tick(this);
#endif
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }

    internal void SetOpen(bool open)
    {
        if (_open == open) return;
        _open = open;
        if (open || !_ownsCursor) return;
        _ownsCursor = false;
        try
        {
            CursorModeManager.ReleaseApplicationCursor();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Cursor release failed: {e.Message}");
        }
    }

    // In first person the game hides and locks the cursor. Its UI cursor request is recomputed by
    // UIManager from the game's own windows, so the menu takes the "application" request instead
    // (the one the game uses while it is out of focus). The game drops that one when it regains
    // focus, hence the check every frame.
    private void HoldCursor()
    {
        if (CursorModeManager.IsCursorActive) return;
        CursorModeManager.RequestApplicationCursor();
        _ownsCursor = true;
    }

    private void OnGUI()
    {
        if (!_open || _guiFailed) return;
        try
        {
            _window = GUI.Window(WindowId, _window, _drawWindow, $"{Plugin.Name} {Plugin.Version}");
        }
        catch (Exception e)
        {
            _guiFailed = true;
            Plugin.Logger.LogError($"Menu disabled, drawing failed: {e}");
        }
    }

    private void DrawWindow(int id)
    {
        _y = TitleBar + 4f;

        // The default window skin is nearly see-through over a bright scene.
        var backdrop = new Rect(0f, TitleBar - 4f, Width, _window.height - TitleBar + 4f);
        GUI.Box(backdrop, "");
        GUI.Box(backdrop, "");

        if (!Sandbox.InGame)
        {
            GUI.Label(Row(), "Load a save to use the tools.");
        }
        else
        {
            Header("Player");
            bool fly = GUI.Toggle(Row(), Sandbox.Fly, $" Fly / ghost mode  [{Settings.FlyKey.Value}]");
            if (fly != Sandbox.Fly) Sandbox.Fly = fly;
            if (fly) GUI.Label(Row(), "      E up, Q down, passes through walls");

            GUI.Label(Row(), "Movement speed  x" + Sandbox.SpeedMultiplier.ToString("0.0", CultureInfo.InvariantCulture));
            float speed = GUI.HorizontalSlider(Row(16f), Sandbox.SpeedMultiplier, 0.5f, Settings.MaxSpeedMultiplier.Value);
            speed = Mathf.Round(speed * 10f) / 10f;
            if (speed != Sandbox.SpeedMultiplier) Sandbox.SpeedMultiplier = speed;
            if (GUI.Button(Row(), "Reset speed")) Sandbox.SpeedMultiplier = 1f;

            if (Sandbox.HasClock)
            {
                Header("Time");
                GUI.Label(Row(), Sandbox.ClockText);
                bool frozen = GUI.Toggle(Row(), Sandbox.ClockFrozen, " Freeze clock");
                if (frozen != Sandbox.ClockFrozen) Sandbox.ClockFrozen = frozen;
                if (GUI.Button(Row(), "Skip 1 hour")) Sandbox.SkipHours(1);
            }
        }

        _y += 4f;
        GUI.Label(Row(), $"[{Settings.MenuKey.Value}] closes this menu");

        _window.height = _y + Pad - RowGap;
        GUI.DragWindow(new Rect(0f, 0f, Width, TitleBar));
    }

    private void Header(string text)
    {
        _y += 4f;
        GUI.Label(Row(), $"<b>{text}</b>");
    }

    private Rect Row(float height = RowHeight)
    {
        var rect = new Rect(Pad, _y, Width - 2f * Pad, height);
        _y += height + RowGap;
        return rect;
    }
}
