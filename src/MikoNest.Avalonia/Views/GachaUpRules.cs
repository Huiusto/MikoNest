using System;
using System.Collections.Generic;

namespace MiNest.Avalonia.Views;

/// <summary>限定池 UP! 判定：各游戏常驻 / 非 UP 五星名单。</summary>
internal static class GachaUpRules
{
    private static HashSet<string> Set(string list)
        => new(list.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, HashSet<string>> Standard = new(StringComparer.OrdinalIgnoreCase)
    {
        ["genshin"] = Set("迪卢克|莫娜|七七|刻晴|提纳里|迪希雅|梦见月瑞希|天空之刃|天空之脊|天空之翼|天空之卷|天空之傲|风鹰剑|阿莫斯之弓|四风原典|和璞鸢|狼的末路"),
        ["zzz"] = Set("冯·莱卡恩|丽娜|猫宫又奈|11号|格莉丝|珂蕾妲|安比|妮可|比利|安东|本·比格|苍角|露西|派派|可琳|赛斯|狛野真斗|潘引壶|硫磺石|燃狱齿轮|拘缚者|嵌合编译器|啜泣摇篮|深海访客|钢铁肉垫|防爆者VI型|加农转子|人为刀俎|左转轮子|双生泣星|触电唇彩|雨林饕客|贵重骨核|好斗的阿炮|含羞恶面|街头巨星|星徽引擎|家政员|仿制星徽引擎|旋钻机-赤轴|德玛拉电池II型|聚宝箱|轰鸣座驾|时光切片|逍遥游球|兔能环|比格气缸|春日融融|正版变身器"),
        ["starrail"] = Set("彦卿|姬子|瓦尔特|克拉拉|白露|杰帕德|布洛妮娅|三月七|丹恒|艾丝妲|希露瓦|娜塔莎|桑博|佩拉|停云|青雀|寒鸦|驭空|素裳|玲可|桂乃芬|加拉赫|雪衣|米沙|如泥酣眠|银河铁道之夜|以世界之名|无可取代的东西|时节不居|制胜的瞬间|但战斗还未结束"),
        ["bh3"] = Set("空之律者|理之律者|炽翎|白夜执事|影骑士·月轮|失落迷迭|朔夜观星|识之律者|薪炎之律者|雷之律者|粉色妖精小姐|辉骑士·月魄|极地战刃|月下初拥|山吹|圣女祈祷|影舞冲击|血色玫瑰|幻海梦蝶|女武神·游侠|女武神·强袭|白练|绯红|深红|空无之钥|2nd圣遗物|索尔之锤|超电磁手炮|雷刀·鬼角|幽色咏叹调|灵刀·樱吹雪|妖刀·赤染樱|磁暴·斩|3rd圣遗物|虚无之刃|提尔的断腕|火刀·真田|5th圣遗物|鲜血之舞|犹大的誓约|11th圣遗物|仿犹大·血之拥抱|永暮双狼|断光之努亚达|残火之鸢"),
    };

    public static bool IsUp(string gameKey, string rank, string pool, string name)
    {
        if (rank != "5" && rank != "4") return false;
        if (!Standard.TryGetValue(gameKey, out var standard)) return false;
        var limited = gameKey switch
        {
            "genshin" => pool is "301" or "400" or "302",
            "starrail" => pool is "11" or "12" or "21" or "22",
            "zzz" => pool is "2" or "3" or "102" or "103",
            "bh3" => pool is "2" or "3" or "4",
            _ => false,
        };
        if (!limited || standard.Contains(name.Trim())) return false;
        return rank == "5" || gameKey != "genshin";
    }
}
