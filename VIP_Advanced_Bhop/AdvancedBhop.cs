using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Capabilities;
using System.Text.Json;
using VipCoreApi;
using Microsoft.Extensions.Logging;

namespace VIP_Advanced_Bhop;

[MinimumApiVersion(380)]
public sealed class AdvancedBhop : BasePlugin
{
    public override string ModuleName => "[VIP] Advanced Bhop";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "Rimmer";

    public BhopConfig Config { get; private set; } = new();
    private void LoadConfig(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "VIP_Advanced_Bhop.jsonc");
        if (!File.Exists(path))
        {
            using var template = typeof(AdvancedBhop).Assembly.GetManifestResourceStream("BhopDefaults")!;
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            template.CopyTo(file);
        }
        Config = JsonSerializer.Deserialize<BhopConfig>(File.ReadAllText(path), new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new JsonException($"Empty Bhop config: {path}");
    }

    internal const string Auto = "sv_autobunnyhopping", Enable = "sv_enablebunnyhopping";
    private const string LinuxMovement = "55 48 89 E5 41 57 41 56 41 55 41 54 49 89 FC 53 48 83 EC ? 48 8B 7F 30";
    private MemoryFunctionVoid<nint, nint>? _movement;
    internal bool MovementHookInstalled { get; private set; }
    private ConVar<float> _jumpImpulse = null!;
    internal float JumpImpulse => _jumpImpulse.Value;
    private bool _loaded;
    private Bhop? _feature;
    private HudPreferences? _preferences;
    private double _nextLookup;
    private static readonly PluginCapability<Action<CCSPlayerController, int, bool, string, int>> Hud = new("pmm_vipcore:bhop-hud");
    private static readonly PluginCapability<Action<CCSPlayerController, string, int>> Toast = new("pmm_vipcore:bhop-toast");
    private Action<CCSPlayerController, int, bool, string, int>? _hud;
    private Action<CCSPlayerController, string, int>? _toast;
    private static readonly PluginCapability<Action<CCSPlayerController, string, bool>> Notifications = new("pmm_vipcore:notification");
    private Action<CCSPlayerController, string, bool>? _notifications;
    private ConVar<bool> _auto = null!, _enable = null!;

    public override void Load(bool hotReload)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("ProcessMovement requires a Linux CS2 server.");
        _auto = ConVar<bool>.Find(Auto) ?? throw new InvalidOperationException(Auto);
        _enable = ConVar<bool>.Find(Enable) ?? throw new InvalidOperationException(Enable);
        _jumpImpulse = ConVar<float>.Find("sv_jump_impulse") ?? throw new InvalidOperationException("sv_jump_impulse missing");
        _movement = new(LinuxMovement);
        if (_movement.Handle != 0)
        {
            _movement.Hook(BeforeMove, HookMode.Pre);
            MovementHookInstalled = true;
        }
        else
            Logger.LogInformation("ProcessMovement unavailable; using server-tick takeoff.");
        Server.ExecuteCommand("sv_legacy_jump 1");
        RegisterListener<Listeners.OnMapStart>(_ => Server.NextFrame(() => Server.ExecuteCommand("sv_legacy_jump 1")));
        _loaded = true;
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        if (!_loaded || _feature != null) return;
        var api = IVipCoreApi.Capability.Get() ?? throw new InvalidOperationException("VipCoreApi missing");
        if (api.FeatureManager.FindByName("Bhop") is { } existing)
        {
            Logger.LogError("Bhop already belongs to {Owner}. Unload that module and reload VIP_Advanced_Bhop.",
                existing.GetType().Assembly.GetName().Name);
            return;
        }
        LoadConfig(api.ModulesConfigDirectory);
        FindNotificationProvider();
        if (Config.PlayerSettings) _preferences = new HudPreferences(api);
        _feature = new Bhop(this, api);
    }

    private void FindNotificationProvider()
    {
        try { _notifications = Notifications.Get(); }
        catch (KeyNotFoundException) { _notifications = null; }
        try { _hud = Hud.Get(); } catch (KeyNotFoundException) { _hud = null; }
        try { _toast = Toast.Get(); } catch (KeyNotFoundException) { _toast = null; }
        _nextLookup = Server.EngineTime + 1f;
    }

    private int Display(CCSPlayerController player)
    {
        int value = _preferences?.Mode(player) ?? 0;
        return value is >= 1 and <= 3 ? value : Math.Clamp(Config.DisplayMode, 1, 3);
    }

    internal void DrawHud(CCSPlayerController player, bool active, string time, int progress)
    {
        if (Server.EngineTime >= _nextLookup) FindNotificationProvider();
        if (_hud == null) return;
        int position = progress >= 0 && Config.Info is 2 or 3 && Display(player) != 1
            ? Math.Clamp(Config.Position, 0, 3) : -1;
        _hud(player, position, active, time, progress);
    }

    internal void Notice(CCSPlayerController player, string key, int seconds, bool active)
    {
        int mode = Config.Info;
        if (mode is < 1 or > 3) return;
        string text = Localizer.ForPlayer(player, key, seconds);
        FindNotificationProvider();
        if (_notifications == null || mode == 1 || (mode == 3 && Display(player) != 2))
            _feature?.PrintToChat(player, text);
        if (mode is 2 or 3 && Display(player) != 2)
        {
            int severity = active ? 0 : key is "bhop_first_round_off" or "bhop_restricttime" ? 2 : 1;
            if (_toast != null) _toast(player, text, severity);
            else _notifications?.Invoke(player, text, active);
        }
    }

    private HookResult BeforeMove(DynamicHook hook)
    {
        var handle = hook.GetParam<nint>(0);
        _feature?.PrepareMovement(handle);
        return HookResult.Continue;
    }

    internal void Restore(CCSPlayerController player)
    {
        player.ReplicateConVar(Auto, _auto.Value ? "1" : "0");
        player.ReplicateConVar(Enable, _enable.Value ? "1" : "0");
    }

    public override void Unload(bool hotReload)
    {
        _loaded = false;
        if (MovementHookInstalled && _movement != null)
        {
            MovementHookInstalled = false;
            _movement.Unhook(BeforeMove, HookMode.Pre);
        }
        _feature?.Reset();
        _feature?.Dispose();
        _preferences?.Dispose();
        _feature = null;
    }
}
