using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NivalisToolBelt;

/// <summary>
/// The menu's look, modelled on the game's own panels: dark plates with cut corners, gold headings,
/// the game's typeface. Everything is generated at runtime, the mod ships no assets.
/// </summary>
internal static class Theme
{
    // Sampled from the game's UI.
    private static readonly Color Gold = Rgb(217, 163, 95);
    private static readonly Color GoldBright = Rgb(236, 190, 128);
    private static readonly Color Text = Rgb(216, 210, 200);
    private static readonly Color Muted = Rgb(150, 144, 136);
    private static readonly Color Ink = Rgb(27, 25, 24);
    private static readonly Color Plate = new Color(27 / 255f, 25 / 255f, 24 / 255f, 0.96f);
    private static readonly Color Edge = Rgb(112, 98, 80);
    private static readonly Color ButtonFill = Rgb(62, 58, 54);
    private static readonly Color ButtonHover = Rgb(86, 80, 73);
    private static readonly Color Groove = Rgb(52, 49, 46);

    // The game's fonts are ordinary dynamic fonts, so IMGUI can use them as they are.
    private const string RegularFontName = "BarlowSemiCondensed-Medium";
    private const string BoldFontName = "BarlowSemiCondensed-ExtraBold";
    private const float FontRetryInterval = 2f;

    public const float CheckSize = 18f;

    public static GUIStyle Window, Title, Version, Label, LabelRight, Heading, Hint, Button, ButtonSelected, Field, FieldFocused, FieldEmpty;

    private static GUIStyle _empty, _thumb;
    private static GUISkin _skin;
    private static Texture2D _gold, _groove, _line, _checkOff, _checkOn;
    private static Font _regular, _bold;
    private static float _nextFontLookup;

    /// <summary>Call at the start of every OnGUI: builds the theme, and again if Unity threw its textures away.</summary>
    public static void Ensure()
    {
        if (_gold == null) Build();
        if ((_regular == null || _bold == null) && Time.realtimeSinceStartup >= _nextFontLookup)
        {
            _nextFontLookup = Time.realtimeSinceStartup + FontRetryInterval;
            FindFonts();
        }
    }

    /// <summary>The skin to draw the window's contents with; it carries the scrollbar look.</summary>
    public static GUISkin Skin => _skin;

    private static void Build()
    {
        _gold = Solid(Gold);
        _groove = Solid(Groove);
        _line = Solid(Edge);
        _checkOff = CutPlate(18, 18, Ink, Edge, 1, 0, 0);
        _checkOn = Check();

        var panel = CutPlate(40, 40, Plate, Edge, 1, 12, 12);
        Window = new GUIStyle { border = Sides(16) };
        Window.normal.background = panel;
        Window.onNormal.background = panel;

        Title = TextStyle(19, Gold, TextAnchor.MiddleLeft);
        Version = TextStyle(13, Muted, TextAnchor.MiddleRight);
        Label = TextStyle(15, Text, TextAnchor.MiddleLeft);
        LabelRight = TextStyle(15, Text, TextAnchor.MiddleRight);
        Heading = TextStyle(14, Gold, TextAnchor.MiddleLeft);
        Hint = TextStyle(13, Muted, TextAnchor.MiddleLeft);

        var up = CutPlate(24, 24, ButtonFill, ButtonFill, 0, 6, 0);
        var over = CutPlate(24, 24, ButtonHover, ButtonHover, 0, 6, 0);
        var lit = CutPlate(24, 24, Gold, Gold, 0, 6, 0);
        var litOver = CutPlate(24, 24, GoldBright, GoldBright, 0, 6, 0);

        Button = TextStyle(14, Text, TextAnchor.MiddleCenter);
        Button.border = Sides(8);
        Button.normal.background = up;
        Button.hover.background = over;
        Button.hover.textColor = Color.white;
        Button.active.background = lit;
        Button.active.textColor = Ink;

        ButtonSelected = new GUIStyle(Button);
        ButtonSelected.normal.background = lit;
        ButtonSelected.normal.textColor = Ink;
        ButtonSelected.hover.background = litOver;
        ButtonSelected.hover.textColor = Ink;

        Field = TextStyle(15, Text, TextAnchor.MiddleLeft);
        Field.padding = new RectOffset(8, 8, 0, 0);
        Field.border = Sides(4);
        Field.normal.background = CutPlate(12, 12, Ink, Edge, 1, 0, 0);
        FieldEmpty = new GUIStyle(Field);
        FieldEmpty.normal.textColor = Muted;
        FieldFocused = new GUIStyle(Field);
        FieldFocused.normal.background = CutPlate(12, 12, Ink, Gold, 1, 0, 0);
        FieldFocused.normal.textColor = Color.white;

        _empty = new GUIStyle();
        _thumb = new GUIStyle { fixedWidth = 10f, fixedHeight = 16f };
        _thumb.normal.background = Solid(Text);
        _thumb.hover.background = Solid(Color.white);
        _thumb.active.background = Solid(GoldBright);

        // Scroll views take their bar from the skin, so the list needs one of its own.
        _skin = Object.Instantiate(GUI.skin);
        _skin.hideFlags = HideFlags.HideAndDontSave;
        var bar = new GUIStyle { fixedWidth = 8f };
        bar.normal.background = _groove;
        var barThumb = new GUIStyle { fixedWidth = 8f };
        barThumb.normal.background = _gold;
        var none = new GUIStyle { fixedHeight = 0f };
        _skin.verticalScrollbar = bar;
        _skin.verticalScrollbarThumb = barThumb;
        _skin.verticalScrollbarUpButton = none;
        _skin.verticalScrollbarDownButton = none;

        ApplyFonts();
    }

