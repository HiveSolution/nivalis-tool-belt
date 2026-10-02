using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using Nivalis;
using UnityEngine;

namespace NivalisToolBelt;

internal sealed class Spot
{
    public string Scene;
    public string Name;
    public Vector3 Position;
    public float Yaw;
}

/// <summary>
/// Saved positions. Coordinates only mean something inside the area (scene) they were saved in,
/// so every spot remembers its scene; going to one in another area travels there first.
/// </summary>
internal static class Teleports
{
    // One spot per line: scene, name, x, y, z, yaw, separated by tabs. Names can be edited by hand.
    private static readonly string FilePath = Path.Combine(Paths.ConfigPath, Plugin.Guid + ".spots.txt");

    private static readonly List<Spot> All = new List<Spot>();
    private static readonly List<Spot> ListCache = new List<Spot>();
    private static bool _loaded;

    // What the game reports while it is between two areas.
    private const string NoScene = "None";
    private const float TravelTimeout = 120f;
    private const float RefusedTimeout = 5f;
    private const float SettleTime = 0.5f;
    private const float WatchTime = 3f;
    private const float JumpDistance = 5f;
    private static Spot _pending;
    private static string _origin;
    private static bool _leftOrigin;
    private static float _pendingSince;
    private static float _arrivedSince;
    private static Spot _placed;
    private static float _watchUntil;
    private static Vector3 _lastPosition;

    private static GameSceneManager Scenes =>
        Singleton<GameSceneManager>.InstanceExist() ? Singleton<GameSceneManager>.Instance : null;

    /// <summary>Scene name of the current area, or null while there is none (title screen, between areas).</summary>
    public static string CurrentScene
    {
        get
        {
            string scene = Scenes?.CurrentGameplaySceneName;
            return string.IsNullOrEmpty(scene) || scene == NoScene ? null : scene;
        }
    }

    public static string AreaName
    {
        get
        {
            var location = Scenes?.CurrentWorldLocation;
            string name = location == null ? null : location.DisplayName;
            return string.IsNullOrEmpty(name) ? CurrentScene ?? "?" : name;
        }
    }

    /// <summary>All spots, the current area's first. The list is reused, do not keep it.</summary>
    public static List<Spot> Listed
    {
        get
        {
            EnsureLoaded();
            ListCache.Clear();
            string scene = CurrentScene;
            foreach (var spot in All)
                if (spot.Scene == scene) ListCache.Add(spot);
            foreach (var spot in All)
                if (spot.Scene != scene) ListCache.Add(spot);
            return ListCache;
        }
    }

    public static bool IsHere(Spot spot) => spot.Scene == CurrentScene;

    /// <summary>The spot the player is travelling to, while the game loads its area.</summary>
    public static Spot Pending => _pending;

    public static void SaveCurrent()
    {
        var controller = Sandbox.Controller;
        string scene = CurrentScene;
        if (controller == null || string.IsNullOrEmpty(scene)) return;
        EnsureLoaded();

        string area = AreaName;
        int number = 1;
        while (All.Exists(s => s.Scene == scene && s.Name == $"{area} {number}")) number++;

        All.Add(new Spot
        {
            Scene = scene,
            Name = $"{area} {number}",
            Position = controller.transform.position,
            Yaw = controller.Rotation.eulerAngles.y,
        });
        Write();
    }

    /// <summary>Teleports to the spot. Returns true if that needs a trip to another area first.</summary>
    public static bool Go(Spot spot)
    {
        if (Sandbox.Controller == null || _pending != null) return false;
        if (IsHere(spot))
        {
            Arrive(spot);
            return false;
        }
        return Travel(spot);
    }

