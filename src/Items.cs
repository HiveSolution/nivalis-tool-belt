using System;
using System.Collections.Generic;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.InventorySystem;
using UnityEngine;

namespace NivalisToolBelt;

internal sealed class ItemEntry
{
    public ItemType Type;
    public string Name;
    public string SearchKey;
    /// <summary>How many the player holds; only kept current by <see cref="Items.FindOwned"/>.</summary>
    public int Count;
}

/// <summary>Adds items from the game's item database to the player's inventory, and takes them out again.</summary>
internal static class Items
{
    private const float OwnedRefreshInterval = 1f;

    private static readonly List<ItemEntry> All = new List<ItemEntry>();
    private static readonly List<ItemEntry> Matches = new List<ItemEntry>();
    private static readonly List<ItemEntry> Owned = new List<ItemEntry>();
    private static ItemDatabase _source;
    private static string _query;
    private static string _ownedQuery;
    private static float _nextOwnedRefresh;

    /// <summary>What the last <see cref="Add"/> or <see cref="Remove"/> did, for the menu to show.</summary>
    public static string LastResult { get; private set; }

    private static PlayerInventory Inventory =>
        Singleton<PlayerManager>.InstanceExist() ? Singleton<PlayerManager>.Instance.LocalPlayer?.Inventory : null;

    /// <summary>Items whose name contains the text, sorted by name. The list is reused, do not keep it.</summary>
    public static List<ItemEntry> Find(string query)
    {
        if (!EnsureLoaded())
        {
            Matches.Clear();
            return Matches;
        }
        query = query.Trim().ToLowerInvariant();
        if (query == _query) return Matches;
        _query = query;
        Matches.Clear();
        foreach (var entry in All)
            if (query.Length == 0 || entry.SearchKey.Contains(query)) Matches.Add(entry);
        return Matches;
    }

    /// <summary>Like <see cref="Find"/>, but only what the player holds, with <see cref="ItemEntry.Count"/> filled in.</summary>
    public static List<ItemEntry> FindOwned(string query)
    {
        var inventory = Inventory;
        if (!EnsureLoaded() || inventory == null)
        {
            Owned.Clear();
            return Owned;
        }
        // Counting means one question to the game per catalogue entry, so not every frame.
        query = query.Trim().ToLowerInvariant();
        if (query == _ownedQuery && Time.realtimeSinceStartup < _nextOwnedRefresh) return Owned;
        _ownedQuery = query;
        _nextOwnedRefresh = Time.realtimeSinceStartup + OwnedRefreshInterval;
        Owned.Clear();
        var items = inventory.Items;
        foreach (var entry in All)
        {
            if (query.Length > 0 && !entry.SearchKey.Contains(query)) continue;
            entry.Count = items.GetItemCount(entry.Type);
            if (entry.Count > 0) Owned.Add(entry);
        }
        return Owned;
    }

    // Asking the game for every item's name is slow, so the catalogue is read once per database.
    private static bool EnsureLoaded()
    {
        if (!Singleton<ItemDatabase>.InstanceExist()) return false;
        var database = Singleton<ItemDatabase>.Instance;
        if (database == _source) return true;
        _source = database;
        _query = null;
        _ownedQuery = null;
        Load(database);
        return true;
    }

    private static void Load(ItemDatabase database)
    {
        All.Clear();
        var items = database._allItems;
        for (int i = 0; i < items.Length; i++)
        {
            var item = items[i];
            // Items the game does not let the player store are internal ones (no name, no icon).
            if (item == null || !item.IsPlayerStorable) continue;
            string name = item.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;
            All.Add(new ItemEntry { Type = item, Name = name });
        }
        TellVariantsApart();
        foreach (var entry in All) entry.SearchKey = entry.Name.ToLowerInvariant();
        All.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    // A good third of the catalogue (mostly furniture) shares its display name with other variants.
    // Add what sets them apart: the furniture style if that differs within the group, otherwise
    // a number, in the order of the game's own asset names so that it stays the same every time.
    private static void TellVariantsApart()
    {
        var groups = new Dictionary<string, List<ItemEntry>>();
        foreach (var entry in All)
        {
            if (!groups.TryGetValue(entry.Name, out var group)) groups[entry.Name] = group = new List<ItemEntry>();
            group.Add(entry);
        }
        foreach (var group in groups.Values)
        {
            if (group.Count < 2) continue;
            var styles = new HashSet<string>();
            foreach (var entry in group) styles.Add(entry.Type.furnitureStyle.ToString());
            if (styles.Count == group.Count)
            {
                foreach (var entry in group) entry.Name += $" ({entry.Type.furnitureStyle})";
                continue;
            }
            group.Sort((a, b) => string.Compare(a.Type.name, b.Type.name, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < group.Count; i++) group[i].Name += $" #{i + 1}";
        }
    }

    public static void Add(ItemEntry entry, int amount)
    {
        var inventory = Inventory;
        if (inventory == null) return;

        // The inventory can refuse (it has a capacity), so report what actually arrived.
        int before = inventory.Items.GetItemCount(entry.Type);
        inventory.AddItem(entry.Type, amount);
        int added = inventory.Items.GetItemCount(entry.Type) - before;
        LastResult = added == amount ? $"Added {added} x {entry.Name}"
            : added > 0 ? $"Only {added} x {entry.Name} fit"
            : $"{entry.Name} did not fit";
        Plugin.Logger.LogInfo(LastResult);
        _nextOwnedRefresh = 0f;
    }

    /// <summary>Takes items out of the inventory for good. An amount of zero or less means all of them.</summary>
    public static void Remove(ItemEntry entry, int amount)
    {
        var inventory = Inventory;
        if (inventory == null) return;

        var items = inventory.Items;
        int before = items.GetItemCount(entry.Type);
        if (amount <= 0 || amount >= before) items.TakeAllByType(entry.Type);
        else for (int i = 0; i < amount; i++) items.TakeOne(entry.Type);
        int removed = before - items.GetItemCount(entry.Type);
        LastResult = removed > 0 ? $"Removed {removed} x {entry.Name}" : $"Could not remove {entry.Name}";
        Plugin.Logger.LogInfo(LastResult);
        _nextOwnedRefresh = 0f;
    }
}
