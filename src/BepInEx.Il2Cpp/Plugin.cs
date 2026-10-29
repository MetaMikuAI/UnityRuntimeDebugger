using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityRuntimeDebugger.Hosting;
using PluginInfo = UnityRuntimeDebugger.Hosting.PluginInfo;

namespace UnityRuntimeDebugger.Il2Cpp;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BasePlugin
{
    private Debugger? _debugger;
    private LogListener? _logs;

    public override void Load()
    {
        var address = Config.Bind("Server", "ListenAddress", "127.0.0.1", "0.0.0.0 for LAN access.");
        var port = Config.Bind("Server", "Port", 18765, new ConfigDescription("", new AcceptableValueRange<int>(1, 65535)));
        var enabled = Config.Bind("Security", "Enabled", false, "Runs arbitrary C# for anyone with the token.");
        var acknowledged = Config.Bind("Security", "AcknowledgeRisk", false, "Required together with Enabled.");
        var fixedToken = Config.Bind("Security", "Token", "", "Empty: random on every start.");
        var nativeUpdate = Config.Bind("Compatibility", "UseNativeUpdateHook", false,
            "Run main-thread work from a Dobby hook on EventSystem.Update instead of a MonoBehaviour.");

        if (!enabled.Value || !acknowledged.Value)
        {
            Log.LogWarning("UnityRuntimeDebugger is disabled ([Security] Enabled, AcknowledgeRisk).");
            return;
        }

        var token = string.IsNullOrWhiteSpace(fixedToken.Value) ? Guid.NewGuid().ToString("N") : fixedToken.Value.Trim();
        try
        {
            _debugger = Debugger.Start(new DebuggerOptions(token, new UnityGame(), new LoaderInfo("BepInEx 6 IL2CPP", Paths.BepInExVersion.ToString(), Plugins))
            {
                ListenAddress = address.Value,
                Port = port.Value,
                Runtime = new Il2CppRuntime(),
                LogFile = Path.Combine(Paths.BepInExRootPath, "LogOutput.log"),
                OnError = message => Log.LogError(message),
            });
            _debugger.MainThread.Error += ex => Log.LogError(ex);

            _logs = new LogListener(_debugger.Logs);
            BepInEx.Logging.Logger.Listeners.Add(_logs);

            if (nativeUpdate.Value)
                NativeUpdateHook.Install(_debugger.MainThread);
            else
                MainThreadPump.Install(_debugger.MainThread);
        }
        catch (Exception ex)
        {
            Log.LogError($"UnityRuntimeDebugger failed to start: {ex}");
            return;
        }

        Log.LogWarning($"UnityRuntimeDebugger token: {token}");
        foreach (var url in _debugger.Urls())
            Log.LogWarning($"UnityRuntimeDebugger web console: {url}/#token={token}");
        if (_debugger.IsLoopbackOnly && Application.platform == RuntimePlatform.Android)
            Log.LogWarning($"UnityRuntimeDebugger: adb forward tcp:{port.Value} tcp:{port.Value}");
    }

    public override bool Unload()
    {
        NativeUpdateHook.Uninstall();
        if (_logs != null)
            BepInEx.Logging.Logger.Listeners.Remove(_logs);
        _debugger?.Dispose();
        return true;
    }

    private static IReadOnlyList<PluginInfo> Plugins()
        => IL2CPPChainloader.Instance.Plugins.Values
            .Select(p => new PluginInfo(p.Metadata.GUID, p.Metadata.Name, p.Metadata.Version.ToString()))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
