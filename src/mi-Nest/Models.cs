using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiNest.Models;

/// <summary>一款米哈游 PC 游戏的静态定义</summary>
public class GameDef
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>游戏安装目录下的 Unity 数据目录名（用于定位 webCaches）</summary>
    public string DataDir { get; set; } = "";
    /// <summary>可能的游戏主程序名</summary>
    public string[] ExeNames { get; set; } = Array.Empty<string>();
    /// <summary>注册表中可能存在的安装路径键</summary>
    public string[] RegistryPaths { get; set; } = Array.Empty<string>();
    /// <summary>封面渐变色（占位用，可替换为官图）</summary>
    public string CoverColor1 { get; set; } = "#2B3A67";
    public string CoverColor2 { get; set; } = "#5B8DEF";
    /// <summary>官网（软件内打开）</summary>
    public string OfficialUrl { get; set; } = "";
}

/// <summary>应用设置（明文部分，存 %AppData%\mi-Nest\settings.json）</summary>
public class AppSettings
{
    public string Theme { get; set; } = "Light";
    /// <summary>界面语言（"" = 跟随系统；zh-Hans/zh-Hant/en/ja/ko/ru），重启后生效</summary>
    public string Language { get; set; } = "";
    /// <summary>自定义数据目录（设置-应用信息 更改）；为空则使用默认 %AppData%\mi-Nest</summary>
    public string DataDirOverride { get; set; } = "";
    /// <summary>gameKey -> 安装目录</summary>
    public Dictionary<string, string> GamePaths { get; set; } = new();
    /// <summary>自定义启动页背景图（绝对路径）</summary>
    public string? CustomBackground { get; set; }
    /// <summary>每款游戏单独的自定义背景图：gameKey -> 绝对路径</summary>
    public Dictionary<string, string> GameBackgrounds { get; set; } = new();
    /// <summary>API 请求用的设备 ID（首次生成后持久化）</summary>
    public string DeviceId { get; set; } = "";
    /// <summary>左侧导航栏（中间区）项顺序：内置 key（启动/社区/抽卡记录/任务/签到/米哈游相关）或 pin:xxx</summary>
    public List<string> NavOrder { get; set; } = new();
    /// <summary>在隐藏设置中被关闭（隐藏）的内置导航项 key；默认空 = 全部显示</summary>
    public List<string> DisabledNavItems { get; set; } = new();
    /// <summary>从「米哈游相关」固定到左侧导航栏的链接项</summary>
    public List<PinnedLink> PinnedLinks { get; set; } = new();
    /// <summary>是否已应用默认固定（首次启动默认把 HOYO-MiX 固定到左侧项目栏，之后不再自动追加）</summary>
    public bool DefaultPinApplied { get; set; }
    /// <summary>「第三方工具」页内置顶项（URL 列表，置顶的工具排在列表最前面）</summary>
    public List<string> ThirdPartyPinned { get; set; } = new();
    /// <summary>开机自动运行 mi-Nest</summary>
    public bool AutoStartOnBoot { get; set; }
    /// <summary>退出游戏后自动弹出启动器主界面</summary>
    public bool BringFrontOnGameExit { get; set; }
    /// <summary>关闭行为：Tray=最小化到系统托盘，Exit=退出启动器</summary>
    public string CloseBehavior { get; set; } = "Exit";
    /// <summary>胡桃风格背景：用胡桃主题插画作为应用界面背景（隐藏设置里开启）</summary>
    public bool HutaoBackdrop { get; set; }
    /// <summary>隐藏左侧「账号」按钮（设置页「更新」上方的开关控制）</summary>
    public bool HideAccountNav { get; set; }
}

/// <summary>固定到左侧导航栏的快捷链接</summary>
public class PinnedLink
{
    public string Key { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>米哈游通行证账号（Cookie 用 DPAPI 加密后存储）</summary>
public class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nickname { get; set; } = "";
    public string Uid { get; set; } = "";
    public string CookieEncrypted { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime LastLogin { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string Cookie { get; set; } = "";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Nickname) ? Uid : $"{Nickname}（{Uid}）";
}

/// <summary>抽卡记录条目</summary>
public class GachaItem
{
    public string Uid { get; set; } = "";
    /// <summary>所属游戏 key（本地库/多游戏管理用；UIGF/SRGF 导出时不含此字段）</summary>
    public string GameKey { get; set; } = "";
    public string GachaType { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    public string RankType { get; set; } = ""; // 5 / 4 / 3
    public string ItemType { get; set; } = "";
    /// <summary>抽卡数量（UIGF count 字段，通常为 1）</summary>
    public string Count { get; set; } = "1";
    public string Time { get; set; } = "";
    public string Id { get; set; } = "";
    /// <summary>卡池显示名（拉取后填充）</summary>
    public string PoolName { get; set; } = "";
}

/// <summary>卡池定义</summary>
public class GachaPoolDef
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public GachaPoolDef(string code, string name) { Code = code; Name = name; }
}

/// <summary>游戏角色（用于签到）</summary>
public class SignRole
{
    public string Uid { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Region { get; set; } = "";
    public string RegionName { get; set; } = "";
    public string GameBiz { get; set; } = "";
    public string Level { get; set; } = "";
}

/// <summary>一次签到/任务操作的日志行（显示为 [INFO][HH:mm:ss]文本）</summary>
public class SignLogEntry
{
    public string Text { get; set; } = "";
    public string Level { get; set; } = "INFO";   // INFO / WARN / ERROR
    public DateTime Time { get; set; } = DateTime.Now;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsError => Level == "ERROR";

    public string Line => $"[{Level}][{Time:HH:mm:ss}]{Text}";

    public override string ToString() => Line;
}
