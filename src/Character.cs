using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Nivalis;
using Nivalis.Boat;
using Nivalis.SkillSystem;
using UnityEngine;

namespace NivalisToolBelt;

/// <summary>Tools for the player's own state: money, skill levels and the boat.</summary>
internal static class Character
{
    private const float BoatLookupInterval = 1f;

    private static BoatGhost _boat;
    private static float _nextBoatLookup;

    private static PlayerManager.Player Player =>
        Singleton<PlayerManager>.InstanceExist() ? Singleton<PlayerManager>.Instance.LocalPlayer : null;

    /// <summary>The game counts money in cents.</summary>
    public static int MoneyCents
    {
        get
        {
            var inventory = Player?.Inventory;
            return inventory == null ? 0 : inventory.Money;
        }
    }

    /// <summary>Adds (or with a negative amount removes) money without a receipt, so it stays out of the day's balance. Never goes below zero.</summary>
    public static void AddMoney(int cents)
    {
        var inventory = Player?.Inventory;
        if (inventory == null) return;
        if (cents < 0) cents = -Mathf.Min(-cents, inventory.Money);
        if (cents != 0) inventory.ChangeMoneyWithoutReceipt(cents);
    }

    /// <summary>Every skill the game defines. Null while no save is loaded.</summary>
    public static Il2CppReferenceArray<SkillDefinition> Skills =>
        Singleton<SkillLevelController>.InstanceExist() ? Singleton<SkillLevelController>.Instance._allSkills : null;

    /// <summary>Highest level of the skill, counted from zero like the game stores it.</summary>
    public static int MaxLevel(SkillDefinition skill) => skill.LevelCount - 1;

    /// <summary>Current level, counted from zero. The game displays it plus one.</summary>
    public static int GetLevel(SkillDefinition skill) =>
        Singleton<SkillLevelController>.Instance.GetPlayerSkillExperience(skill).CurrentLevel;

    /// <summary>Moves the skill to the start of the given level (counted from zero).</summary>
    public static void SetLevel(SkillDefinition skill, int level)
    {
        var player = Player;
        if (player == null) return;
        level = Mathf.Clamp(level, 0, MaxLevel(skill));
        if (level == GetLevel(skill)) return;
        var controller = Singleton<SkillLevelController>.Instance;
        // The game's experience table is indexed by the level number it displays (from 1).
        float target = skill.GetExperienceForLevel(level + 1);
        float current = controller.GetPlayerSkillExperience(skill).Experience;

        // Preferred: hand the game the experience difference, so its own level-up handling
        // (notification, unlocks) runs. A skill at its top level holds NaN experience, which
        // no difference can fix, and the game may not take experience away; then write the entry.
        string how = "experience";
        if (!float.IsNaN(current) && target != current) controller.AddExperience(skill, player.Id, target - current);
        if (GetLevel(skill) != level)
        {
            how = "direct write";
            var entries = controller._playerData.PerSkillExperience;
            var key = FindSkillKey(entries, skill);
            if (key == null)
            {
                Plugin.Logger.LogWarning($"No experience entry found for {skill.DisplayName}");
                return;
            }
            entries[key] = new SkillLevelController.PlayerSkillExperience { Experience = target, CurrentLevel = level };
        }
        Plugin.Logger.LogInfo($"{skill.DisplayName} set to level {GetLevel(skill) + 1} ({how})");
    }

    private static Il2CppSystem.Type FindSkillKey(Il2CppSystem.Collections.Generic.Dictionary<Il2CppSystem.Type, SkillLevelController.PlayerSkillExperience> entries, SkillDefinition skill)
    {
        var dataType = skill.SkillDataType;
        if (dataType != null && entries.ContainsKey(dataType)) return dataType;
        var ownType = skill.GetIl2CppType();
        return entries.ContainsKey(ownType) ? ownType : null;
    }

    // Looking the boat up walks the game's object registry, so not every frame.
    private static BoatGhost Boat
    {
        get
        {
            if (Time.realtimeSinceStartup >= _nextBoatLookup)
            {
                _nextBoatLookup = Time.realtimeSinceStartup + BoatLookupInterval;
                _boat = Sandbox.InGame ? BoatGhost.FindBoat() : null;
            }
            return _boat;
        }
    }

    public static bool HasBoat => Boat != null;

    public static bool BoatUnlocked
    {
        get
        {
            var boat = Boat;
            return boat != null && boat.Unlocked;
        }
    }

    public static void UnlockBoat()
    {
        var boat = Boat;
        if (boat == null || boat.Unlocked) return;
        Sandbox.Controller.DevUnlockBoat();
        if (!boat.Unlocked) boat.Unlocked = true;
        Plugin.Logger.LogInfo("Boat unlocked");
    }
}
