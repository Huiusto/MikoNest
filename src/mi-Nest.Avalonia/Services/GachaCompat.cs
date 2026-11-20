using System;
using System.Text.Json;

namespace MiNest.Services;

/// <summary>
/// Avalonia 版复用的抽卡本地库只依赖这两个与平台无关的小工具；
/// 桌面版把它们放在 MiHoYoHttp / GachaService 里，这里提供等价实现，
/// 避免把 WPF 相关代码链接进跨平台工程。
/// </summary>
public static class GachaService
{
    public static string PoolName(string gameKey, string code) => gameKey switch
    {
        "genshin" => code switch
        {
            "100" => "新手祈愿",
            "200" => "常驻祈愿",
            "301" or "400" => "角色活动祈愿",
            "302" => "武器活动祈愿",
            "500" => "集录祈愿",
            _ => "祈愿",
        },
        "starrail" => code switch
        {
            "1" => "群星跃迁",
            "2" => "始发跃迁",
            "11" => "角色活动跃迁",
            "12" or "22" => "光锥活动跃迁",
            "21" => "角色联动跃迁",
            _ => "跃迁",
        },
        "zzz" => code switch
        {
            "1" => "常驻频段",
            "2" or "102" => "独家频段",
            "3" or "103" => "音擎频段",
            "5" => "邦布频段",
            _ => "频段",
        },
        _ => "补给",
    };
}

public static class JsonCompat
{
    public static string GetProp(this JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return "";
        if (!el.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }
}
