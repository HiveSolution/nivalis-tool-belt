using System;
using System.Globalization;
using BepInEx.Unity.IL2CPP.Configuration;
using Il2CppInterop.Runtime;
using Nivalis;
using UnityEngine;
using UnityEngine.InputSystem;

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

    private const int MaxVisibleItems = 8;
    private const int MaxVisiblePeople = 6;
    private const int MaxVisibleVenues = 6;
    private const int DebtStep = 1000;
    private const int MaxFieldLength = 40;
    private const int TabsPerRow = 4;

    private static readonly string[] Tabs = { "Move", "Player", "Items", "People", "Venues", "Time", "Teleport" };
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
    private Vector2 _itemScroll;
    private string _itemQuery = "";
    private Vector2 _personScroll;
    private string _personQuery = "";
    private PersonEntry _person;
    private Vector2 _venueScroll;
    private VenueEntry _venue;
    private string _focusedField;
    private bool _typing;
    private Il2CppSystem.Collections.Generic.List<InputAction> _mutedActions;

    public ToolBeltBehaviour(IntPtr ptr) : base(ptr) { }

    internal bool IsOpen => _open;

    internal bool IsTyping => _typing;

    private void Awake()
    {
        _drawWindow = DelegateSupport.ConvertDelegate<GUI.WindowFunction>(new Action<int>(DrawWindow));
    }

    private void Update()
    {
        try
        {
            SetTyping(_open && _focusedField != null);
            if (IsPressed(Settings.MenuKey.Value)) SetOpen(!_open);
            if (IsPressed(Settings.FlyKey.Value)) Sandbox.Fly = !Sandbox.Fly;
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

    // A hotkey bound to a key that types a character must not fire while a text field has the keyboard.
    private bool IsPressed(KeyboardShortcut shortcut)
    {
        if (!shortcut.IsDown()) return false;
        int key = (int)shortcut.MainKey;
        bool typesCharacter = key >= (int)KeyCode.Space && key <= (int)KeyCode.Tilde;
        return !(_typing && typesCharacter);
    }

    // IMGUI text fields get their keys from the window, not from the game's input actions, so the
    // game would still react to every letter (inventory, map, ...). Switch its actions off meanwhile.
    private void SetTyping(bool typing)
    {
        if (_typing == typing) return;
        _typing = typing;
        if (typing)
        {
            _mutedActions = InputSystem.ListEnabledActions();
            for (int i = 0; i < _mutedActions.Count; i++) _mutedActions[i].Disable();
        }
        else if (_mutedActions != null)
        {
            for (int i = 0; i < _mutedActions.Count; i++) _mutedActions[i].Enable();
            _mutedActions = null;
        }
    }

    internal void SetOpen(bool open)
    {
        if (_open == open) return;
        _open = open;
        if (!open) _focusedField = null;
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
            for (int first = 0; first < Tabs.Length; first += TabsPerRow)
            {
                var cells = Columns(TabsPerRow);
                for (int i = first; i < Tabs.Length && i < first + TabsPerRow; i++)
                    if (Button(cells[i - first], Tabs[i], _tab == i) && _tab != i)
                    {
                        _tab = i;
                        _focusedField = null;
                    }
            }
            _y += SectionGap;

            switch (Tabs[_tab])
            {
                case "Move": DrawMove(); break;
                case "Player": DrawPlayer(); break;
                case "Items": DrawItems(); break;
                case "People": DrawPeople(); break;
                case "Venues": DrawVenues(); break;
                case "Time": DrawTime(); break;
                default: DrawTeleport(); break;
            }
        }

        _y += SectionGap;
        Theme.Rule(new Rect(Pad, _y, Width - 2f * Pad, 1f), false);
        _y += RowGap;
        GUI.Label(Row(20f), $"[{Settings.MenuKey.Value}] closes this menu", Theme.Hint);

        _window.height = _y + Pad - RowGap;
        GUI.DragWindow(new Rect(0f, 0f, Width, Header));
    }

    private void DrawItems()
    {
        _itemQuery = TextField(Row(), _itemQuery, "item-search", "Search items...");
        var items = Items.Find(_itemQuery);
        Heading("Add to inventory", items.Count == 1 ? "1 item" : $"{items.Count} items");

        const float line = RowHeight + RowGap;
        const float addWidth = 40f;
        int visible = Mathf.Min(items.Count, MaxVisibleItems);
        bool scrolls = items.Count > MaxVisibleItems;
        float listWidth = Width - 2f * Pad;
        float innerWidth = scrolls ? listWidth - 14f : listWidth;
        var area = new Rect(Pad, _y, listWidth, visible * line);
        _y += area.height;

        ItemEntry add = null;
        int amount = 0;
        if (scrolls) _itemScroll = GUI.BeginScrollView(area, _itemScroll, new Rect(0f, 0f, innerWidth, items.Count * line));
        // The catalogue has hundreds of entries; only the rows in view are drawn.
        int first = scrolls ? Mathf.Clamp((int)(_itemScroll.y / line), 0, items.Count - visible) : 0;
        int last = Mathf.Min(items.Count, first + visible + 1);
        for (int i = first; i < last; i++)
        {
            float x = scrolls ? 0f : area.x;
            float y = (scrolls ? 0f : area.y) + i * line;
            GUI.Label(new Rect(x, y, innerWidth - 2f * (addWidth + RowGap) - 4f, RowHeight), items[i].Name, Theme.Label);
            if (Button(new Rect(x + innerWidth - 2f * addWidth - RowGap, y, addWidth, RowHeight), "+1")) { add = items[i]; amount = 1; }
            if (Button(new Rect(x + innerWidth - addWidth, y, addWidth, RowHeight), "+10")) { add = items[i]; amount = 10; }
        }
        if (scrolls) GUI.EndScrollView();

        if (add != null) Items.Add(add, amount);
        GUI.Label(Row(20f), Items.LastResult ?? "Type to search, then add 1 or 10.", Theme.Hint);
    }

    /// <summary>
    /// A one-line text field. The game build has Unity's own text field stripped out, so this one
    /// collects the typed characters itself. While it has the keyboard, <see cref="Update"/> mutes
    /// the game's key bindings.
    /// </summary>
    private string TextField(Rect rect, string text, string name, string placeholder)
    {
        var current = Event.current;
        bool focused = _focusedField == name;
        if (current.type == EventType.MouseDown)
        {
            focused = rect.Contains(current.mousePosition);
            if (focused) _focusedField = name;
            else if (_focusedField == name) _focusedField = null;
        }
        else if (focused && current.type == EventType.KeyDown)
        {
            // A key press arrives twice: once as a key code, once as the character it types.
            var key = current.keyCode;
            char typed = current.character;
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Escape) _focusedField = null;
            else if (key == KeyCode.Backspace && text.Length > 0) text = text.Substring(0, text.Length - 1);
            else if (typed >= ' ' && typed != (char)127 && text.Length < MaxFieldLength) text += typed;
            current.Use();
        }

        bool caret = focused && (int)(Time.realtimeSinceStartup * 2f) % 2 == 0;
        if (text.Length == 0 && !focused) GUI.Label(rect, placeholder, Theme.FieldEmpty);
        else GUI.Label(rect, caret ? text + "|" : text, focused ? Theme.FieldFocused : Theme.Field);
        return text;
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

        _y += SectionGap;
        Heading("Debt and points");
        CounterRow("Noodle bar debt", Character.NoodleBarDebt, DebtStep, "-1k", "+1k");
        CounterRow("Other debt", Character.OtherDebt, DebtStep, "-1k", "+1k");
        CounterRow("Inspiration points", Character.InspirationPoints, 1, "-", "+");

        var skills = Character.Skills;
        if (skills != null && skills.Length > 0)
        {
            _y += SectionGap;
            Heading("Skill levels");
            for (int i = 0; i < skills.Length; i++)
            {
                var skill = skills[i];
                int level = Character.GetLevel(skill);
                // Shown from 1 like the game does; stored from 0.
                int change = Stepper(skill.DisplayName, $"{level + 1} / {Character.MaxLevel(skill) + 1}", "-", "+");
                if (change != 0) Character.SetLevel(skill, level + change);
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

    private void CounterRow(string label, string counter, int step, string minus, string plus)
    {
        if (!Character.HasCounter(counter)) return;
        int change = Stepper(label, Number(Character.GetCounter(counter), "#,0"), minus, plus);
        if (change != 0) Character.AddToCounter(counter, change * step);
    }

    private void DrawPeople()
    {
        _personQuery = TextField(Row(), _personQuery, "person-search", "Search people...");
        var people = People.Find(_personQuery);
        Heading("Relationships", people.Count == 1 ? "1 person" : $"{people.Count} people");

        const float line = RowHeight + RowGap;
        int visible = Mathf.Min(people.Count, MaxVisiblePeople);
        bool scrolls = people.Count > MaxVisiblePeople;
        float listWidth = Width - 2f * Pad;
        float innerWidth = scrolls ? listWidth - 14f : listWidth;
        var area = new Rect(Pad, _y, listWidth, visible * line);
        _y += area.height;

        if (scrolls) _personScroll = GUI.BeginScrollView(area, _personScroll, new Rect(0f, 0f, innerWidth, people.Count * line));
        int first = scrolls ? Mathf.Clamp((int)(_personScroll.y / line), 0, people.Count - visible) : 0;
        int last = Mathf.Min(people.Count, first + visible + 1);
        for (int i = first; i < last; i++)
        {
            float x = scrolls ? 0f : area.x;
            float y = (scrolls ? 0f : area.y) + i * line;
            string label = people[i].Met ? people[i].Name : people[i].Name + "  (not met)";
            if (Button(new Rect(x, y, innerWidth, RowHeight), label, people[i] == _person)) _person = people[i];
        }
        if (scrolls) GUI.EndScrollView();

        if (_person == null)
        {
            GUI.Label(Row(20f), "Pick someone to change how they see you.", Theme.Hint);
            return;
        }

        _y += SectionGap;
        Heading(_person.Name);
        foreach (string kind in People.Kinds)
        {
            int level = People.Get(_person, kind);
            int change = Stepper(kind, $"{level} / {People.MaxLevel}", "-", "+");
            if (change != 0) People.Set(_person, kind, level + change);
        }
    }

    private void DrawVenues()
    {
        var venues = Venues.List();
        Heading("Venues", venues.Count == 1 ? "1 venue" : $"{venues.Count} venues");
        if (venues.Count == 0)
        {
            GUI.Label(Row(), "No venues found.", Theme.Label);
            return;
        }

        const float line = RowHeight + RowGap;
        int visible = Mathf.Min(venues.Count, MaxVisibleVenues);
        bool scrolls = venues.Count > MaxVisibleVenues;
        float listWidth = Width - 2f * Pad;
        float innerWidth = scrolls ? listWidth - 14f : listWidth;
        var area = new Rect(Pad, _y, listWidth, visible * line);
        _y += area.height;

        if (scrolls) _venueScroll = GUI.BeginScrollView(area, _venueScroll, new Rect(0f, 0f, innerWidth, venues.Count * line));
        for (int i = 0; i < venues.Count; i++)
        {
            float x = scrolls ? 0f : area.x;
            float y = (scrolls ? 0f : area.y) + i * line;
            string label = Venues.IsMine(venues[i]) ? venues[i].Name + "  (yours)" : venues[i].Name;
            if (Button(new Rect(x, y, innerWidth, RowHeight), label, venues[i] == _venue)) _venue = venues[i];
        }
        if (scrolls) GUI.EndScrollView();

        if (_venue == null || !venues.Contains(_venue))
        {
            _venue = null;
            GUI.Label(Row(20f), "Pick a venue. Yours are listed first.", Theme.Hint);
            return;
        }

        _y += SectionGap;
        if (Venues.IsMine(_venue))
        {
            Heading(_venue.Name, "Yours");
            int level = Venues.GetLevel(_venue);
            int change = Stepper("Level", $"{level} / {Venues.MaxLevel(_venue)}", "-", "+");
            if (change != 0) Venues.SetLevel(_venue, level + change);
            if (Venues.IsStartingVenue(_venue)) GUI.Label(Row(20f), "Your starting venue cannot be given up here.", Theme.Hint);
            else if (Button(Row(), "Give up this venue")) Venues.GiveUp(_venue);
        }
        else
        {
            Heading(_venue.Name, "Not yours");
            if (Button(Row(), "Take over for free")) Venues.TakeOver(_venue);
        }
    }

    /// <summary>A row with a label, a value and two buttons. Returns -1 or +1 for the button clicked, else 0.</summary>
    private int Stepper(string label, string value, string minus, string plus)
    {
        var row = Row();
        float step = minus.Length > 1 ? 40f : 28f;
        var text = new Rect(row.x, row.y, row.width - 2f * (step + RowGap) - 4f, row.height);
        GUI.Label(text, label, Theme.Label);
        GUI.Label(text, value, Theme.LabelRight);
        if (Button(new Rect(row.xMax - 2f * step - RowGap, row.y, step, row.height), minus)) return -1;
        return Button(new Rect(row.xMax - step, row.y, step, row.height), plus) ? 1 : 0;
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
