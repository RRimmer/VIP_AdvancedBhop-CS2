using CounterStrikeSharp.API.Core;
using VipCoreApi;
using VipCoreApi.Enums;

namespace VIP_Advanced_Bhop;

internal sealed class HudPreferences(IVipCoreApi api) : VipFeature<int>("BhopHud", api, FeatureType.Selectable)
{
    private const string Cookie = "advanced_bhop_display";
    public int Mode(CCSPlayerController player) => PlayerHasFeature(player) ? GetPlayerCookie<int>(player, Cookie) : 0;

    public override void OnSelectItem(CCSPlayerController player, VipFeature feature)
    {
        var menu = CreateMenu("Уведомления Bunnyhop");
        string[] choices = ["Как на сервере", "Только уведомление", "Только отсчёт", "Уведомление и отсчёт"];
        int current = Mode(player);
        for (int i = 0; i < choices.Length; i++)
        {
            int value = i;
            menu.AddMenuOption(choices[i], (p, _) => SetPlayerCookie(p, Cookie, value), i == current);
        }
        menu.Open(player);
    }
}
