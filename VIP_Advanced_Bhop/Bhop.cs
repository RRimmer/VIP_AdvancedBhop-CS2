using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using VipCoreApi;
using CounterStrikeSharp.API.Modules.Utils;

namespace VIP_Advanced_Bhop;

internal sealed class Bhop : VipFeature<int>
{
    private sealed class PlayerState(CCSPlayerController player)
    {
        public readonly CCSPlayerController Player = player;
        public bool Started, Window, First = true;
        public float Deadline, RestrictedUntil, PhaseStart, DamageStart, NextHud, ActiveSince;
        public bool? Applied;
        public nint Movement;
        public int LastHeldAirTick = -100;
        public bool AutoRepaired, EnableRepaired;
    }

    private readonly AdvancedBhop _plugin;
    private readonly Dictionary<int, PlayerState> _players = new();
    private readonly Dictionary<nint, PlayerState> _moving = new();
    private readonly Dictionary<int, (CCSPlayerController Player, float Since, bool Pistol)> _blockedHud = new();
    private readonly HashSet<(string, int)> _broadcast = new();
    private bool _ended, _firstBlocked, _announcedBlock;
    private float _nextAccess, _nextAudit;

    public Bhop(AdvancedBhop plugin, IVipCoreApi api) : base("Bhop", api)
    {
        _plugin = plugin;
        plugin.RegisterEventHandler<EventRoundStart>((_, _) => { Reset(); _ended = false; return HookResult.Continue; });
        plugin.RegisterEventHandler<EventRoundEnd>((_, _) => { _ended = true; Reset(); return HookResult.Continue; });
        plugin.RegisterListener<Listeners.OnMapStart>(_ => { Reset(); _ended = false; });
        plugin.RegisterListener<Listeners.OnMapEnd>(() => { _ended = true; Reset(); });
        plugin.RegisterListener<Listeners.OnClientDisconnect>(slot => Remove(slot));
        plugin.RegisterEventHandler<EventPlayerDeath>((ev, _) =>
        {
            if (ev.Userid is { IsValid: true } p) Remove(p.Slot);
            return HookResult.Continue;
        });
        plugin.RegisterEventHandler<EventPlayerHurt>(Hurt);
        plugin.RegisterListener<Listeners.OnTick>(Tick);
    }

    public override void OnPlayerSpawn(CCSPlayerController p, bool vip) => Refresh(p);
    public override void OnPlayerAuthorized(CCSPlayerController p, string group) => Refresh(p);
    public override void OnSelectItem(CCSPlayerController p, VipFeature feature) => Refresh(p);
    public override void OnPlayerRemoved(CCSPlayerController p, string group) => Remove(p.Slot);
    public override void OnPlayerDisconnect(CCSPlayerController p, bool vip) => Remove(p.Slot);

    private float HideAt(float since) => _plugin.Config.HudAutohideSeconds > 0
        ? since + _plugin.Config.HudAutohideSeconds : float.PositiveInfinity;

    private void Refresh(CCSPlayerController p)
    {
        if (!p.IsValid || p.IsBot || _ended) return;
        if (!p.PawnIsAlive || p.TeamNum < 2 || !PlayerHasFeature(p))
        {
            Remove(p.Slot);
            return;
        }
        if (_firstBlocked || !IsFeatureEnabled(p))
        {
            if (_players.ContainsKey(p.Slot)) Remove(p.Slot);
            float now = Server.CurrentTime;
            if (!_blockedHud.TryGetValue(p.Slot, out var blocked) ||
                blocked.Player.Handle != p.Handle || blocked.Pistol != _firstBlocked)
                _blockedHud[p.Slot] = blocked = (p, now, _firstBlocked);
            bool visible = now < HideAt(blocked.Since);
            _plugin.DrawHud(p, false, _firstBlocked ? "До конца раунда" : "∞", visible ? 0 : -1);
            return;
        }
        if (_blockedHud.ContainsKey(p.Slot)) Remove(p.Slot);
        if (_players.TryGetValue(p.Slot, out var previous) && previous.Player.Handle == p.Handle) return;
        Remove(p.Slot);
        var state = new PlayerState(p);
        _players.Add(p.Slot, state);
        Apply(state, false);
    }

    private void Remove(int slot)
    {
        if (_blockedHud.Remove(slot, out var blocked) && blocked.Player.IsValid)
            _plugin.DrawHud(blocked.Player, false, "", -1);
        if (!_players.Remove(slot, out var s)) return;
        _moving.Remove(s.Movement);
        if (s.Player.IsValid)
        {
            _plugin.DrawHud(s.Player, false, "", -1);
            _plugin.Restore(s.Player);
        }
    }

