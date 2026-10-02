using System;
using System.Collections.Generic;
using Nivalis;
using Nivalis.CraftingSystem;
using Nivalis.InventorySystem;

namespace NivalisToolBelt;

internal sealed class ItemEntry
{
    public ItemType Type;
    public string Name;
    public string SearchKey;
}

/// <summary>Adds items from the game's item database to the player's inventory.</summary>
internal static class Items
{
    private static readonly List<ItemEntry> All = new List<ItemEntry>();
    private static readonly List<ItemEntry> Matches = new List<ItemEntry>();
    private static ItemDatabase _source;
    private static string _query;

    /// <summary>What the last <see cref="Add"/> did, for the menu to show.</summary>
    public static string LastResult { get; private set; }

    /// <summary>Items whose name contains the text, sorted by name. The list is reused, do not keep it.</summary>
    public static List<ItemEntry> Find(string query)
    {
        if (!Singleton<ItemDatabase>.InstanceExist())
        {
            Matches.Clear();
            return Matches;
        }
        // Asking the game for every item's name is slow, so the catalogue is read once per database.
        var database = Singleton<ItemDatabase>.Instance;
        if (database != _source)
        {
            _source = database;
            _query = null;
            Load(database);
        }

        query = query.Trim().ToLowerInvariant();
        if (query == _query) return Matches;
        _query = query;
        Matches.Clear();
        foreach (var entry in All)
            if (query.Length == 0 || entry.SearchKey.Contains(query)) Matches.Add(entry);
        return Matches;
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
            All.Add(new ItemEntry { Type = item, Name = name, SearchKey = name.ToLowerInvariant() });
        }
        All.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    public static void Add(ItemEntry entry, int amount)
    {
        if (!Singleton<PlayerManager>.InstanceExist()) return;
        var inventory = Singleton<PlayerManager>.Instance.LocalPlayer?.Inventory;
        if (inventory == null) return;

        // The inventory can refuse (it has a capacity), so report what actually arrived.
        int before = inventory.Items.GetItemCount(entry.Type);
        inventory.AddItem(entry.Type, amount);
        int added = inventory.Items.GetItemCount(entry.Type) - before;
        LastResult = added == amount ? $"Added {added} x {entry.Name}"
            : added > 0 ? $"Only {added} x {entry.Name} fit"
            : $"{entry.Name} did not fit";
        Plugin.Logger.LogInfo(LastResult);
    }
}
