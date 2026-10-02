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
    private const float Width = 330f;
    private const float Pad = 16f;
    private const float RowHeight = 26f;
    private const float RowGap = 5f;
    private const float SectionGap = 8f;
    private const float SliderHeight = 18f;
    private const float Header = 36f;
    private const int MaxVisibleSpots = 6;

    private static readonly string[] Tabs = { "Move", "Player", "Time", "Teleport" };
    private static readonly int[] MoneySteps = { -1000, -100, 100, 1000, 10000 };
    private static readonly string[] MoneyLabels = { "-1k", "-100", "+100", "+1k", "+10k" };
    private static readonly float[] ClockSpeeds = { 0.25f, 0.5f, 1f, 2f, 5f, 10f };

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
            Theme.Ensure();
            _window = GUI.Window(WindowId, _window, _drawWindow, "", Theme.Window);
        }
        catch (Exception e)
        {
            _guiFailed = true;
            Plugin.Logger.LogError($"Menu disabled, drawing failed: {e}");
        }
    }

    private void DrawWindow(int id)
    {
        // Scroll views take their scrollbar from the skin; everything else is styled per call.
        var skin = GUI.skin;
        GUI.skin = Theme.Skin;
        try
        {
            DrawContents();
        }
        catch (Exception e)
        {
            _guiFailed = true;
            Plugin.Logger.LogError($"Menu disabled, drawing failed: {e}");
        }
        finally
        {
            GUI.skin = skin;
        }
    }

    private void DrawContents()
    {
        // The plate's top-right corner is cut, so the version sits a little further in.
        GUI.Label(new Rect(Pad, 0f, Width - 2f * Pad, Header), "TOOL BELT", Theme.Title);
        GUI.Label(new Rect(Pad, 0f, Width - 2f * Pad - 8f, Header), Plugin.Version, Theme.Version);
        Theme.Rule(new Rect(1f, Header, Width - 2f, 2f), true);
        _y = Header + 12f;

        if (!Sandbox.InGame)
        {
            GUI.Label(Row(), "Load a save to use the tools.", Theme.Label);
        }
        else
        {
            var tabs = Columns(Tabs.Length);
            for (int i = 0; i < Tabs.Length; i++)
                if (Button(tabs[i], Tabs[i], _tab == i)) _tab = i;
            _y += SectionGap;

            if (_tab == 0) DrawMove();
            else if (_tab == 1) DrawPlayer();
            else if (_tab == 2) DrawTime();
            else DrawTeleport();
        }

        _y += SectionGap;
        Theme.Rule(new Rect(Pad, _y, Width - 2f * Pad, 1f), false);
        _y += RowGap;
        GUI.Label(Row(20f), $"[{Settings.MenuKey.Value}] closes this menu", Theme.Hint);

        _window.height = _y + Pad - RowGap;
        GUI.DragWindow(new Rect(0f, 0f, Width, Header));
    }

    private void DrawMove()
    {
        var row = Row();
        bool fly = Theme.Toggle(row, Sandbox.Fly, "Fly / ghost mode");
        GUI.Label(row, $"[{Settings.FlyKey.Value}]", Theme.Version);
        if (fly != Sandbox.Fly) Sandbox.Fly = fly;
        if (fly) GUI.Label(Row(20f), "E up, Q down, passes through walls", Theme.Hint);

        _y += SectionGap;
        Heading("Movement speed", "x" + Number(Sandbox.SpeedMultiplier, "0.0"));
        float speed = Theme.Slider(Row(SliderHeight), Sandbox.SpeedMultiplier, 0.5f, Settings.MaxSpeedMultiplier.Value);
        speed = Mathf.Round(speed * 10f) / 10f;
        if (speed != Sandbox.SpeedMultiplier) Sandbox.SpeedMultiplier = speed;
        if (Button(Row(), "Reset speed")) Sandbox.SpeedMultiplier = 1f;
    }

    private void DrawPlayer()
    {
        Heading("Money", Number(Character.MoneyCents / 100f, "#,0.00"));
        var amounts = Columns(MoneySteps.Length);
        for (int i = 0; i < MoneySteps.Length; i++)
            if (Button(amounts[i], MoneyLabels[i])) Character.AddMoney(MoneySteps[i] * 100);

        var skills = Character.Skills;
        if (skills != null && skills.Length > 0)
        {
            _y += SectionGap;
            Heading("Skill levels");
            for (int i = 0; i < skills.Length; i++)
            {
                var skill = skills[i];
                int level = Character.GetLevel(skill);
                var row = Row();
                const float step = 28f;
                var text = new Rect(row.x, row.y, row.width - 2f * (step + RowGap) - 4f, row.height);
                GUI.Label(text, skill.DisplayName, Theme.Label);
                // Shown from 1 like the game does; stored from 0.
                GUI.Label(text, $"{level + 1} / {Character.MaxLevel(skill) + 1}", Theme.LabelRight);
                if (Button(new Rect(row.xMax - 2f * step - RowGap, row.y, step, row.height), "-")) Character.SetLevel(skill, level - 1);
                if (Button(new Rect(row.xMax - step, row.y, step, row.height), "+")) Character.SetLevel(skill, level + 1);
            }
        }

        if (Character.HasBoat)
        {
            _y += SectionGap;
            if (Character.BoatUnlocked)
            {
                Heading("Boat", "Unlocked");
            }
            else
            {
                Heading("Boat", "Locked");
                if (Button(Row(), "Unlock the boat")) Character.UnlockBoat();
            }
        }
    }

    private void DrawTime()
    {
        if (!Clock.Available)
        {
            GUI.Label(Row(), "No clock in this scene.", Theme.Label);
            return;
        }

        Heading("Clock", Clock.Text);
        bool frozen = Theme.Toggle(Row(), Clock.Frozen, "Freeze clock");
        if (frozen != Clock.Frozen) Clock.Frozen = frozen;

        _y += SectionGap;
        Heading("Clock speed");
        var speeds = Columns(ClockSpeeds.Length);
        for (int i = 0; i < ClockSpeeds.Length; i++)
            if (Button(speeds[i], "x" + Number(ClockSpeeds[i], "0.##"), Clock.SpeedMultiplier == ClockSpeeds[i]))
                Clock.SpeedMultiplier = ClockSpeeds[i];

        _y += SectionGap;
        Heading("Skip ahead");
        if (Clock.InCurfew)
        {
            GUI.Label(Row(), "Curfew: sleep to start the next day.", Theme.Label);
            return;
        }
        if (Button(Row(), "Skip 1 hour")) Clock.SkipHours(1);
        _skipToHour = Mathf.RoundToInt(Theme.Slider(Row(SliderHeight), _skipToHour, 0f, 23f));
        string target = Clock.CurfewComesBefore(_skipToHour) ? $"curfew ({Clock.CurfewHour:00}:00)" : $"{_skipToHour:00}:00";
        if (Button(Row(), $"Skip to {target}")) Clock.SkipTo(_skipToHour);
    }

    private void DrawTeleport()
    {
        if (Teleports.Pending != null)
        {
            GUI.Label(Row(), $"Travelling to {Teleports.Pending.Name}...", Theme.Label);
            return;
        }

        Heading("Saved spots", $"You are in {Teleports.AreaName}");
        if (Button(Row(), "Save current position")) Teleports.SaveCurrent();

        var spots = Teleports.Listed;
        if (spots.Count == 0)
        {
            GUI.Label(Row(), "No saved spots yet.", Theme.Label);
            return;
        }

        const float line = RowHeight + RowGap;
        const float removeWidth = 28f;
        bool scrolls = spots.Count > MaxVisibleSpots;
        float listWidth = Width - 2f * Pad;
        float innerWidth = scrolls ? listWidth - 14f : listWidth;
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
            if (Button(new Rect(x, y, innerWidth - removeWidth - RowGap, RowHeight), label)) go = spots[i];
            if (Button(new Rect(x + innerWidth - removeWidth, y, removeWidth, RowHeight), "X")) remove = spots[i];
        }
        if (scrolls) GUI.EndScrollView();

        // Applied after the loop, both change what the list shows.
        if (remove != null) Teleports.Remove(remove);
        // Get out of the way of the travel transition.
        if (go != null && Teleports.Go(go)) SetOpen(false);
    }

    /// <summary>The game writes its button and heading text in capitals.</summary>
    private static bool Button(Rect rect, string text, bool selected = false) =>
        GUI.Button(rect, text.ToUpperInvariant(), selected ? Theme.ButtonSelected : Theme.Button);

    /// <summary>A gold section heading, optionally with a value on the right of the same row.</summary>
    private void Heading(string text, string value = null)
    {
        var row = Row(22f);
        GUI.Label(row, text.ToUpperInvariant(), Theme.Heading);
        if (value != null) GUI.Label(row, value, Theme.LabelRight);
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
