using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP.Configuration;
using UnityEngine;

namespace NivalisToolBelt;

internal static class Settings
{
    public const int MaxStorageMultiplier = 10;
    public const float MinMenuScale = 0.5f;
    public const float MaxMenuScale = 3f;

    public static ConfigEntry<KeyboardShortcut> MenuKey;
    public static ConfigEntry<KeyboardShortcut> FlyKey;
    public static ConfigEntry<KeyboardShortcut> UndetectedKey;
    public static ConfigEntry<KeyboardShortcut> BoatFuelKey;
    public static ConfigEntry<KeyboardShortcut> GrowthKey;
    public static ConfigEntry<float> MaxSpeedMultiplier;
    public static ConfigEntry<float> MenuScale;
    public static ConfigEntry<int> StorageMultiplier;

    public static void Bind(ConfigFile config)
    {
        MenuKey = config.Bind("Hotkeys", "ToggleMenu", new KeyboardShortcut(KeyCode.F1),
            "Opens and closes the tool belt menu.");
        FlyKey = config.Bind("Hotkeys", "ToggleFly", new KeyboardShortcut(KeyCode.F2),
            "Toggles fly / ghost mode without opening the menu.");
        UndetectedKey = config.Bind("Hotkeys", "ToggleUndetected", new KeyboardShortcut(KeyCode.F3),
            "Toggles staying undetected during the curfew.");
        BoatFuelKey = config.Bind("Hotkeys", "ToggleBoatFuel", new KeyboardShortcut(KeyCode.F4),
            "Toggles unlimited boat fuel.");
        GrowthKey = config.Bind("Hotkeys", "ToggleInstantGrowth", new KeyboardShortcut(KeyCode.F6),
            "Toggles instant growth in your greenhouses.");
        MaxSpeedMultiplier = config.Bind("Menu", "MaxSpeedMultiplier", 5f,
            new ConfigDescription("Upper end of the movement speed slider.", new AcceptableValueRange<float>(2f, 20f)));
        MenuScale = config.Bind("Menu", "Scale", 1f,
            new ConfigDescription("Size of the menu, 1 is normal. Set from the menu.", new AcceptableValueRange<float>(MinMenuScale, MaxMenuScale)));
        // Kept between sessions, unlike the switches: items stored beyond the normal space depend on it.
        StorageMultiplier = config.Bind("Venues", "StorageMultiplier", 1,
            new ConfigDescription("How many times the normal storage space your venues have. Set from the menu.", new AcceptableValueRange<int>(1, MaxStorageMultiplier)));
    }
}