    internal void Reset()
    {
        foreach (var slot in _blockedHud.Keys.Concat(_players.Keys).ToArray()) Remove(slot);
        _nextAccess = _nextAudit = 0;
        _firstBlocked = _announcedBlock = false;
        _broadcast.Clear();
    }

    internal void PrepareMovement(nint handle)
    {
        if (!_moving.TryGetValue(handle, out var state) || !state.Player.IsValid || !state.Player.PawnIsAlive) return;
        var pawn = state.Player.PlayerPawn.Value;
        if (pawn is { IsValid: true } && pawn.MovementServices?.Handle == handle)
            PrepareJump(state, pawn);
    }

    private void Tick()
    {
        if (_ended || Utilities.GetGameRules() is not { } rules) return;
        float now = Server.CurrentTime;
        _broadcast.Clear();
        if (now >= _nextAccess)
        {
            _nextAccess = now + .5f;
            _firstBlocked = _plugin.Config.FirstOff != 0 && !rules.WarmupPeriod && IsPistolRound();
            foreach (var player in Utilities.GetPlayers()) Refresh(player);
            if (_firstBlocked && !_announcedBlock)
            {
                foreach (var p in Utilities.GetPlayers())
                    if (!p.IsBot && (_plugin.Config.Notify == 0 || (p.TeamNum >= 2 && IsPlayerVip(p) && PlayerHasFeature(p))))
                        _plugin.Notice(p, "bhop_first_round_off", 0, false);
                _announcedBlock = true;
            }
        }
        float interval = Math.Clamp(_plugin.Config.AuditInterval, 0, 10);
        bool audit = interval > 0 && now >= _nextAudit;
        if (audit) _nextAudit = now + interval;
        foreach (var s in _players.Values)
        {
            if (!s.Player.IsValid || !s.Player.PawnIsAlive) continue;
            if (!s.Started && !rules.FreezePeriod)
            {
                s.Started = true;
                int delay = Cooldown(s.Player);
                s.PhaseStart = now;
                s.Deadline = now + delay;
                if (delay > 0) Announce(s, "bhop_time_wait", delay, false);
            }
            if (s.Started && now >= s.Deadline) Advance(s, now);
            bool active = s.Window && now >= s.RestrictedUntil;
            if (active && s.RestrictedUntil > 0)
                _plugin.Notice(s.Player, "bhop_restricton", 0, true);
            if (now >= s.RestrictedUntil) s.RestrictedUntil = 0;
            if (s.Applied != active)
            {
                if (active) s.ActiveSince = now;
                s.NextHud = now;
                Apply(s, active);
            }
            else if (audit) Audit(s, active);
            if (s.Started && now >= s.NextHud)
            {
                s.NextHud = now + .25f;
                bool damaged = s.Window && s.RestrictedUntil > now;
                float end = damaged ? s.RestrictedUntil : s.Deadline;
                float start = damaged ? s.DamageStart : s.PhaseStart;
                bool unlimited = float.IsPositiveInfinity(end);
                if (unlimited && now >= HideAt(s.ActiveSince))
                {
                    _plugin.DrawHud(s.Player, false, "", -1);
                    s.NextHud = float.PositiveInfinity;
                }
                else
                {
                    float remaining = Math.Max(0, end - now);
                    int progress = unlimited ? 40 : (int)Math.Ceiling(40 * Math.Clamp(remaining / Math.Max(.001f, end - start), 0, 1));
                    _plugin.DrawHud(s.Player, active, unlimited ? "∞" : $"{Math.Ceiling(remaining)} сек", progress);
                    if (unlimited) s.NextHud = Math.Min(s.NextHud, HideAt(s.ActiveSince));
                }
            }
            if (!_plugin.MovementHookInstalled && active && !rules.FreezePeriod) PrepareJump(s, s.Player.PlayerPawn.Value);
            if (!_plugin.MovementHookInstalled) continue;
            nint handle = active && !rules.FreezePeriod ? s.Player.PlayerPawn.Value?.MovementServices?.Handle ?? 0 : 0;
            if (handle == s.Movement) continue;
            _moving.Remove(s.Movement);
            s.Movement = handle;
            if (handle != 0) _moving[handle] = s;
        }
    }

