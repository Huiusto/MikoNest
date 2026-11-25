using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MiNest.Windows;

/// <summary>设置页「更新日志」窗口：按版本倒序展示从首个公开版到当前版本的变化。</summary>
public sealed class ChangelogWindow : Window
{
    private sealed record Entry(string Version, string Date, string[] Items);

    private static readonly Entry[] Entries =
    {
        new("0.1.38", "2026-11-25", new[]
        {
            "修复换票响应嵌套 token 被当成 JSON 原文写入 Cookie 的问题：exchange / getTokenBySToken 返回的 token 无论是字符串还是 { token: \"...\" } 嵌套对象，都正确取到纯凭证；历史上被写坏的 cookie_token / ltoken 会在下次刷新时识别为无效并重新换票，不再带着无效凭证请求角色接口。(๑•̀ㅂ•́)و✧",
            "角色拉取日志补齐诊断：逐款游戏透出原始 retcode 与消息，并标注 stoken / cookie_token / ltoken 凭证齐全情况，便于定位是缺凭证还是接口风控，不再笼统提示「登录凭证可能已过期」；换票请求头 SDK/账号版本对齐已验证参考实现。(＾▽＾)",
        }),
        new("0.1.37", "2026-11-25", new[]
        {
            "重调扫码登录数据获取（对照 MikoMiko / TeyvatGuide / Starward）：二维码生成与轮询请求头版本升至 2.119.0、补齐 app.mihoyo.com referer；登录确认响应改为 JSON 字段优先、响应 Set-Cookie 兜底解析账号 ID / mid / stoken / cookie_token / ltoken；token 类型标记缺失时回退取首个 token，减少「登录凭证解析失败」。(๑•̀ㅂ•́)و✧",
            "短信验证码登录复用同一套凭证解析与 Set-Cookie 兜底；响应头已下发的 cookie_token / ltoken 直接保留，避免重复换票。(＾▽＾)",
        }),
        new("0.1.36", "2026-11-25", new[]
        {
            "换票链路改为现代通行证 V2 接口（ma-cn-session/app/exchange，stoken 换取 cookie_token_v2 / ltoken_v2），旧换票链路作为兜底；已保存、只带 stoken 的账号在登录校验与任务/签到拉取前会自动换票补齐 cookie_token / ltoken，无需重新扫码。(๑•̀ㅂ•́)و✧",
            "角色获取与身份展示对齐已验证口径：角色接口 getUserGameRolesByCookie 逐款游戏循环、身份接口 getUserFullInfo 按账号 UID 查询，均携带完整 Cookie、不再附加 DS 签名；修复刚登录账号仍误报「登录凭证可能已过期」、账号显示占位昵称与账号 ID 的问题。(＾▽＾)",
        }),
        new("0.1.35", "2026-11-25", new[]
        {
            "账号扫码 / 手机号登录后先显示通行证昵称与米游社 UID：用户资料接口改为携带完整登录态，昵称优先取用户资料（account_name、nickname 兜底），UID 取账号 ID，账号列表正确呈现「昵称（米游社 UID）」，不再只显示占位名。(๑•̀ㅂ•́)و✧",
            "修复「任务/签到」刚登录账号仍报「暂无法获取游戏角色：登录凭证可能已过期」：角色获取改为两条参考实现链路合并去重——TeyvatGuide cookie_token 链路（getUserGameRolesByCookie，X4 DS2）与 Starward CookieToken 链路（passport-api 按游戏循环、完整 Cookie、不签 DS）；仅两条链路都返回登录失效类错误才提示凭证过期，其余错误透出原始消息与 retcode，不再笼统误判。(＾▽＾)",
        }),
        new("0.1.34", "2026-11-25", new[]
        {
            "修复扫码与手机号登录后账号仍显示占位昵称的问题：通行证昵称与米游社 UID 改由米游社用户资料接口获取（口径对齐 TeyvatGuide，DS1 签名、仅带 cookie_token+account_id），昵称取不到时回退通行证账号名，账号列表正确显示「昵称（米游社 UID）」。(๑•̀ㅂ•́)و✧",
            "修复「任务/签到」提示「签到接口暂不可用」：签到状态/执行、米游币任务列表与状态、社区打卡、浏览点赞分享的 Cookie 子集、DS 类型与盐值对齐 TeyvatGuide 实用脚本板块；缺少 cookie_token 凭证时给出明确提示，接口原始 retcode 与消息一并透出，不再笼统报「暂不可用」。(＾▽＾)",
        }),
        new("0.1.33", "2026-11-25", new[]
        {
            "「游戏设置」里的「预约游戏」「米哈云游」开关现在只控制各自入口：隐藏时保留原位置占位，其余按钮与卡片的位置、对齐不再受影响。(＾▽＾)",
            "修复扫码与手机号登录后，账号名称和米游社 UID 错误显示成绑定游戏（如崩坏学院2）角色名与游戏 UID 的问题：账号列表改为展示通行证昵称与米游社 UID（账号 ID），身份信息取自米游社用户资料接口。(๑•̀ㅂ•́)و✧",
            "调整全新安装后的默认设置：「预约游戏」「米哈云游」入口默认关闭；页面不透明度默认最低；界面配色默认跟随系统；关闭软件默认最小化到系统托盘。(￣▽￣)ノ",
        }),
        new("0.1.32", "2026-11-25", new[]
        {
            "修复「账号」页扫码登录与手机号短信登录均提示「Cookie 无效：登录状态失败」的问题：登录校验不再只依赖 cookie_token，新增 stoken 角色链路（getUserGameRolesByStoken）与 Action Ticket 身份兜底，扫码/短信拿到 stoken 即可完成登录建号。(๑•̀ㅂ•́)و✧",
            "启动页「游戏设置」新增显隐开关：可自由选择是否显示左下角「预约游戏」卡片与左上角「米哈云游」按钮，关闭后启动页对应入口立即隐藏。(＾▽＾)",
        }),
        new("0.1.31", "2026-11-25", new[]
        {
            "启动页「全部游戏」下拉在游戏名前显示对应角色头像图标：原神（派蒙）、崩坏3（琪亚娜）、崩坏：星穹铁道、绝区零（安比）。(＾▽＾)",
        }),
        new("0.1.30", "2026-11-25", new[]
        {
            "「任务/签到」页排版对齐参考图：账号状态改为彩色圆点 + 状态文字，已登录显示绿色圆点。(๑•̀ㅂ•́)و✧",
            "登录失效时，米游币任务与签到任务卡片统一显示橙色「登录状态已失效，请到「账号」页重新登录」提示，不再直接弹出接口报错。(￣▽￣)ノ",
        }),
        new("0.1.28", "2026-11-22", new[]
        {
            "软件由原名 mi-Nest 正式更名为 MikoNest；软件图标更换为全新蓝白 Q 版形象，侧栏品牌字样更换为蔚蓝圆润字标。(๑•̀ㅂ•́)و✧",
            "安装目录、主程序、快捷方式、开机启动项与用户数据目录同步迁移到 MikoNest；原设置、账号与抽卡记录会自动继承，无需手动搬家。(￣▽￣)ノ",
        }),
        new("0.1.27", "2026-11-22", new[]
        {
            "原神祈愿记录：武器活动祈愿 5★ 记录下的保底条改为 0-80，0-65 为绿色、66-80 为红色，按武器池真实保底长度显示。(๑•̀ㅂ•́)و✧",
            "千星奇域常驻颂愿：3★ 记录下的保底条改为 0-90，0-70 为绿色、71-80 为红色，81-90 继续红色收尾，不再按 10 抽短条显示。(￣▽￣)ノ",
        }),
        new("0.1.26", "2026-11-21", new[]
        {
            "设置页「界面配色」「语言切换」「应用维护」三张卡片降低高度，并保持同一行完全等高，顶部区域更紧凑。(￣▽￣)ノ",
            "「应用维护」里的「检查更新」按钮在开始更新后自动变成 0% 到 100% 的下载进度提示；下载完成显示 100%，安装包启动后由安装程序接管。(๑•̀ㅂ•́)و✧",
            "「发现新版本」更新弹窗整体缩小，减少对桌面内容的遮挡，同时保留左侧角色、版本信息和右侧更新内容。(＾▽＾)",
        }),
        new("0.1.25", "2026-11-21", new[]
        {
            "修复启动页点击「安装游戏」时，如果旧安装目录仍存在但找不到游戏主程序，只会提示「未在目录中找到游戏主程序」而无法进入安装的问题；现在会直接打开安装窗口，重新选择路径并开始安装。(๑•̀ㅂ•́)و✧",
        }),
        new("0.1.24", "2026-11-21", new[]
        {
            "启动页「获取游戏」统一改为「安装游戏」，更直观地说明点击后会进入安装流程。(￣▽￣)ノ",
            "打开软件时自动检查 MikoNest 更新，发现更高版本会立即弹出更新窗口；更新窗口按参考图改为左侧角色、中间版本信息、右侧更新内容三栏布局，并展示本次 Release 的更新日志。点击「开始更新」1 秒后窗口自动最小化，下载与安装转入后台继续。(๑•̀ㅂ•́)و✧",
            "设置页「界面配色」卡片与右侧「语言切换」「应用维护」卡片改为等高布局，视觉更整齐。(＾▽＾)",
        }),
        new("0.1.23", "2026-11-21", new[]
        {
            "「游戏设置」新增「卸载游戏」，确认后可删除该游戏的安装目录；启动页首页固定显示自定义顺序里的第一款游戏。修复在游戏设置里停止安装并清理后，主按钮误显示「开始游戏」的问题。(￣▽￣)ノ",
            "修复「发现新版本」弹窗右上角关闭按钮被按钮内边距挤压、只显示一半的问题；现在关闭按钮完整可见、点击区域也更大。(＾▽＾)",
        }),
        new("0.1.22", "2026-11-21", new[]
        {
            "安装详情里的「停止安装并清理」移到「游戏设置」；「游戏设置」里不再显示「卸载游戏」按钮。需要中止安装时，从三条横杠进入游戏设置即可。(￣▽￣)ノ",
            "安装进度按钮、百分比、图标和进度条的黄色改成跟随 Windows 系统强调色，换系统主题色后安装界面也会一起变。(๑•̀ㅂ•́)و✧",
            "启动页「全部游戏」下拉支持自定义顺序：按住每项右侧的 ⠿ 拖动即可排序，松开后自动保存，下次打开仍按你排的顺序显示。(＾▽＾)",
        }),
        new("0.1.21", "2026-11-21", new[]
        {
            "安装软件时自动预取启动页全部动态壁纸，首次进入启动页即可直接播放，无需再等待加载。(￣▽￣)ノ",
            "安装游戏后进度直接显示在启动页「开始游戏」位置：主按钮显示暂停 / 继续与百分比，鼠标靠近弹出官方启动器式下载详情（下载量、安装量、速度、剩余时间、目标目录），可停止安装并清理已写入内容。(๑•̀ㅂ•́)و✧",
            "自动检查更新对同一版本只提示一次：取消或关闭弹窗即忽略该版本，出现更高版本才会再次弹出。设置页「检测更新」也改用同一粉色更新弹窗，并移除按钮下方的「已是最新版本」文本。",
        }),
        new("0.1.20", "2026-11-20", new[]
        {
            "启动页「游戏设置」改为三条横杠图标按钮，不再显示文字；面板里移除了「查看游戏日志」与「安装 / 更新游戏」。(≧▽≦)",
            "安装游戏不再下载分卷压缩包：改为像官方启动器那样在本软件内逐文件安装，可随时停止并清理已写入内容。",
            "开始游戏前自动检查 MikoNest 更新，发现新版弹出「发现新版本」提醒，可选择「取消 / 开始更新」。",
        }),
        new("0.1.19", "2026-11-20", new[]
        {
            "设置页「实时便签」改为「更新日志」，可查看从 v0.1.5 起每个版本的更新与修复内容。",
            "启动页「开始游戏」右侧新增「游戏设置」：查看安装路径与磁盘空间、重新定位、查看游戏日志、卸载游戏，并可直接在软件内安装 / 更新。",
            "抽卡记录 UID 下拉不再出现「全部 UID」汇总；无账号时显示「无账号」，更新记录后保持当前 UID。千星奇域常驻颂愿按星级显示 4★ 0-90、3★ 0-10 保底条。",
            "绝区零、星穹铁道、崩坏3 的限定角色 / 武器记录加入金色 UP! 标记。ヾ(≧▽≦*)o",
        }),
        new("0.1.18", "2026-11-20", new[]
        {
            "修复扫码登录换取 cookie_token 失败（-464）：SToken v2 优先，保留 v1 与 Game Token 兜底。",
            "修复从「启动」页点击「社区」仍停留在启动页的问题。(＾▽＾)",
        }),
        new("0.1.17", "2026-11-20", new[]
        {
            "修复扫码登录后缺少 stoken / cookie_token，任务与签到无法执行。",
            "抽卡记录更新后保持当前 UID，不再自动跳转。",
            "4★ 保底条改为 0-10（0-7 绿、8-10 红）。(￣▽￣)ノ",
        }),
        new("0.1.16", "2026-11-20", new[]
        {
            "启动页后台预加载全部官方动态壁纸，切换游戏无需等待。",
            "官方换版本后自动清理旧壁纸。",
            "修复关闭到托盘后再次启动黑屏。(＾▽＾)",
        }),
        new("0.1.15", "2026-11-20", new[]
        {
            "签到触发图形验证码时在软件内完成验证并自动重试。",
            "修复一键更新无法覆盖旧版本的问题。",
            "macOS 端极光玻璃界面、抽卡页与设置页视觉重做。",
        }),
        new("0.1.13", "2026-11-20", new[]
        {
            "新增 Avalonia 跨平台 UI 与 macOS 安装包。",
            "绝区零改用 real_gacha_type，修复星级差 1；星铁、崩坏3 接入对应抽卡接口。",
            "抽卡记录新增 0-90 保底条与金色 UP! 标记。",
            "语言抽屉点击外部自动收起；新增跟随系统与 13 套配色。",
            "Avalonia 界面第一版，感谢一路催更的你们 (≧ω≦)",
        }),
        new("0.1.12", "2026-11-19", new[] { "版本迭代与稳定性修复。" }),
        new("0.1.11", "2026-11-19", new[] { "版本迭代与界面细节优化。" }),
        new("0.1.10", "2026-11-19", new[] { "版本迭代与稳定性修复。" }),
        new("0.1.9", "2026-11-19", new[] { "版本迭代与稳定性修复。" }),
        new("0.1.8", "2026-11-19", new[] { "版本迭代与稳定性修复。" }),
        new("0.1.7", "2026-11-19", new[]
        {
            "启动页接入官方壁纸接口，支持 .webm 动态壁纸与预下载提示。",
            "获取游戏改为安装弹窗。",
            "修复最小化后再次点击图标无法还原、安装弹窗按钮被裁切。",
        }),
        new("0.1.5", "2026-11-17", new[]
        {
            "首个公开版本：启动游戏、社区、抽卡记录、每日签到、HOYO-MiX、账号与设置。",
            "任务 / 签到页重做：六款游戏角色签到、米游币每日任务、多账号执行与实时日志。",
            "新增胡桃红配色、更新 / 卸载入口与多游戏运行状态。",
        }),
    };

