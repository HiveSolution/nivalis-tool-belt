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

    private static readonly string[] Tabs = { "General","Player", "Items", "People", "Property", "Time", "Teleport" };
    private static readonly string[] ItemModes = { "Catalogue", "Inventory" };
    private static readonly string[] PropertyKinds = { "Venues", "Apartments", "Greenhouses" };
    private const string RenameField = "spot-rename";
    private const float NoticeWidth = 340f;
    private const float NoticeSeconds = 2.5f;
    private const int WeatherPerRow = 2;

    private static readonly Nivalis.GhostSystem.Ai.Happiness[] Moods =
    {
        Nivalis.GhostSystem.Ai.Happiness.Sad, Nivalis.GhostSystem.Ai.Happiness.Normal, Nivalis.GhostSystem.Ai.Happiness.Happy,
    };

    private static readonly int[] MoneySteps = { -1000, -100, 100, 1000, 10000 };
    private static readonly string[] MoneyLabels = { "-1k", "-100", "+100", "+1k", "+10k" };
    private static readonly float[] ClockSpeeds = { 0.25f, 0.5f, 1f, 2f, 5f, 10f };

    private Rect _window = new Rect(40f, 150f, Width, 200f);
    private GUI.WindowFunction _drawWindow;
    private bool _open;
    private bool _ownsCursor;
    private bool _guiFailed;
    private float _y;
    private string _notice = "";
    private float _noticeUntil;
    private int _tab;
    private float _draggedScale = -1f;
    private int _skipToHour = 6;
    private Vector2 _spotScroll;
    private Vector2 _itemScroll;
    private string _itemQuery = "";
    private Vector2 _personScroll;
    private string _personQuery = "";
    private PersonEntry _person;
    private Vector2 _venueScroll;
    private VenueEntry _venue;
    private int _propertyKind;
    private EstateEntry _apartment;
    private Vector2 _apartmentScroll;
    private EstateEntry _greenhouse;
    private Vector2 _greenhouseScroll;
    private int _itemMode;
    private bool _renameMode;
    private Spot _renaming;
    private string _renameText = "";
    private string _focusedField;
    private bool _typing;
    private Il2CppSystem.Collections.Generic.List<InputAction> _mutedActions;

    public ToolBeltBehaviour(IntPtr ptr) : base(ptr) { }

    internal bool IsOpen => _open;

    internal bool IsTyping => _typing;

    internal Rect WindowRect => _window;

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
            if (IsPressed(Settings.FlyKey.Value))
            {
                Sandbox.Fly = !Sandbox.Fly;
                Notify("Fly / ghost mode", Sandbox.Fly);
            }
            if (IsPressed(Settings.UndetectedKey.Value))
            {
                Toggles.Undetected = !Toggles.Undetected;
                Notify("Undetected during curfew", Toggles.Undetected);
            }
            if (IsPressed(Settings.BoatFuelKey.Value))
            {
                Toggles.BoatFuel = !Toggles.BoatFuel;
                Notify("Unlimited boat fuel", Toggles.BoatFuel);
            }
            if (IsPressed(Settings.GrowthKey.Value))
            {
                Toggles.InstantGrowth = !Toggles.InstantGrowth;
                Notify("Instant greenhouse growth", Toggles.InstantGrowth);
            }
            Toggles.Tick();
            Venues.Tick();
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

    /// <summary>Shows a short notice on screen: a hotkey gives no other sign of what it did while the menu is closed.</summary>
    private void Notify(string what, bool on)
    {
        _notice = $"{what}: {(on ? "on" : "off")}";
        _noticeUntil = Time.realtimeSinceStartup + NoticeSeconds;
    }

    private void OnGUI()
    {
        bool notice = Time.realtimeSinceStartup < _noticeUntil;
        if ((!_open && !notice) || _guiFailed) return;
        try
        {
            Theme.Ensure();
            // A new scale would move the slider away from the mouse mid-drag, so it waits for the release.
            if (_draggedScale > 0f && GUIUtility.hotControl == 0)
            {
                Settings.MenuScale.Value = _draggedScale;
                _draggedScale = -1f;
            }
            float scale = Settings.MenuScale.Value;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float screenWidth = Screen.width / scale;
            // Keep the title bar on screen, or a bigger scale could push the menu out of reach.
            _window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, screenWidth - Width));
            _window.y = Mathf.Clamp(_window.y, 0f, Mathf.Max(0f, Screen.height / scale - Header));
            if (notice) GUI.Label(new Rect((screenWidth - NoticeWidth) / 2f, 190f, NoticeWidth, 34f), _notice, Theme.Notice);
            if (_open) _window = GUI.Window(WindowId, _window, _drawWindow, "", Theme.Window);
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
                case "General": DrawGeneral(); break;
                case "Player": DrawPlayer(); break;
                case "Items": DrawItems(); break;
                case "People": DrawPeople(); break;
                case "Property": DrawProperty(); break;
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
        var modes = Columns(ItemModes.Length);
        for (int i = 0; i < ItemModes.Length; i++)
            if (Button(modes[i], ItemModes[i], _itemMode == i)) _itemMode = i;

        _itemQuery = TextField(Row(), _itemQuery, "item-search", "Search items...");
        bool catalogue = _itemMode == 0;
        var items = catalogue ? Items.Find(_itemQuery) : Items.FindOwned(_itemQuery);
        Heading(catalogue ? "Add to inventory" : "In your inventory", items.Count == 1 ? "1 item" : $"{items.Count} items");

        const float line = RowHeight + RowGap;
        const float actionWidth = 40f;
        int visible = Mathf.Min(items.Count, MaxVisibleItems);
        bool scrolls = items.Count > MaxVisibleItems;
        float listWidth = Width - 2f * Pad;
        float innerWidth = scrolls ? listWidth - 14f : listWidth;
        var area = new Rect(Pad, _y, listWidth, visible * line);
        _y += area.height;

        ItemEntry picked = null;
        int amount = 0;
        if (scrolls) _itemScroll = GUI.BeginScrollView(area, _itemScroll, new Rect(0f, 0f, innerWidth, items.Count * line));
        // The catalogue has hundreds of entries; only the rows in view are drawn.
        int first = scrolls ? Mathf.Clamp((int)(_itemScroll.y / line), 0, items.Count - visible) : 0;
        int last = Mathf.Min(items.Count, first + visible + 1);
        for (int i = first; i < last; i++)
        {
            float x = scrolls ? 0f : area.x;
            float y = (scrolls ? 0f : area.y) + i * line;
            var text = new Rect(x, y, innerWidth - 2f * (actionWidth + RowGap) - 4f, RowHeight);
            GUI.Label(text, items[i].Name, Theme.Label);
            if (!catalogue) GUI.Label(text, items[i].Count.ToString(), Theme.LabelRight);
            // In the inventory view an amount of zero stands for "all of them".
            if (Button(new Rect(x + innerWidth - 2f * actionWidth - RowGap, y, actionWidth, RowHeight), catalogue ? "+1" : "-1")) { picked = items[i]; amount = 1; }
            if (Button(new Rect(x + innerWidth - actionWidth, y, actionWidth, RowHeight), catalogue ? "+10" : "All")) { picked = items[i]; amount = catalogue ? 10 : 0; }
        }
        if (scrolls) GUI.EndScrollView();

        if (picked != null)
        {
            if (catalogue) Items.Add(picked, amount);
            else Items.Remove(picked, amount);
        }
        string hint = catalogue ? "Type to search, then add 1 or 10." : "Removed items are gone for good.";
        GUI.Label(Row(20f), Items.LastResult ?? hint, Theme.Hint);
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

    private void DrawGeneral()
    {
        var row = Row();
        bool fly = Theme.Toggle(row, Sandbox.Fly, "Fly / ghost mode");
        GUI.Label(row, $"[{Settings.FlyKey.Value}]", Theme.Version);
        if (fly != Sandbox.Fly) Sandbox.Fly = fly;
        if (fly) GUI.Label(Row(20f), "E up, Q down, passes through walls", Theme.Hint);

        row = Row();
        bool undetected = Theme.Toggle(row, Toggles.Undetected, "Undetected during curfew");
        GUI.Label(row, $"[{Settings.UndetectedKey.Value}]", Theme.Version);
        if (undetected != Toggles.Undetected) Toggles.Undetected = undetected;

        row = Row();
        bool fuel = Theme.Toggle(row, Toggles.BoatFuel, "Unlimited boat fuel");
        GUI.Label(row, $"[{Settings.BoatFuelKey.Value}]", Theme.Version);
        if (fuel != Toggles.BoatFuel) Toggles.BoatFuel = fuel;

        _y += SectionGap;
        Heading("Movement speed", "x" + Number(Sandbox.SpeedMultiplier, "0.0"));
        float speed = Theme.Slider(Row(SliderHeight), Sandbox.SpeedMultiplier, 0.5f, Settings.MaxSpeedMultiplier.Value);
        speed = Mathf.Round(speed * 10f) / 10f;
        if (speed != Sandbox.SpeedMultiplier) Sandbox.SpeedMultiplier = speed;
        if (Button(Row(), "Reset speed")) Sandbox.SpeedMultiplier = 1f;

        _y += SectionGap;
        float scale = _draggedScale > 0f ? _draggedScale : Settings.MenuScale.Value;
        Heading("Menu scale", "x" + Number(scale, "0.0"));
        float picked = Theme.Slider(Row(SliderHeight), scale, Settings.MinMenuScale, Settings.MaxMenuScale);
        picked = Mathf.Round(picked * 10f) / 10f;
        if (picked != scale) _draggedScale = picked;
        if (Button(Row(), "Reset scale"))
        {
            _draggedScale = -1f;
            Settings.MenuScale.Value = 1f;
        }
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

    private void DrawProperty()
    {
        var kinds = Columns(PropertyKinds.Length);
        for (int i = 0; i < PropertyKinds.Length; i++)
            if (Button(kinds[i], PropertyKinds[i], _propertyKind == i)) _propertyKind = i;

        if (_propertyKind == 0)
        {
            DrawVenues();
            DrawAllVenues();
        }
        else if (_propertyKind == 1)
        {
            DrawEstates(Estates.Apartments(), "Apartments", ref _apartment, ref _apartmentScroll);
        }
        else
        {
            var row = Row();
            bool growth = Theme.Toggle(row, Toggles.InstantGrowth, "Instant growth in your greenhouses");
            GUI.Label(row, $"[{Settings.GrowthKey.Value}]", Theme.Version);
            if (growth != Toggles.InstantGrowth) Toggles.InstantGrowth = growth;
            DrawEstates(Estates.Greenhouses(), "Greenhouses", ref _greenhouse, ref _greenhouseScroll);
        }
    }

    // Settings that apply to every venue the player holds.
    private void DrawAllVenues()
    {
        _y += SectionGap;
        Heading("All your venues");
        int change = Stepper("Storage space", $"x{Venues.StorageMultiplier}", "-", "+");
        if (change != 0) Venues.StorageMultiplier += change;

        var held = Venues.HeldHappiness;
        int staff = Venues.StaffCount;
        Heading("Staff happiness", held == null ? "Up to the game" : $"Held for {staff} staff");
        var moods = Columns(Moods.Length);
        for (int i = 0; i < Moods.Length; i++)
            if (Button(moods[i], Moods[i].ToString(), held == Moods[i])) Venues.HoldStaffHappiness(Moods[i]);
        if (held != null && Button(Row(), "Leave it to the game again")) Venues.HoldStaffHappiness(null);
    }

    private void DrawEstates(System.Collections.Generic.List<EstateEntry> estates, string title, ref EstateEntry selected, ref Vector2 scroll)
    {
        Heading(title, estates.Count.ToString());
        if (estates.Count == 0)
        {
            GUI.Label(Row(), "None found.", Theme.Label);
            return;
        }

        const float line = RowHeight + RowGap;
        int visible = Mathf.Min(estates.Count, MaxVisibleVenues);
        bool scrolls = estates.Count > MaxVisibleVenues;
        float listWidth = Width - 2f * Pad;
        float innerWidth = scrolls ? listWidth - 14f : listWidth;
        var area = new Rect(Pad, _y, listWidth, visible * line);
        _y += area.height;

        if (scrolls) scroll = GUI.BeginScrollView(area, scroll, new Rect(0f, 0f, innerWidth, estates.Count * line));
        for (int i = 0; i < estates.Count; i++)
        {
            float x = scrolls ? 0f : area.x;
            float y = (scrolls ? 0f : area.y) + i * line;
            string label = Estates.IsMine(estates[i]) ? estates[i].Name + "  (yours)" : estates[i].Name;
            if (Button(new Rect(x, y, innerWidth, RowHeight), label, estates[i] == selected)) selected = estates[i];
        }
        if (scrolls) GUI.EndScrollView();

        if (selected == null || !estates.Contains(selected))
        {
            selected = null;
            GUI.Label(Row(20f), "Pick one. Yours are listed first.", Theme.Hint);
            return;
        }

        _y += SectionGap;
        if (Estates.IsMine(selected))
        {
            bool rented = Estates.State(selected) == Nivalis.GhostSystem.CustomerLoop.OwnershipType.Rent;
            Heading(selected.Name, rented ? "Rented" : "Owned");
            if (rented) GUI.Label(Row(20f), $"The game charges {Number(Estates.DailyRent(selected) / 100f, "#,0.00")} rent a day.", Theme.Hint);
            if (Estates.IsOnlyHome(selected)) GUI.Label(Row(20f), "Your only home cannot be given up here.", Theme.Hint);
            else if (Button(Row(), "Give up")) Estates.GiveUp(selected);
        }
        else
        {
            Heading(selected.Name, "Not yours");
            if (Button(Row(), $"Rent ({Number(Estates.DailyRent(selected) / 100f, "#,0.00")} a day)")) Estates.Rent(selected);
            if (Button(Row(), "Take over for free")) Estates.Buy(selected);
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
        }
        else
        {
            if (Button(Row(), "Skip 1 hour")) Clock.SkipHours(1);
            _skipToHour = Mathf.RoundToInt(Theme.Slider(Row(SliderHeight), _skipToHour, 0f, 23f));
            string target = Clock.CurfewComesBefore(_skipToHour) ? $"curfew ({Clock.CurfewHour:00}:00)" : $"{_skipToHour:00}:00";
            if (Button(Row(), $"Skip to {target}")) Clock.SkipTo(_skipToHour);
        }

        var presets = Weather.Presets();
        if (presets.Count == 0) return;
        _y += SectionGap;
        Heading("Weather", Weather.Held ? "Held" : "Follows the forecast");
        for (int first = 0; first < presets.Count; first += WeatherPerRow)
        {
            var cells = Columns(WeatherPerRow);
            for (int i = first; i < presets.Count && i < first + WeatherPerRow; i++)
                if (Button(cells[i - first], presets[i].Name)) Weather.Set(presets[i]);
        }
        if (Weather.Held && Button(Row(), "Follow the forecast again")) Weather.FollowForecast();
    }

    private void DrawTeleport()
    {
        if (Teleports.Pending != null)
        {
            GUI.Label(Row(), $"Travelling to {Teleports.Pending.Name}...", Theme.Label);
            return;
        }

        Heading("Saved spots", $"You are in {Teleports.AreaName}");
        var top = Row();
        const float renameWidth = 84f;
        if (Button(new Rect(top.x, top.y, top.width - renameWidth - RowGap, top.height), "Save current position")) Teleports.SaveCurrent();
        if (Button(new Rect(top.xMax - renameWidth, top.y, renameWidth, top.height), "Rename", _renameMode)) _renameMode = !_renameMode;

        DrawSpots();

        _y += SectionGap;
        int unlocked = Teleports.UnlockedAreaCount, total = Teleports.AreaCount;
        Heading("Areas", $"{unlocked} of {total} unlocked");
        if (unlocked < total && Button(Row(), "Unlock all areas")) Teleports.UnlockAllAreas();
    }

    private void DrawSpots()
    {
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
            var name = new Rect(x, y, innerWidth - removeWidth - RowGap, RowHeight);
            if (spots[i] == _renaming)
            {
                _renameText = TextField(name, _renameText, RenameField, "Name");
                // Enter, Escape or a click elsewhere takes the keyboard away: that ends the edit.
                if (_focusedField != RenameField)
                {
                    Teleports.Rename(_renaming, _renameText);
                    _renaming = null;
                }
            }
            else
            {
                // Spots in other areas need a trip there first; the game shows its usual travel transition.
                string label = Teleports.IsHere(spots[i]) ? spots[i].Name : spots[i].Name + "  (travel)";
                if (Button(name, label))
                {
                    if (_renameMode)
                    {
                        _renaming = spots[i];
                        _renameText = spots[i].Name;
                        _focusedField = RenameField;
                    }
                    else go = spots[i];
                }
            }
            if (Button(new Rect(x + innerWidth - removeWidth, y, removeWidth, RowHeight), "X")) remove = spots[i];
        }
        if (scrolls) GUI.EndScrollView();
        if (_renameMode) GUI.Label(Row(20f), "Click a spot to rename it, Enter to finish.", Theme.Hint);

        // Applied after the loop, both change what the list shows.
        if (remove != null)
        {
            if (remove == _renaming) _renaming = null;
            Teleports.Remove(remove);
        }
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