    private void PrepareJump(PlayerState state, CCSPlayerPawn? pawn)
    {
        if (pawn is not { IsValid: true } ||
            pawn.MoveType != MoveType_t.MOVETYPE_WALK || pawn.WaterLevel >= .5f ||
            pawn.MovementServices is not { } movement) return;

        if (((movement.Buttons.ButtonStates[0] | movement.QueuedButtonDownMask) & (ulong)PlayerButtons.Jump) == 0)
        {
            state.LastHeldAirTick = -100;
            return;
        }
        float limit = _plugin.Config.MaxSpeed;
        if (limit != -1f && (!(limit > 0) || !float.IsFinite(limit)))
            limit = 300f;
        var velocity = pawn.AbsVelocity;
        float speed = velocity.Length2D();
        if (limit > 0 && speed > limit)
        {
            float scale = limit / speed;
            velocity.X *= scale;
            velocity.Y *= scale;
        }

        const uint grounded = (uint)PlayerFlags.FL_ONGROUND;
        if ((pawn.Flags & grounded) == 0)
        {
            state.LastHeldAirTick = Server.TickCount;
            return;
        }
        float impulse = _plugin.JumpImpulse;
        if (impulse <= 0) return;
        // A landing while jump was held in the air continues the chain.
        // Scale velocity, since ballistic height is proportional to its square.
        if (Server.TickCount - state.LastHeldAirTick <= 2)
            impulse *= 0.85f;

        // Detach from the floor before applying velocity through the engine.
        // Otherwise ground movement may discard the upward component.
        pawn.GroundEntity.Raw = Utilities.InvalidEHandleIndex;
        pawn.Flags &= ~grounded;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_hGroundEntity");
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_fFlags");
        pawn.Teleport(velocity: new System.Numerics.Vector3(velocity.X, velocity.Y, impulse));
    }

    private int Cooldown(CCSPlayerController p)
    {
        int value = GetValue(p);
        return Math.Max(0, value == 1 ? _plugin.Config.LockTime : value);
    }

    private void Advance(PlayerState s, float now)
    {
        s.PhaseStart = now;
        if (s.Window && Cooldown(s.Player) is > 0 and var cooldown)
        {
            s.Window = false;
            s.Deadline = now + cooldown;
            Announce(s, "bhop_time_off", cooldown, false);
            return;
        }
        int duration = Math.Max(0, _plugin.Config.AllowTime);
        s.Window = true;
        s.Deadline = duration == 0 ? float.PositiveInfinity : now + duration;
        if (s.RestrictedUntil <= now)
            Announce(s, duration == 0 ? "bhop_time_infinite" : s.First ? "bhop_time_on_first" : "bhop_time_ready", duration, true);
        s.First = false;
    }

    private HookResult Hurt(EventPlayerHurt ev, GameEventInfo info)
    {
        var p = ev.Userid;
        var attacker = ev.Attacker;
        int seconds = _plugin.Config.DamageTime;
        if (_plugin.Config.Restrict == 0 || seconds <= 0 || p is not { IsValid: true } ||
            attacker is not { IsValid: true } || attacker.TeamNum < 2 || attacker.TeamNum == p.TeamNum ||
            !_players.TryGetValue(p.Slot, out var s) || !s.Window) return HookResult.Continue;
        bool first = s.RestrictedUntil <= Server.CurrentTime;
        s.DamageStart = Server.CurrentTime;
        s.RestrictedUntil = s.DamageStart + seconds;
        s.NextHud = 0;
        _moving.Remove(s.Movement);
        s.Movement = 0;
        Apply(s, false);
        if (first) _plugin.Notice(p, "bhop_restricttime", seconds, false);
        return HookResult.Continue;
    }

    private void Announce(PlayerState s, string key, int seconds, bool active)
    {
        _plugin.Notice(s.Player, key, seconds, active);
        if (_plugin.Config.Notify != 0 || !_broadcast.Add((key, seconds))) return;
        foreach (var p in Utilities.GetPlayers())
            if (!p.IsBot && !_players.ContainsKey(p.Slot)) _plugin.Notice(p, key, seconds, active);
    }

    private static void Apply(PlayerState s, bool active)
    {
        if (s.Applied == active) return;
        s.Applied = active;
        s.LastHeldAirTick = -100;
        s.AutoRepaired = s.EnableRepaired = false;
        string value = active ? "1" : "0";
        s.Player.ReplicateConVar(AdvancedBhop.Auto, value);
        s.Player.ReplicateConVar(AdvancedBhop.Enable, "0");
    }

    private void Audit(PlayerState state, bool active)
    {
        if (_plugin.Config.AuditForce == 0) return;
        Check(AdvancedBhop.Auto, active, ref state.AutoRepaired);
        Check(AdvancedBhop.Enable, false, ref state.EnableRepaired);

        void Check(string name, bool expected, ref bool repaired)
        {
            string raw = state.Player.GetConVarValue(name);
            bool actual;
            if (raw == "1") actual = true;
            else if (raw == "0") actual = false;
            else if (!bool.TryParse(raw, out actual)) return;

            if (actual == expected) repaired = false;
            else if (!repaired)
            {
                state.Player.ReplicateConVar(name, expected ? "1" : "0");
                repaired = true;
            }
        }
    }
}
