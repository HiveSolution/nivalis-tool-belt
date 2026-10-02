using System;
using System.Collections.Generic;
using Nivalis;
using Nivalis.Apartment;
using Nivalis.GhostSystem.CustomerLoop;

namespace NivalisToolBelt;

internal sealed class EstateEntry
{
    public BaseProperty Property;
    public Apartment Apartment;
    public Greenhouse Greenhouse;
    public string Name;
}

/// <summary>
/// Apartments and greenhouses. Each kind has its own manager in the game with its own renting and
/// buying calls; those are used as they are, and whatever they charge or pay out is undone, so
/// everything here is free.
/// </summary>
internal static class Estates
{
    private static readonly List<EstateEntry> ApartmentList = new List<EstateEntry>();
    private static readonly List<EstateEntry> GreenhouseList = new List<EstateEntry>();
    private static PropertyManager _source;

    private static PlayerManager.Player Player =>
        Singleton<PlayerManager>.InstanceExist() ? Singleton<PlayerManager>.Instance.LocalPlayer : null;

    /// <summary>The apartments meant for the player (no shelters or story locations), the player's own first. The list is reused, do not keep it.</summary>
    public static List<EstateEntry> Apartments()
    {
        EnsureLoaded();
        return ApartmentList;
    }

    /// <summary>Every greenhouse, the player's own first. The list is reused, do not keep it.</summary>
    public static List<EstateEntry> Greenhouses()
    {
        EnsureLoaded();
        return GreenhouseList;
    }

    // The apartment and greenhouse managers also carry assets the game never places in the world
    // (same names, never ownable), so the lists come from the property manager's registered ones.
    private static void EnsureLoaded()
    {
        if (!Singleton<PropertyManager>.InstanceExist())
        {
            _source = null;
            ApartmentList.Clear();
            GreenhouseList.Clear();
            return;
        }
        var manager = Singleton<PropertyManager>.Instance;
        if (manager == _source) return;
        _source = manager;
        ApartmentList.Clear();
        GreenhouseList.Clear();
        var states = manager._propertyStates;
        for (int i = 0; i < states.Count; i++)
        {
            var property = states[i].Property;
            var apartment = property.TryCast<Apartment>();
            if (apartment != null)
            {
                if (apartment.IsForPlayer && !apartment.Shelter)
                    ApartmentList.Add(new EstateEntry { Property = apartment, Apartment = apartment, Name = apartment.GetName() });
                continue;
            }
            var greenhouse = property.TryCast<Greenhouse>();
            if (greenhouse != null)
                GreenhouseList.Add(new EstateEntry { Property = greenhouse, Greenhouse = greenhouse, Name = greenhouse.GetName() });
        }
        Sort(ApartmentList);
        Sort(GreenhouseList);
    }

    private static void Sort(List<EstateEntry> list)
    {
        list.Sort((a, b) =>
        {
            bool mineA = IsMine(a), mineB = IsMine(b);
            return mineA != mineB ? (mineA ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    public static bool IsMine(EstateEntry entry) => entry.Property.PlayerOwned;

    /// <summary>None, Rent or Purchase.</summary>
    public static OwnershipType State(EstateEntry entry)
    {
        var player = Player;
        return player == null ? OwnershipType.None : player.GetPropertyOwnershipState(entry.Property);
    }

    /// <summary>Rent per day in cents, as the game charges it at the end of the day.</summary>
    public static int DailyRent(EstateEntry entry) => entry.Property.GetRentPrice();

    /// <summary>True for the player's only apartment: without a home there is nowhere to sleep.</summary>
    public static bool IsOnlyHome(EstateEntry entry)
    {
        if (entry.Apartment == null || !IsMine(entry)) return false;
        foreach (var other in Apartments())
            if (other != entry && IsMine(other)) return false;
        return true;
    }

    public static void Rent(EstateEntry entry) => Change(entry, "Rented", player =>
    {
        if (entry.Apartment != null) Singleton<ApartmentManager>.Instance.StartRenting(entry.Apartment, player, false);
        else Singleton<GreenhouseManager>.Instance.StartRenting(GreenhouseArea(entry), player);
    });

    public static void Buy(EstateEntry entry)
    {
        // The game refuses a purchase the player cannot afford, so the price is lent for the
        // moment of the purchase; Change() takes back whatever the balance moved by.
        int price = Singleton<PropertyManager>.Instance.GetBuyPrice(entry.Property);
        Change(entry, "Took over", player =>
        {
            if (entry.Apartment != null) Singleton<ApartmentManager>.Instance.Buy(entry.Apartment, player);
            else Singleton<GreenhouseManager>.Instance.Buy(GreenhouseArea(entry), player);
        }, 2 * price);
    }

    public static void GiveUp(EstateEntry entry)
    {
        if (!IsMine(entry) || IsOnlyHome(entry)) return;
        bool rented = State(entry) == OwnershipType.Rent;
        Change(entry, "Gave up", player =>
        {
            if (entry.Apartment != null)
            {
                if (rented) Singleton<ApartmentManager>.Instance.StopRenting(entry.Apartment, false);
                else Singleton<ApartmentManager>.Instance.Sell(entry.Apartment, player);
            }
            else if (rented) Singleton<GreenhouseManager>.Instance.StopRenting(GreenhouseArea(entry), player);
            else Singleton<GreenhouseManager>.Instance.Sell(GreenhouseArea(entry), player);
        });
    }

    private static GreenhouseAreaGhost GreenhouseArea(EstateEntry entry) =>
        Singleton<GreenhouseManager>.Instance.GetArea(entry.Greenhouse);

    private static void Change(EstateEntry entry, string verb, Action<PlayerManager.Player> action, int advance = 0)
    {
        var player = Player;
        if (player == null) return;
        int money = Character.MoneyCents;
        if (advance > 0) Character.AddMoney(advance);
        action(player);
        // The game's calls take the price or pay out the sale; put the balance back.
        int difference = money - Character.MoneyCents;
        if (difference != 0) Character.AddMoney(difference);
        Plugin.Logger.LogInfo($"{verb} {entry.Name}: now {State(entry)}, balance kept at {Character.MoneyCents / 100f:0.00}");
        Sort(entry.Apartment != null ? ApartmentList : GreenhouseList);
    }
}
