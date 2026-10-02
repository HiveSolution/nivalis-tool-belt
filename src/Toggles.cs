using Nivalis;
using Nivalis.Boat;
using UnityEngine;

namespace NivalisToolBelt;

/// <summary>Switches that keep something in a state for as long as they are on. All are off after a restart.</summary>
internal static class Toggles
{
    private const float SlowInterval = 0.5f;

    private static bool _undetected;
    private static float _nextSlowTick;
    private static float _fuelSeen;

    /// <summary>While on, the curfew's security (cameras, drones) does not notice the player.</summary>
    public static bool Undetected
    {
        get => _undetected;
        set
        {
            if (_undetected == value) return;
            _undetected = value;
            // The game arms its security when the curfew starts; hand it back armed if one is running.
            if (!value && Singleton<CurfewManager>.InstanceExist() && CurfewManager.IsCurfewInEffect)
                Singleton<CurfewManager>.Instance._isCurfewSecurityEnabled = true;
            Plugin.Logger.LogInfo($"Undetected during curfew {(value ? "on" : "off")}");
        }
    }

    /// <summary>While on, the boat's tank stays full.</summary>
    public static bool BoatFuel { get; set; }

    /// <summary>While on, everything planted in the player's greenhouses is ready to harvest at once.</summary>
    public static bool InstantGrowth { get; set; }

    public static void Tick()
    {
        if (_undetected) StayUndetected();

        if (Time.realtimeSinceStartup < _nextSlowTick) return;
        _nextSlowTick = Time.realtimeSinceStartup + SlowInterval;
        if (!Sandbox.InGame) return;
        TrackBoat();
        if (InstantGrowth) GrowEverything();
    }

    // Every frame: detection builds up continuously, and a single noticed frame is enough for
    // the game to show its "you've been spotted" message.
    private static void StayUndetected()
    {
        if (!Singleton<CurfewManager>.InstanceExist()) return;
        var curfew = Singleton<CurfewManager>.Instance;
        if (curfew._isCurfewSecurityEnabled) curfew._isCurfewSecurityEnabled = false;
        if (curfew.Awarness > 0f) curfew.ResetSecurityDetection();
    }

    // The tank size is only known while the boat is in the same area as the player. Elsewhere the
    // fullest it has been seen has to do, so that is noted even while the switch is off.
    private static void TrackBoat()
    {
        var boat = BoatGhost.FindBoat();
        if (boat == null) return;
        var controller = BoatController.Instance;
        float full = controller != null ? controller.FuelCapacity : Mathf.Max(_fuelSeen, boat.Fuel);
        _fuelSeen = full;
        if (BoatFuel && boat.Fuel < full) boat.Fuel = full;
    }

    private static void GrowEverything()
    {
        if (!Singleton<GreenhouseManager>.InstanceExist()) return;
        var manager = Singleton<GreenhouseManager>.Instance;
        foreach (var entry in Estates.Greenhouses())
        {
            if (!Estates.IsMine(entry)) continue;
            var modules = manager.GetArea(entry.Greenhouse).modules;
            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i].Value;
                // Growth runs from 0 to 1; at 1 the crop can be harvested.
                if (module != null && module.Planted && module.growth < 1f) module.Growth = 1f;
            }
        }
    }
}
