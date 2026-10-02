using System;
using System.Collections.Generic;
using Articy.Unity;
using Nivalis;
using Nivalis.GhostSystem.Ai;
using UnityEngine;

namespace NivalisToolBelt;

internal sealed class PersonEntry
{
    public Person Person;
    public string Name;
    public string SearchKey;
    public string Variable;
    public bool Met;
}

/// <summary>
/// Relationship levels with the story characters. The game keeps them in its story variables
/// ("AlfieGunfibel.Friend") and mirrors them onto the person, so they are written to the variable
/// and the game's own listener does the rest.
/// </summary>
internal static class People
{
    public static readonly string[] Kinds = { "Friend", "Business", "Romance", "Enemy" };

    private static readonly List<PersonEntry> All = new List<PersonEntry>();
    private static readonly List<PersonEntry> Matches = new List<PersonEntry>();
    private static PersonDataManager _source;
    private static string _query;

    public static int MaxLevel => RelationshipVector.MaxLevelsCount;

    /// <summary>Story characters whose name contains the text, the ones already met first. The list is reused, do not keep it.</summary>
    public static List<PersonEntry> Find(string query)
    {
        if (!Singleton<PersonDataManager>.InstanceExist())
        {
            Matches.Clear();
            return Matches;
        }
        var manager = Singleton<PersonDataManager>.Instance;
        if (manager != _source)
        {
            _source = manager;
            _query = null;
            Load(manager);
        }

        query = query.Trim().ToLowerInvariant();
        if (query == _query) return Matches;
        _query = query;
        Matches.Clear();
        foreach (var entry in All)
            if (query.Length == 0 || entry.SearchKey.Contains(query)) Matches.Add(entry);
        return Matches;
    }

    // The registry also holds some 1,500 generated passers-by; only characters with story
    // variables have relationships.
    private static void Load(PersonDataManager manager)
    {
        All.Clear();
        var variables = ArticyDatabase.DefaultGlobalVariables;
        foreach (var pair in manager.guidToPersons)
        {
            var person = pair.Value;
            if (person == null || !person.hasStory) continue;
            string variable = person.PureArticyVariableName;
            if (string.IsNullOrEmpty(variable) || !variables.IsVariableOfTypeInteger($"{variable}.{Kinds[0]}")) continue;
            string name = person.DisplayedName;
            var data = person.RuntimeData;
            All.Add(new PersonEntry
            {
                Person = person,
                Name = name,
                SearchKey = name.ToLowerInvariant(),
                Variable = variable,
                Met = data != null && data.HasBeenMet,
            });
        }
        All.Sort((a, b) => a.Met != b.Met ? (a.Met ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    public static int Get(PersonEntry entry, string kind) =>
        ArticyDatabase.DefaultGlobalVariables.GetVariableByString<int>($"{entry.Variable}.{kind}", false);

    public static void Set(PersonEntry entry, string kind, int level)
    {
        level = Mathf.Clamp(level, 0, MaxLevel);
        if (level == Get(entry, kind)) return;
        var boxed = new Il2CppSystem.Int32 { m_value = level }.BoxIl2CppObject();
        ArticyDatabase.DefaultGlobalVariables.SetVariableByString($"{entry.Variable}.{kind}", boxed);
        Plugin.Logger.LogInfo($"{entry.Name}: {kind} set to {Get(entry, kind)}");
    }
}
