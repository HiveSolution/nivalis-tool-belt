using System.Collections.Generic;
using Nivalis;

namespace NivalisToolBelt;

internal sealed class WeatherPreset
{
    public WeatherForecastType Type;
    public string Name;
}

/// <summary>Switches the weather to one of the game's own forecast presets.</summary>
internal static class Weather
{
    private static readonly List<WeatherPreset> PresetList = new List<WeatherPreset>();
    private static WeatherForecastController _source;

    private static WeatherForecastController Controller =>
        Singleton<WeatherForecastController>.InstanceExist() ? Singleton<WeatherForecastController>.Instance : null;

    /// <summary>The game's weather types. The list is reused, do not keep it.</summary>
    public static List<WeatherPreset> Presets()
    {
        var controller = Controller;
        if (controller == null)
        {
            PresetList.Clear();
            return PresetList;
        }
        if (controller != _source)
        {
            _source = controller;
            PresetList.Clear();
            var types = controller.forecastTypes;
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == null) continue;
                string name = types[i].DisplayName;
                PresetList.Add(new WeatherPreset { Type = types[i], Name = string.IsNullOrWhiteSpace(name) ? types[i].name : name });
            }
        }
        return PresetList;
    }

    /// <summary>True while the weather no longer follows the game's forecast.</summary>
    public static bool Held
    {
        get
        {
            var controller = Controller;
            return controller != null && controller.stopTimedWeatherProgression;
        }
    }

    /// <summary>Sets the weather and keeps it until <see cref="FollowForecast"/>.</summary>
    public static void Set(WeatherPreset preset)
    {
        var controller = Controller;
        if (controller == null) return;
        controller.Dev_SetWeatherToPreset(preset.Type);
        controller.stopTimedWeatherProgression = true;
        Plugin.Logger.LogInfo($"Weather set to {preset.Name}");
    }

    public static void FollowForecast()
    {
        var controller = Controller;
        if (controller == null) return;
        controller.stopTimedWeatherProgression = false;
        controller.ForceUpdateLocalWeatherManager();
        Plugin.Logger.LogInfo("Weather follows the forecast again");
    }
}
