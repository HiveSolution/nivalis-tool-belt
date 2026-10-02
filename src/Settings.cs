using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP.Configuration;
using UnityEngine;

namespace NivalisToolBelt;

internal static class Settings
{
    public static ConfigEntry<KeyboardShortcut> MenuKey;
    public static ConfigEntry<KeyboardShortcut> FlyKey;
    public static ConfigEntry<float> MaxSpeedMultiplier;

    public static void Bind(ConfigFile config)
    {
        MenuKey = config.Bind("Hotkeys", "ToggleMenu", new KeyboardShortcut(KeyCode.F1),
            "Opens and closes the tool belt menu.");
        FlyKey = config.Bind("Hotkeys", "ToggleFly", new KeyboardShortcut(KeyCode.F2),
            "Toggles fly / ghost mode without opening the menu.");
        MaxSpeedMultiplier = config.Bind("Menu", "MaxSpeedMultiplier", 5f,
            new ConfigDescription("Upper end of the movement speed slider.", new AcceptableValueRange<float>(2f, 20f)));
    }
}