    // Uses the game's own area travel (the one behind the area exits) to the destination's default
    // arrival point; Tick() moves the player on to the spot once the game has finished arriving.
    private static bool Travel(Spot spot)
    {
        var portal = FindArrivalPortal(spot.Scene);
        if (portal == null)
        {
            Plugin.Logger.LogWarning($"No area found for scene '{spot.Scene}', cannot travel to {spot.Name}");
            return false;
        }
        if (!Singleton<TravelManager>.InstanceExist()) return false;
        var travel = Singleton<TravelManager>.Instance;
        // The game also refuses travel during the curfew. That one is a gameplay rule, not a sign that
        // the player is busy (cutscene, dialogue, end-of-day screens), so the tool belt goes anyway.
        bool curfewOnly = Clock.InCurfew && Sandbox.Controller.CanMove;
        if (!travel.CanTravel && !curfewOnly)
        {
            Plugin.Logger.LogInfo("The game does not allow travelling right now");
            return false;
        }
        travel.RequestTravel(portal, false, false);
        _pending = spot;
        _origin = CurrentScene;
        _leftOrigin = false;
        _pendingSince = Time.realtimeSinceStartup;
        _arrivedSince = -1f;
        Plugin.Logger.LogInfo($"Travelling to {spot.Name}");
        return true;
    }

    private static PortalKey FindArrivalPortal(string scene)
    {
        var scenes = Scenes;
        if (scenes == null) return null;
        foreach (var location in scenes.worldLocations)
            if (location != null && location.SceneName == scene) return location.DefaultLocation;
        return null;
    }

    public static void Tick()
    {
        float now = Time.realtimeSinceStartup;
        var controller = Sandbox.Controller;

        if (_pending == null)
        {
            // If the game's arrival sequence puts the player at the area entrance after all,
            // that shows as a jump right after the teleport; go back to the spot.
            if (_placed == null) return;
            if (now > _watchUntil || controller == null || !IsHere(_placed))
            {
                _placed = null;
                return;
            }
            var position = controller.transform.position;
            if (Vector3.Distance(position, _lastPosition) > JumpDistance) Arrive(_placed);
            else _lastPosition = position;
            return;
        }

        if (CurrentScene != _origin) _leftOrigin = true;
        if (now - _pendingSince > (_leftOrigin ? TravelTimeout : RefusedTimeout))
        {
            Plugin.Logger.LogWarning(_leftOrigin ? $"Gave up travelling to {_pending.Name}" : $"The game refused the trip to {_pending.Name}");
            _pending = null;
            return;
        }

        // Between areas the player drops freely; standing still in the destination means the game
        // has put them at its arrival point and handed control back.
        bool arrived = IsHere(_pending) && !Scenes.IsLoading && controller != null && controller.CanMove && controller.isGrounded;
        if (!arrived)
        {
            _arrivedSince = -1f;
            return;
        }
        if (_arrivedSince < 0f) _arrivedSince = now;
        if (now - _arrivedSince < SettleTime) return;

        _placed = _pending;
        _watchUntil = now + WatchTime;
        _pending = null;
        Arrive(_placed);
    }

    private static void Arrive(Spot spot)
    {
        var controller = Sandbox.Controller;
        // A spot saved in mid-air or inside geometry has nothing to stand on; arrive flying.
        if (!Sandbox.Fly && !controller.RaycastCheckGround(spot.Position)) Sandbox.Fly = true;
        controller.StopMovement();
        controller.TeleportPlayer(spot.Position, Quaternion.Euler(0f, spot.Yaw, 0f), false);
        _lastPosition = spot.Position;
    }

    public static void Remove(Spot spot)
    {
        if (All.Remove(spot)) Write();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(FilePath)) return;
        try
        {
            foreach (string line in File.ReadAllLines(FilePath))
            {
                string[] parts = line.Split('\t');
                if (parts.Length != 6) continue;
                All.Add(new Spot
                {
                    Scene = parts[0],
                    Name = parts[1],
                    Position = new Vector3(Parse(parts[2]), Parse(parts[3]), Parse(parts[4])),
                    Yaw = Parse(parts[5]),
                });
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Could not read {FilePath}: {e.Message}");
        }
    }

    private static void Write()
    {
        try
        {
            var lines = new List<string>(All.Count);
            foreach (var spot in All)
                lines.Add(string.Join("\t", spot.Scene, spot.Name, Format(spot.Position.x), Format(spot.Position.y), Format(spot.Position.z), Format(spot.Yaw)));
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Could not write {FilePath}: {e.Message}");
        }
    }

    private static float Parse(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
