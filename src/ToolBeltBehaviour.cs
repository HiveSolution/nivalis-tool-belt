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
    private const int MaxVisibleSpots = 6;

    private static readonly string[] Tabs = { "Player", "Time", "Teleport" };
    private static readonly float[] ClockSpeeds = { 0.25f, 0.5f, 1f, 2f, 5f, 10f };
    private static readonly Color Selected = new Color(1f, 0.8f, 0.35f);

    private Rect _window = new Rect(40f, 150f, Width, 200f);
    private GUI.WindowFunction _drawWindow;
    private bool _open;
    private bool _ownsCursor;
    private bool _guiFailed;
    private float _y;
    private int _tab;
    private int _skipToHour = 6;
    private Vector2 _spotScroll;

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
            Clock.Tick();
            Teleports.Tick();
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
            var tabs = Columns(Tabs.Length);
            for (int i = 0; i < Tabs.Length; i++)
                if (Button(tabs[i], Tabs[i], _tab == i)) _tab = i;
            _y += 4f;

            if (_tab == 0) DrawPlayer();
            else if (_tab == 1) DrawTime();
            else DrawTeleport();
        }

        _y += 4f;
        GUI.Label(Row(), $"[{Settings.MenuKey.Value}] closes this menu");

        _window.height = _y + Pad - RowGap;
        GUI.DragWindow(new Rect(0f, 0f, Width, TitleBar));
    }

    private void DrawPlayer()
    {
        bool fly = GUI.Toggle(Row(), Sandbox.Fly, $" Fly / ghost mode  [{Settings.FlyKey.Value}]");
        if (fly != Sandbox.Fly) Sandbox.Fly = fly;
        if (fly) GUI.Label(Row(), "      E up, Q down, passes through walls");

        GUI.Label(Row(), "Movement speed  x" + Number(Sandbox.SpeedMultiplier, "0.0"));
        float speed = GUI.HorizontalSlider(Row(16f), Sandbox.SpeedMultiplier, 0.5f, Settings.MaxSpeedMultiplier.Value);
        speed = Mathf.Round(speed * 10f) / 10f;
        if (speed != Sandbox.SpeedMultiplier) Sandbox.SpeedMultiplier = speed;
        if (GUI.Button(Row(), "Reset speed")) Sandbox.SpeedMultiplier = 1f;
    }

    private void DrawTime()
    {
        if (!Clock.Available)
        {
            GUI.Label(Row(), "No clock in this scene.");
            return;
        }

        GUI.Label(Row(), Clock.Text);
        bool frozen = GUI.Toggle(Row(), Clock.Frozen, " Freeze clock");
        if (frozen != Clock.Frozen) Clock.Frozen = frozen;

        GUI.Label(Row(), "Clock speed");
        var speeds = Columns(ClockSpeeds.Length);
        for (int i = 0; i < ClockSpeeds.Length; i++)
            if (Button(speeds[i], "x" + Number(ClockSpeeds[i], "0.##"), Clock.SpeedMultiplier == ClockSpeeds[i]))
                Clock.SpeedMultiplier = ClockSpeeds[i];

        _y += 4f;
        if (Clock.InCurfew)
        {
            GUI.Label(Row(), "Curfew: sleep to start the next day.");
            return;
        }
        if (GUI.Button(Row(), "Skip 1 hour")) Clock.SkipHours(1);
        _skipToHour = Mathf.RoundToInt(GUI.HorizontalSlider(Row(16f), _skipToHour, 0f, 23f));
        string target = Clock.CurfewComesBefore(_skipToHour) ? $"curfew ({Clock.CurfewHour:00}:00)" : $"{_skipToHour:00}:00";
        if (GUI.Button(Row(), $"Skip ahead to {target}")) Clock.SkipTo(_skipToHour);
    }

    private void DrawTeleport()
    {
        if (Teleports.Pending != null)
        {
            GUI.Label(Row(), $"Travelling to {Teleports.Pending.Name}...");
            return;
        }

        GUI.Label(Row(), $"You are in {Teleports.AreaName}");
        if (GUI.Button(Row(), "Save current position")) Teleports.SaveCurrent();

        var spots = Teleports.Listed;
        if (spots.Count == 0)
        {
            GUI.Label(Row(), "No saved spots yet.");
            return;
        }

        const float line = RowHeight + RowGap;
        const float removeWidth = 28f;
        bool scrolls = spots.Count > MaxVisibleSpots;
        float listWidth = Width - 2f * Pad;
        float innerWidth = scrolls ? listWidth - 18f : listWidth;
        var area = new Rect(Pad, _y, listWidth, Mathf.Min(spots.Count, MaxVisibleSpots) * line);
        _y += area.height;

        Spot go = null, remove = null;
        if (scrolls) _spotScroll = GUI.BeginScrollView(area, _spotScroll, new Rect(0f, 0f, innerWidth, spots.Count * line));
        for (int i = 0; i < spots.Count; i++)
        {
            float x = scrolls ? 0f : area.x;
            float y = (scrolls ? 0f : area.y) + i * line;
            // Spots in other areas need a trip there first; the game shows its usual travel transition.
            string label = Teleports.IsHere(spots[i]) ? spots[i].Name : spots[i].Name + "  (travel)";
            if (GUI.Button(new Rect(x, y, innerWidth - removeWidth - RowGap, RowHeight), label)) go = spots[i];
            if (GUI.Button(new Rect(x + innerWidth - removeWidth, y, removeWidth, RowHeight), "X")) remove = spots[i];
        }
        if (scrolls) GUI.EndScrollView();

        // Applied after the loop, both change what the list shows.
        if (remove != null) Teleports.Remove(remove);
        // Get out of the way of the travel transition.
        if (go != null && Teleports.Go(go)) SetOpen(false);
    }

    private static bool Button(Rect rect, string text, bool selected)
    {
        var color = GUI.color;
        if (selected) GUI.color = Selected;
        bool clicked = GUI.Button(rect, text);
        GUI.color = color;
        return clicked;
    }

    private static string Number(float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private Rect Row(float height = RowHeight)
    {
        var rect = new Rect(Pad, _y, Width - 2f * Pad, height);
        _y += height + RowGap;
        return rect;
    }

    /// <summary>One row split into <paramref name="count"/> equal cells.</summary>
    private Rect[] Columns(int count)
    {
        var row = Row();
        float width = (row.width - (count - 1) * RowGap) / count;
        var cells = new Rect[count];
        for (int i = 0; i < count; i++)
            cells[i] = new Rect(row.x + i * (width + RowGap), row.y, width, row.height);
        return cells;
    }
}
