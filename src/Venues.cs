using System;
using System.Collections.Generic;
using Nivalis;
using Nivalis.GhostSystem.CustomerLoop;
using UnityEngine;

namespace NivalisToolBelt;

internal sealed class VenueEntry
{
    public Venue Venue;
    public string Name;
}

/// <summary>
/// Venue level and ownership, through the game's property manager so that its own bookkeeping
/// (owner lists, rent, events) runs. Only the player's venues and the ones the game marks as
/// acquirable are offered; venues run by other owners are left alone.
/// </summary>
internal static class Venues
{
    private static readonly List<VenueEntry> All = new List<VenueEntry>();
    private static PropertyManager _source;

    private static PlayerManager.Player Player =>
        Singleton<PlayerManager>.InstanceExist() ? Singleton<PlayerManager>.Instance.LocalPlayer : null;

    /// <summary>The player's venues first, then the ones that can be taken over. The list is reused, do not keep it.</summary>
    public static List<VenueEntry> List()
    {
        if (!Singleton<PropertyManager>.InstanceExist())
        {
            All.Clear();
            return All;
        }
        var manager = Singleton<PropertyManager>.Instance;
        if (manager != _source)
        {
            _source = manager;
            Load(manager);
        }
        return All;
    }

    private static void Load(PropertyManager manager)
    {
        All.Clear();
        var states = manager._propertyStates;
        for (int i = 0; i < states.Count; i++)
        {
            var venue = states[i].Property.TryCast<Venue>();
            if (venue == null || !(venue.IsAcquireable || venue.PlayerOwned)) continue;
            All.Add(new VenueEntry { Venue = venue, Name = venue.GetName() });
        }
        Sort();
    }

    private static void Sort()
    {
        All.Sort((a, b) =>
        {
            bool mineA = a.Venue.PlayerOwned, mineB = b.Venue.PlayerOwned;
            return mineA != mineB ? (mineA ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    public static bool IsMine(VenueEntry entry) => entry.Venue.PlayerOwned;

    /// <summary>The venue the game starts the player with; the story hangs on it, so it cannot be given up here.</summary>
    public static bool IsStartingVenue(VenueEntry entry) => entry.Venue.StartingVenue;

    public static int MaxLevel(VenueEntry entry) => entry.Venue.LevellingData.Length;

    public static int GetLevel(VenueEntry entry) => entry.Venue.RuntimeData.currentLevel;

    public static void SetLevel(VenueEntry entry, int level)
    {
        level = Mathf.Clamp(level, 1, MaxLevel(entry));
        if (level == GetLevel(entry)) return;
        Singleton<PropertyManager>.Instance.SetVenueLevel(entry.Venue, level);
        Plugin.Logger.LogInfo($"{entry.Name} set to level {GetLevel(entry)}");
    }

    public static void TakeOver(VenueEntry entry)
    {
        var player = Player;
        if (player == null || IsMine(entry)) return;
        Singleton<PropertyManager>.Instance.AddOwnedProperty(player.Cast<IPropertyOwner>(), entry.Venue, OwnershipType.Purchase);
        Plugin.Logger.LogInfo($"Took over {entry.Name}: {(IsMine(entry) ? "now yours" : "the game did not accept it")}");
        Sort();
    }

    public static void GiveUp(VenueEntry entry)
    {
        var player = Player;
        if (player == null || !IsMine(entry) || IsStartingVenue(entry)) return;
        Singleton<PropertyManager>.Instance.RemoveOwnedProperty(player.Cast<IPropertyOwner>(), entry.Venue);
        Plugin.Logger.LogInfo($"Gave up {entry.Name}: {(IsMine(entry) ? "the game kept it yours" : "no longer yours")}");
        Sort();
    }
}