    public ChangelogWindow()
    {
        Title = "更新日志 · MikoNest";
        Width = 640;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["PageBg"];

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "更新日志  (≧▽≦)",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });
        header.Children.Add(new TextBlock
        {
            Text = "MikoNest 从首个公开版本到 0.1.38 的更新与修复记录",
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = (Brush)Application.Current.Resources["TextSecondary"],
        });
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var list = new StackPanel();
        scroll.Content = list;
        Loaded += (_, _) => scroll.ScrollToTop();

        foreach (var e in Entries)
        {
            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBg"],
                BorderBrush = (Brush)Application.Current.Resources["CardBorder"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 13, 16, 14),
                Margin = new Thickness(0, 0, 0, 10),
            };
            var stack = new StackPanel();
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock
            {
                Text = $"v{e.Version}",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = $"  {e.Date}",
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 0, 0, 2),
                Foreground = (Brush)Application.Current.Resources["TextSecondary"],
            });
            stack.Children.Add(titleRow);

            foreach (var item in e.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock
                {
                    Text = "•",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextSecondary"],
                });
                var txt = new TextBlock
                {
                    Text = item,
                    FontSize = 12.5,
                    LineHeight = 19,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["TextSecondary"],
                };
                Grid.SetColumn(txt, 1);
                row.Children.Add(txt);
                stack.Children.Add(row);
            }

            card.Child = stack;
            list.Children.Add(card);
        }

        Content = root;
    }
}

