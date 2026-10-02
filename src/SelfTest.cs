#if SELFTEST
using System;
using System.IO;
using BepInEx;
using Nivalis;
using UnityEngine;

namespace NivalisToolBelt;

/// <summary>
/// Scripted in-game test, compiled only with -p:SelfTest=true and active only while
/// BepInEx/config/toolbelt-selftest.txt exists (its content is the save to load).
/// Writes one status line per second to BepInEx/toolbelt-selftest.log so an external
/// script can press the real hotkeys and check the effect. F9 = screenshot, F10 = speed x3 / x1,
/// F11 = save to the toolbelt_test slot, F12 = load it.
/// </summary>
internal static class SelfTest
{
    private static readonly string FlagPath = Path.Combine(Paths.ConfigPath, "toolbelt-selftest.txt");
    private static readonly string LogPath = Path.Combine(Paths.BepInExRootPath, "toolbelt-selftest.log");

    private static bool? _active;
    private static string _saveName;
    private static float _readySince = -1f;
    private static bool _loadRequested;
    private static float _nextStatus;
    private static int _shots;
    private static bool _fontsDumped;
    private const string TestSlot = "toolbelt_test";

    public static void Tick(ToolBeltBehaviour menu)
    {
        if (_active == null)
        {
            _active = File.Exists(FlagPath);
            if (_active == true)
            {
                _saveName = File.ReadAllText(FlagPath).Trim();
                File.WriteAllText(LogPath, $"selftest start, save '{_saveName}'\n");
            }
        }
        if (_active != true) return;

        float now = Time.realtimeSinceStartup;

        if (!_loadRequested && _saveName.Length > 0 && Singleton<SerializationManager>.InstanceExist()
            && Singleton<GameSceneManager>.InstanceExist() && !Singleton<GameSceneManager>.Instance.IsLoading)
        {
            if (_readySince < 0f) _readySince = now;
            if (now - _readySince > 15f)
            {
                _loadRequested = true;
                Write($"loading save, exists={Singleton<SerializationManager>.Instance.DoesSaveExist(_saveName)}");
                Singleton<SerializationManager>.Instance.Load(_saveName);
            }
        }

        var input = UnityInput.Current;
        if (input.GetKeyDown(KeyCode.F9))
        {
            string shot = Path.Combine(Paths.BepInExRootPath, $"toolbelt-shot-{++_shots}.png");
            ScreenCapture.CaptureScreenshot(shot);
            Write($"screenshot {shot}");
        }
        if (input.GetKeyDown(KeyCode.F10))
        {
            // Knock a few values off their targets so the switches have something to restore.
            var boat = Nivalis.Boat.BoatGhost.FindBoat();
            if (boat != null) boat.Fuel = 10f;
            foreach (var entry in Estates.Greenhouses())
            {
                if (!Estates.IsMine(entry)) continue;
                var modules = Singleton<GreenhouseManager>.Instance.GetArea(entry.Greenhouse).modules;
                for (int i = 0; i < modules.Count; i++)
                    if (modules[i].Value.Planted) modules[i].Value.Growth = 0.3f;
            }
            Write("perturbed boat fuel and greenhouse growth");
        }
        if (input.GetKeyDown(KeyCode.F11))
        {
            Write($"saving {TestSlot}: {Singleton<SerializationManager>.Instance.Save(TestSlot, false)}");
        }
        if (input.GetKeyDown(KeyCode.F12))
        {
            Write($"loading {TestSlot}");
            Singleton<SerializationManager>.Instance.Load(TestSlot);
        }

        if (now < _nextStatus) return;
        _nextStatus = now + 1f;
        try
        {
            var c = Sandbox.Controller;
            string player = c == null
                ? "player=none"
                : $"state={c.State} pos={c.transform.position} yaw={c.Rotation.eulerAngles.y:0} camYaw={c.Camera.transform.eulerAngles.y:0} vel={c.Velocity} move={c.defaultMoveSpeed} grounded={c.isGrounded} rayGround={c.RaycastCheckGround(c.transform.position)}";
            string clock = "clock=none";
            if (Clock.Available)
            {
                var t = Singleton<TimeOfDayManager>.Instance;
                clock = $"clock='{Clock.Text}' secs={TimeOfDayManager.TotalGameSeconds} daySecs={TimeOfDayManager.TotalDaySeconds} frozen={Clock.Frozen} tick={t.timeTickMultiplier} devMult={t.Dev_CurrentGameTimeMultiplier()} devScale={t.Dev_GetTimeScale()} override={t.TimeScaleOverride} unityScale={Time.timeScale} curfew={Clock.InCurfew} curfewHours={CurfewManager.CURFEW_WARNING_START_TIME_HOUR}/{CurfewManager.CURFEW_START_TIME_HOUR}/{CurfewManager.CURFEW_END_TIME_HOUR}";
            }
            string area = Singleton<GameSceneManager>.InstanceExist() ? $"scene='{Singleton<GameSceneManager>.Instance.CurrentGameplaySceneName}' area='{Teleports.AreaName}' spots={Teleports.Listed.Count} pending={Teleports.Pending?.Name} loading={Singleton<GameSceneManager>.Instance.IsLoading} canTravel={(Singleton<TravelManager>.InstanceExist() ? Singleton<TravelManager>.Instance.CanTravel.ToString() : "?")} canMove={(c == null ? "?" : c.CanMove.ToString())} money={(c == null ? 0 : Singleton<PlayerManager>.Instance.LocalPlayer.Inventory.Money)}" : "";
            if (!_fontsDumped && c != null)
            {
                _fontsDumped = true;
                foreach (var variable in Resources.FindObjectsOfTypeAll<IntegerVariable>())
                    Write($"intvar '{variable.name}' = {variable.Value}");
                if (Singleton<Nivalis.GhostSystem.Ai.PersonDataManager>.InstanceExist())
                {
                    var people = Singleton<Nivalis.GhostSystem.Ai.PersonDataManager>.Instance.guidToPersons;
                    foreach (var pair in people)
                    {
                        var person = pair.Value;
                        if (person == null || !person.hasStory) continue;
                        var data = person.RuntimeData;
                        Write($"person '{person.DisplayedName}' var='{person.ArticyVariableName}' pure='{person.PureArticyVariableName}' met={data?.HasBeenMet} rel={(data == null ? "none" : data.RelationshipVector.ToString())}");
                    }
                    Write($"people total={people.Count} maxLevels={Nivalis.GhostSystem.Ai.RelationshipVector.MaxLevelsCount}");
                }
                var database = Singleton<Nivalis.CraftingSystem.ItemDatabase>.Instance._allItems;
                int storable = 0;
                for (int i = 0; i < database.Length; i++) if (database[i] != null && database[i].IsPlayerStorable) storable++;
                Write($"items total={database.Length} storable={storable} listed={Items.Find("").Count}");
                foreach (var entry in Estates.Apartments())
                    Write($"apartment '{entry.Name}' mine={Estates.IsMine(entry)} state={Estates.State(entry)} rent={Estates.DailyRent(entry)} onlyHome={Estates.IsOnlyHome(entry)}");
                foreach (var entry in Estates.Greenhouses())
                    Write($"greenhouse '{entry.Name}' mine={Estates.IsMine(entry)} state={Estates.State(entry)} rent={Estates.DailyRent(entry)}");
                foreach (var preset in Weather.Presets())
                    Write($"weather preset '{preset.Name}' asset='{preset.Type.name}' rain={preset.Type.IsRain} snow={preset.Type.isSnow}");
                int variants = 0;
                foreach (var entry in Items.Find(""))
                    if ((entry.Name.EndsWith(")") || entry.Name.Contains(" #")) && variants++ < 6) Write($"variant '{entry.Name}'");
                Write($"variants total={variants} areas={Teleports.UnlockedAreaCount}/{Teleports.AreaCount}");
                try
                {
                    var curfew = Singleton<CurfewManager>.Instance;
                    Write($"curfew enabled={curfew._isCurfewEnabled} security={curfew._isCurfewSecurityEnabled} awareness={curfew.Awarness} impune={curfew._isImpuneUsed} caught={curfew.IsPlayerCaught}");
                    var boat = Nivalis.Boat.BoatGhost.FindBoat();
                    var boatController = Nivalis.Boat.BoatController.Instance;
                    Write($"boat fuel={(boat == null ? -1f : boat.Fuel)} controller={(boatController == null ? "none" : $"capacity={boatController.FuelCapacity} normalized={boatController.FuelNormalized}")}");
                    Write($"greenhouse debugSpeedUp={Singleton<GreenhouseManager>.Instance.debugSpeedUp}");
                    foreach (var entry in Estates.Greenhouses())
                    {
                        if (!Estates.IsMine(entry)) continue;
                        var plot = Singleton<GreenhouseManager>.Instance.GetArea(entry.Greenhouse);
                        var modules = plot.modules;
                        Write($"greenhouse '{entry.Name}' modules={modules.Count} growing={plot.IsGrowing}");
                        for (int i = 0; i < modules.Count; i++)
                        {
                            var module = modules[i].Value;
                            Write($"  module {i}: planted={module.Planted} ready={module.ReadyForHarvest} plant={(module.plantType == null ? "none" : module.plantType.Name)} growth={module.growth} cost={module.growthCost} speed={(module.Planted ? plot.CalculateGrowthSpeed(module) : 0f)}");
                        }
                    }
                    foreach (var entry in Venues.List())
                    {
                        if (!Venues.IsMine(entry)) continue;
                        var data = entry.Venue.RuntimeData;
                        var joint = data.JointInventory;
                        Write($"venue '{entry.Name}' base fridge={entry.Venue.BaseFridgeSpace} cupboard={entry.Venue.BaseCupboardSpace} normalCap={(joint.NormalCapacity.HasValue ? joint.NormalCapacity.Value : -1)} fridgeCap={(joint.RefridgeratedCapacity.HasValue ? joint.RefridgeratedCapacity.Value : -1)} normalCount={joint.NormalCount} fridgeCount={joint.RefridgeratedCount} furniture={data.PlacedFurniture.Count}");
                        var staff = data.staff;
                        for (int i = 0; i < staff.Count; i++)
                        {
                            var runtime = staff[i].RuntimeData;
                            var mood = runtime.WorkSatisfaction;
                            Write($"  staff '{staff[i].DisplayedName}' happiness={mood.Happiness} value={mood.Value} wage={mood.WageInfluence} skill={mood.SkillInfluence} quit={runtime.WorkQuit} paid={runtime.LastPaidWage} wageNow={runtime.Wage}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Write($"exploration failed: {e}");
                }
                if (Singleton<PropertyManager>.InstanceExist())
                {
                    var manager = Singleton<PropertyManager>.Instance;
                    var states = manager._propertyStates;
                    for (int i = 0; i < states.Count; i++)
                    {
                        try
                        {
                            var ghost = states[i];
                            var property = ghost.Property;
                            var venue = property.TryCast<Nivalis.GhostSystem.CustomerLoop.Venue>();
                            string line = $"property '{property.GetName()}' asset='{property.name}' kind={property.GetIl2CppType().Name} own={property.OwnershipType} playerOwned={property.PlayerOwned} acquireable={property.IsAcquireable} buy={property.BuyCost}/{manager.GetBuyPrice(property)} rent={property.RentCost}/{property.GetRentPrice()} daily={ghost.DailyCost}";
                            if (venue != null)
                                line += $" level={venue.RuntimeData.currentLevel}/{venue.LevellingData.Length} debt={venue.CurrentDebt} owner={(venue.Owner == null ? "none" : venue.Owner.DisplayedName)} tier={venue.Tier} starting={venue.StartingVenue} staff={venue.RuntimeData.staff.Count}";
                            Write(line);
                        }
                        catch (Exception e)
                        {
                            Write($"property {i} failed: {e.Message}");
                        }
                    }
                }
            }
            string character = "";
            var skills = Character.Skills;
            if (skills != null && c != null)
            {
                character = $"typing={menu.IsTyping} bag={Singleton<PlayerManager>.Instance.LocalPlayer.Inventory.Items.ItemCount}/{Singleton<PlayerManager>.Instance.LocalPlayer.Inventory.Items.StackCount} last='{Items.LastResult}' debt={Character.GetCounter(Character.NoodleBarDebt)}/{Character.GetCounter(Character.OtherDebt)} insp={Character.GetCounter(Character.InspirationPoints)} alfie={AlfieVector()} extra={ExtraSummary()} estates={EstateSummary()} weather={WeatherSummary()} areas={Teleports.UnlockedAreaCount}/{Teleports.AreaCount} venues={VenueSummary()} cents={Character.MoneyCents} boat={Character.HasBoat}/{Character.BoatUnlocked} skills=";
                for (int i = 0; i < skills.Length; i++)
                {
                    var xp = Singleton<Nivalis.SkillSystem.SkillLevelController>.Instance.GetPlayerSkillExperience(skills[i]);
                    character += $"[{skills[i].DisplayName}|{skills[i].name} lvl={xp.CurrentLevel} xp={xp.Experience} count={skills[i].LevelCount} need=";
                    for (int l = 0; l < skills[i].LevelCount; l++) character += skills[i].GetExperienceForLevel(l) + ";";
                    character += "]";
                }
            }
            Write($"t={now:0} win={(int)menu.WindowRect.x},{(int)menu.WindowRect.y},{(int)menu.WindowRect.width},{(int)menu.WindowRect.height} menu={menu.IsOpen} cursor={CursorModeManager.IsCursorActive} {player} {clock} {area} {character}");
        }
        catch (Exception e)
        {
            Write($"status failed: {e}");
        }
    }

    private static string ExtraSummary()
    {
        var curfew = Singleton<CurfewManager>.Instance;
        var boat = Nivalis.Boat.BoatGhost.FindBoat();
        string text = $"undetected={Toggles.Undetected} security={curfew._isCurfewSecurityEnabled} awareness={curfew.Awarness:0.00} caught={curfew.IsPlayerCaught} fuelOn={Toggles.BoatFuel} fuel={(boat == null ? -1f : boat.Fuel):0.0} growOn={Toggles.InstantGrowth} growth=";
        foreach (var entry in Estates.Greenhouses())
        {
            if (!Estates.IsMine(entry)) continue;
            var modules = Singleton<GreenhouseManager>.Instance.GetArea(entry.Greenhouse).modules;
            for (int i = 0; i < modules.Count; i++) text += $"{modules[i].Value.growth:0.00}/";
        }
        text += $" storage=x{Venues.StorageMultiplier}";
        foreach (var entry in Venues.List())
        {
            if (!Venues.IsMine(entry)) continue;
            var data = entry.Venue.RuntimeData;
            var joint = data.JointInventory;
            text += $"[{entry.Name} cap={joint.NormalCapacity.Value}/{joint.RefridgeratedCapacity.Value} max={joint.NormalInventory.Restriction.MaxItems.Value}/{joint.RefridgeratedInventory.Restriction.MaxItems.Value} count={joint.NormalCount}/{joint.RefridgeratedCount} mood=";
            var staff = data.staff;
            for (int i = 0; i < staff.Count; i++) text += $"{staff[i].RuntimeData.WorkSatisfaction.Happiness}:{staff[i].RuntimeData.WorkSatisfaction.Value:0.00},";
            text += "]";
        }
        return text;
    }

    private static string EstateSummary()
    {
        string text = "";
        foreach (var entry in Estates.Apartments())
            if (Estates.IsMine(entry)) text += $"[{entry.Name}:{Estates.State(entry)}]";
        foreach (var entry in Estates.Greenhouses())
            if (Estates.IsMine(entry)) text += $"[{entry.Name}:{Estates.State(entry)}]";
        return text;
    }

    private static string WeatherSummary()
    {
        if (!Singleton<WeatherForecastController>.InstanceExist()) return "none";
        var controller = Singleton<WeatherForecastController>.Instance;
        var manager = Nivalis.Weather.WeatherManager.Instance;
        return $"held={controller.stopTimedWeatherProgression} rain={(manager == null ? -1f : manager.globalRainAmount):0.00} fog={(manager == null ? -1f : manager.globalFogAmount):0.00} snow={(manager == null ? -1f : manager.globalSnow):0.00}";
    }

    private static string VenueSummary()
    {
        string text = "";
        var player = Singleton<PlayerManager>.Instance.LocalPlayer;
        foreach (var entry in Venues.List())
            if (Venues.IsMine(entry) || entry.Name == "Sake Bar") text += $"[{entry.Name} mine={Venues.IsMine(entry)} state={player.GetPropertyOwnershipState(entry.Venue)} lvl={Venues.GetLevel(entry)} staff={entry.Venue.RuntimeData.staff.Count}]";
        return text + $" owned={player.GetNumberOfOwnedVenues()}";
    }

    private static string AlfieVector()
    {
        foreach (var pair in Singleton<Nivalis.GhostSystem.Ai.PersonDataManager>.Instance.guidToPersons)
            if (pair.Value != null && pair.Value.PureArticyVariableName == "AlfieGunfibel") return Articy.Unity.ArticyDatabase.DefaultGlobalVariables.GetVariableByString<int>("AlfieGunfibel.Friend", false) + " | " + pair.Value.RuntimeData.RelationshipVector.ToString();
        return "none";
    }

    private static void Write(string line) => File.AppendAllText(LogPath, line + "\n");
}
#endif
