using System;
using System.Collections.Generic;
using Nivalis;
using Nivalis.GhostSystem.Ai;
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
    private const float TickInterval = 1f;

    private sealed class Storage
    {
        public int Normal, Fridge, AppliedNormal, AppliedFridge;
    }

    private static readonly List<VenueEntry> All = new List<VenueEntry>();
    private static readonly Dictionary<IntPtr, Storage> Storages = new Dictionary<IntPtr, Storage>();
    private static readonly Dictionary<IntPtr, HappinessBreakdown> OriginalMoods = new Dictionary<IntPtr, HappinessBreakdown>();
    private static PropertyManager _source;
    private static float _nextTick;

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
        // A fresh manager means a load: the game has rebuilt every venue with its plain capacities.
        Storages.Clear();
        OriginalMoods.Clear();
        HeldHappiness = null;
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

    /// <summary>How many times their normal storage space the player's venues have. Kept in the config file.</summary>
    public static int StorageMultiplier
    {
        get => Settings.StorageMultiplier.Value;
        set
        {
            value = Mathf.Clamp(value, 1, Settings.MaxStorageMultiplier);
            if (value == Settings.StorageMultiplier.Value) return;
            Settings.StorageMultiplier.Value = value;
            ApplyStorage();
            Plugin.Logger.LogInfo($"Venue storage x{value}");
        }
    }

    /// <summary>The mood all staff of the player's venues are kept in, or null while the game decides.</summary>
    public static Happiness? HeldHappiness { get; private set; }

    public static void HoldStaffHappiness(Happiness? mood)
    {
        HeldHappiness = mood;
        if (mood != null) ApplyHappiness();
        else RestoreHappiness();
        Plugin.Logger.LogInfo(mood == null ? "Staff happiness is up to the game again" : $"Staff happiness held at {mood}");
    }

    public static void Tick()
    {
        if (Time.realtimeSinceStartup < _nextTick) return;
        _nextTick = Time.realtimeSinceStartup + TickInterval;
        if (!Sandbox.InGame) return;
        ApplyStorage();
        if (HeldHappiness != null) ApplyHappiness();
    }

    // The game adds to and subtracts from a venue's capacity as storage furniture is placed and
    // removed, so the multiplied value cannot simply be written once. Remember the plain capacity
    // per venue, read whatever the game changed since as a change in plain units, and re-apply.
    private static void ApplyStorage()
    {
        int multiplier = StorageMultiplier;
        foreach (var entry in List())
        {
            if (!IsMine(entry)) continue;
            // The capacity lives in each container's restriction. (The inventory's own capacity
            // properties take a nullable number, which does not survive the trip into the game.)
            var inventory = entry.Venue.RuntimeData.JointInventory;
            var normal = inventory.NormalInventory?.Restriction?.MaxItems;
            var fridge = inventory.RefridgeratedInventory?.Restriction?.MaxItems;
            // No value means unlimited: nothing to multiply.
            if (normal == null || fridge == null || !normal.HasValue || !fridge.HasValue) continue;

            if (!Storages.TryGetValue(inventory.Pointer, out var storage))
            {
                if (multiplier == 1) continue;
                storage = new Storage { Normal = normal.Value, Fridge = fridge.Value, AppliedNormal = normal.Value, AppliedFridge = fridge.Value };
                Storages[inventory.Pointer] = storage;
            }
            storage.Normal = Mathf.Max(0, storage.Normal + normal.Value - storage.AppliedNormal);
            storage.Fridge = Mathf.Max(0, storage.Fridge + fridge.Value - storage.AppliedFridge);
            storage.AppliedNormal = storage.Normal * multiplier;
            storage.AppliedFridge = storage.Fridge * multiplier;
            if (normal.Value != storage.AppliedNormal) normal._value = storage.AppliedNormal;
            if (fridge.Value != storage.AppliedFridge) fridge._value = storage.AppliedFridge;
        }
    }

    // The game recalculates each worker's mood from wage and skill when it pays them, so a mood
    // only lasts if it is written again.
    private static void ApplyHappiness()
    {
        var mood = HeldHappiness.Value;
        float value = mood == Happiness.Sad ? 0f : mood == Happiness.Normal ? 0.5f : 1f;
        foreach (var entry in List())
        {
            if (!IsMine(entry)) continue;
            var staff = entry.Venue.RuntimeData.staff;
            for (int i = 0; i < staff.Count; i++)
            {
                var data = staff[i].RuntimeData;
                if (data == null) continue;
                var current = data.WorkSatisfaction;
                if (!OriginalMoods.ContainsKey(data.Pointer)) OriginalMoods[data.Pointer] = current;
                if (current.Happiness != mood || current.Value != value)
                {
                    current.Happiness = mood;
                    current.Value = value;
                    data.WorkSatisfaction = current;
                }
                // The urge to quit builds up while unhappy; clear it unless that is what was asked for.
                if (mood != Happiness.Sad && data.WorkQuit > 0f) data.WorkQuit = 0f;
            }
        }
    }

    // Gives everyone back the mood they had before it was held, rather than leaving them at the
    // held one until the game next recalculates.
    private static void RestoreHappiness()
    {
        foreach (var entry in List())
        {
            if (!IsMine(entry)) continue;
            var staff = entry.Venue.RuntimeData.staff;
            for (int i = 0; i < staff.Count; i++)
            {
                var data = staff[i].RuntimeData;
                if (data != null && OriginalMoods.TryGetValue(data.Pointer, out var original)) data.WorkSatisfaction = original;
            }
        }
        OriginalMoods.Clear();
    }

    public static int StaffCount
    {
        get
        {
            int count = 0;
            foreach (var entry in List())
                if (IsMine(entry)) count += entry.Venue.RuntimeData.staff.Count;
            return count;
        }
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