    private static void FindFonts()
    {
        foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
        {
            if (font.name == RegularFontName) _regular = font;
            else if (font.name == BoldFontName) _bold = font;
        }
        ApplyFonts();
    }

    // Until the game has loaded its fonts (or if an update renames them) Unity's default font is used.
    private static void ApplyFonts()
    {
        foreach (var style in new[] { Label, LabelRight, Hint, Version, Field, FieldFocused, FieldEmpty })
            style.font = _regular;
        foreach (var style in new[] { Title, Heading, Button, ButtonSelected })
        {
            style.font = _bold;
            style.fontStyle = _bold == null ? FontStyle.Bold : FontStyle.Normal;
        }
    }

    public static bool Toggle(Rect row, bool value, string text)
    {
        bool clicked = GUI.Button(row, "", _empty);
        GUI.DrawTexture(new Rect(row.x, row.y + (row.height - CheckSize) / 2f, CheckSize, CheckSize), value ? _checkOn : _checkOff);
        GUI.Label(new Rect(row.x + CheckSize + 8f, row.y, row.width - CheckSize - 8f, row.height), text, Label);
        return clicked ? !value : value;
    }

    public static float Slider(Rect row, float value, float min, float max)
    {
        var groove = new Rect(row.x, row.y + row.height / 2f - 2f, row.width, 4f);
        GUI.DrawTexture(groove, _groove);
        GUI.DrawTexture(new Rect(groove.x, groove.y, groove.width * Mathf.InverseLerp(min, max, value), groove.height), _gold);
        return GUI.HorizontalSlider(row, value, min, max, _empty, _thumb);
    }

    /// <summary>A hairline across the window, like the rule under the game's panel headers.</summary>
    public static void Rule(Rect rect, bool gold) => GUI.DrawTexture(rect, gold ? _gold : _line);

    private static GUIStyle TextStyle(int size, Color color, TextAnchor anchor)
    {
        var style = new GUIStyle { fontSize = size, alignment = anchor, clipping = TextClipping.Clip, wordWrap = false };
        style.normal.textColor = color;
        return style;
    }

    private static RectOffset Sides(int size) => new RectOffset(size, size, size, size);

    private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

    private static Texture2D Solid(Color color) => Paint(1, 1, (x, y) => color);

    /// <summary>A plate with its top-right and bottom-left corners cut off, the shape the game frames its panels and buttons with.</summary>
    private static Texture2D CutPlate(int width, int height, Color fill, Color edge, int edgeWidth, int cutTopRight, int cutBottomLeft)
    {
        return Paint(width, height, (x, y) =>
        {
            int right = width - 1 - x, bottom = height - 1 - y;
            int topRight = right + y - cutTopRight;
            int bottomLeft = x + bottom - cutBottomLeft;
            if ((cutTopRight > 0 && topRight < 0) || (cutBottomLeft > 0 && bottomLeft < 0)) return Color.clear;
            bool onEdge = x < edgeWidth || y < edgeWidth || right < edgeWidth || bottom < edgeWidth
                || (cutTopRight > 0 && topRight < edgeWidth) || (cutBottomLeft > 0 && bottomLeft < edgeWidth);
            return onEdge ? edge : fill;
        });
    }

    private static Texture2D Check()
    {
        const int size = 18, inset = 4;
        return Paint(size, size, (x, y) =>
        {
            if (x == 0 || y == 0 || x == size - 1 || y == size - 1) return Gold;
            bool inner = x >= inset && y >= inset && x < size - inset && y < size - inset;
            return inner ? Gold : Ink;
        });
    }

    /// <summary>Paints a texture pixel by pixel; <paramref name="pixel"/> gets coordinates from the top left.</summary>
    private static Texture2D Paint(int width, int height, Func<int, int, Color> pixel)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            // Not referenced by any scene, so without this the game's asset clean-up on area change destroys it.
            hideFlags = HideFlags.HideAndDontSave,
        };
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                texture.SetPixel(x, height - 1 - y, pixel(x, y));
        texture.Apply();
        return texture;
    }
}
