using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace NivalisToolBelt;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "renokk.nivalis.toolbelt";
    public const string Name = "Nivalis Tool Belt";
    public const string Version = "0.4.0";

    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        Settings.Bind(Config);
        AddComponent<ToolBeltBehaviour>();
        Log.LogInfo($"{Name} {Version} loaded, menu key: {Settings.MenuKey.Value}");
    }
}
