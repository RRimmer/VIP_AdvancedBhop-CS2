using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace VIP_Advanced_Bhop;

public sealed class BhopConfig : BasePluginConfig
{
    [JsonPropertyName("bhop_max_speed")]
    public float MaxSpeed { get; set; } = 0f;

    [JsonPropertyName("bhop_hud_autohide_seconds")]
    public float HudAutohideSeconds { get; set; } = 5f;

    [JsonPropertyName("bhop_display_mode")]
    public int DisplayMode { get; set; } = 3;

    [JsonPropertyName("bhop_hud_position")]
    public int Position { get; set; }

    [JsonPropertyName("bhop_player_display_settings")]
    public bool PlayerSettings { get; set; }

    [JsonPropertyName("bhop_info")]
    public int Info { get; set; } = 2;

    [JsonPropertyName("bhop_notify")]
    public int Notify { get; set; } = 0;

    [JsonPropertyName("bhop_allowtime")]
    public int AllowTime { get; set; } = 5;

    [JsonPropertyName("bhop_locktime")]
    public int LockTime { get; set; } = 15;

    [JsonPropertyName("bhop_restrict_type")]
    public int Restrict { get; set; } = 0;

    [JsonPropertyName("bhop_restrict_time")]
    public int DamageTime { get; set; } = 2;

    [JsonPropertyName("bhop_first_round_off")]
    public int FirstOff { get; set; } = 0;

    [JsonPropertyName("bhop_stability_audit_interval")]
    public float AuditInterval { get; set; } = 0.25f;

    [JsonPropertyName("bhop_stability_audit_force")]
    public int AuditForce { get; set; } = 1;
}
