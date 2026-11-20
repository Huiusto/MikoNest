using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MiNest.Services;

/// <summary>
/// 界面语言本地化：默认跟随系统，可在 设置-语言 切换为 简体中文/繁体中文/英文/日文/韩文/俄文（重启后生效）。
/// 框架级文本（侧栏导航、网页按钮提示等）已多语言化；页面内容文本逐步覆盖。
/// </summary>
public static class LocalizationService
{
    /// <summary>语言代码列表："" = 跟随系统</summary>
    public static readonly (string Code, string Display)[] Languages =
    {
        ("", "跟随系统"),
        ("zh-Hans", "简体中文"),
        ("zh-Hant", "繁體中文"),
        ("ja", "日本語"),
        ("en", "English"),
        ("ko", "한국어"),
        ("ru", "Русский"),
        ("de", "Deutsch"),
        ("it", "Italiano"),
        ("th", "ภาษาไทย"),
        ("vi", "Tiếng Việt"),
    };

    /// <summary>当前生效语言代码（Settings.Language；"" = 跟随系统）</summary>
    public static string CurrentLang { get; private set; } = "";

    /// <summary>解析后的文化名（zh-Hans / zh-Hant / en / ja / ko / ru）</summary>
    public static string EffectiveLang { get; private set; } = "zh-Hans";

    /// <summary>按 Settings.Language 初始化（App 启动时调用）。</summary>
    public static void Init(string language)
    {
        CurrentLang = language ?? "";
        EffectiveLang = ResolveEffective(CurrentLang);
    }

    private static string ResolveEffective(string code)
    {
        if (!string.IsNullOrEmpty(code)) return code;
        // 跟随系统：按当前 UI 文化就近映射
        var name = CultureInfo.CurrentUICulture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
                ? "zh-Hant" : "zh-Hans";
        }
        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
        if (name.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) return "ru";
        if (name.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return "de";
        if (name.StartsWith("it", StringComparison.OrdinalIgnoreCase)) return "it";
        if (name.StartsWith("th", StringComparison.OrdinalIgnoreCase)) return "th";
        if (name.StartsWith("vi", StringComparison.OrdinalIgnoreCase)) return "vi";
        return "en";
    }

    /// <summary>取翻译文本；找不到键时原样返回 key。</summary>
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        if (Tables.TryGetValue(EffectiveLang, out var table) && table.TryGetValue(key, out var text))
            return text;
        if (Tables.TryGetValue("zh-Hans", out var zh) && zh.TryGetValue(key, out var zhText))
            return zhText;
        return key;
    }

    /// <summary>侧栏中文导航 key → 翻译键（如 "社区" → "nav_community"）。</summary>
    public static string NavKey(string chineseKey) => chineseKey switch
    {
        "启动" => "nav_launch",
        "社区" => "nav_community",
        "抽卡记录" => "nav_gacha",
        "任务/签到" => "nav_tasks",
        "米哈游相关" => "nav_mhy",
        "账号" => "nav_account",
        "设置" => "nav_settings",
        _ => chineseKey,
    };

    // ---------- 附加属性：RadioButton/TextBlock 等绑定翻译键 ----------

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(LocalizationService),
        new PropertyMetadata(null, OnKeyChanged));

    public static void SetKey(DependencyObject obj, string value) => obj.SetValue(KeyProperty, value);
    public static string GetKey(DependencyObject obj) => (string)obj.GetValue(KeyProperty);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe && e.NewValue is string key)
        {
            // 供样式内 TextBlock 直接取用（通过 L10nConverter 绑定）
        }
    }

    // ---------- 字符串表 ----------

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new()
    {
        ["zh-Hans"] = new Dictionary<string, string>
        {
            ["nav_launch"] = "启动",
            ["nav_community"] = "社区",
            ["nav_gacha"] = "抽卡记录",
            ["nav_tasks"] = "任务/签到",
            ["nav_mhy"] = "米哈游相关",
            ["nav_account"] = "账号",
            ["nav_settings"] = "设置",
            ["web_back"] = "后退",
            ["web_forward"] = "向前",
            ["web_refresh"] = "刷新",
        },
        ["zh-Hant"] = new Dictionary<string, string>
        {
            ["nav_launch"] = "啟動",
            ["nav_community"] = "社群",
            ["nav_gacha"] = "抽卡記錄",
            ["nav_tasks"] = "任務/簽到",
            ["nav_mhy"] = "米哈遊相關",
            ["nav_account"] = "帳號",
            ["nav_settings"] = "設定",
            ["web_back"] = "返回",
            ["web_forward"] = "前進",
            ["web_refresh"] = "重新整理",
        },
        ["en"] = new Dictionary<string, string>
        {
            ["nav_launch"] = "Launch",
            ["nav_community"] = "Community",
            ["nav_gacha"] = "Gacha Records",
            ["nav_tasks"] = "Tasks / Check-in",
            ["nav_mhy"] = "HoYo Links",
            ["nav_account"] = "Account",
            ["nav_settings"] = "Settings",
            ["web_back"] = "Back",
            ["web_forward"] = "Forward",
            ["web_refresh"] = "Refresh",
        },
        ["ja"] = new Dictionary<string, string>
        {
            ["nav_launch"] = "起動",
            ["nav_community"] = "コミュニティ",
            ["nav_gacha"] = "ガチャ記録",
            ["nav_tasks"] = "任務/デイリー",
            ["nav_mhy"] = "HoYo 関連",
            ["nav_account"] = "アカウント",
            ["nav_settings"] = "設定",
            ["web_back"] = "戻る",
            ["web_forward"] = "進む",
            ["web_refresh"] = "更新",
        },
        ["ko"] = new Dictionary<string, string>
        {
            ["nav_launch"] = "시작",
            ["nav_community"] = "커뮤니티",
            ["nav_gacha"] = "가챠 기록",
            ["nav_tasks"] = "임무/출석",
            ["nav_mhy"] = "HoYo 관련",
            ["nav_account"] = "계정",
            ["nav_settings"] = "설정",
            ["web_back"] = "뒤로",
            ["web_forward"] = "앞으로",
            ["web_refresh"] = "새로고침",
        },
        ["ru"] = new Dictionary<string, string>
        {
            ["nav_launch"] = "Запуск",
            ["nav_community"] = "Сообщество",
            ["nav_gacha"] = "История молитв",
            ["nav_tasks"] = "Задания/Отметки",
            ["nav_mhy"] = "HoYo ссылки",
            ["nav_account"] = "Аккаунт",
            ["nav_settings"] = "Настройки",
            ["web_back"] = "Назад",
            ["web_forward"] = "Вперёд",
            ["web_refresh"] = "Обновить",
        },
        ["de"] = new Dictionary<string, string>
        {
            ["nav_launch"] = "Start",
            ["nav_community"] = "Community",
            ["nav_gacha"] = "Wunschverlauf",
            ["nav_tasks"] = "Aufgaben/Check-in",
            ["nav_mhy"] = "HoYo-Links",
            ["nav_account"] = "Konto",
            ["nav_settings"] = "Einstellungen",
            ["web_back"] = "Zurück",
            ["web_forward"] = "Vorwärts",
            ["web_refresh"] = "Aktualisieren",
        },
        ["it"] = new Dictionary<string, string>
        {
            ["nav_launch"] = "Avvia",
            ["nav_community"] = "Community",
            ["nav_gacha"] = "Cronologia desideri",
            ["nav_tasks"] = "Attività/Presenze",
            ["nav_mhy"] = "Link HoYo",
            ["nav_account"] = "Account",
            ["nav_settings"] = "Impostazioni",
            ["web_back"] = "Indietro",
            ["web_forward"] = "Avanti",
            ["web_refresh"] = "Aggiorna",
        },
        ["th"] = new Dictionary<string, string>
        {
            ["nav_launch"] = "เริ่มเกม",
            ["nav_community"] = "คอมมิวนิตี้",
            ["nav_gacha"] = "ประวัติการอัญเชิญ",
            ["nav_tasks"] = "ภารกิจ/เช็คอิน",
            ["nav_mhy"] = "ลิงก์ HoYo",
            ["nav_account"] = "บัญชี",
            ["nav_settings"] = "ตั้งค่า",
            ["web_back"] = "ย้อนกลับ",
            ["web_forward"] = "ไปข้างหน้า",
            ["web_refresh"] = "รีเฟรช",
        },
        ["vi"] = new Dictionary<string, string>
        {
            ["nav_launch"] = "Khởi chạy",
            ["nav_community"] = "Cộng đồng",
            ["nav_gacha"] = "Lịch sử cầu nguyện",
            ["nav_tasks"] = "Nhiệm vụ/Điểm danh",
            ["nav_mhy"] = "Liên kết HoYo",
            ["nav_account"] = "Tài khoản",
            ["nav_settings"] = "Cài đặt",
            ["web_back"] = "Quay lại",
            ["web_forward"] = "Tiến tới",
            ["web_refresh"] = "Làm mới",
        },
    };
}

/// <summary>键 → 翻译文本（找不到时原样返回）。用于样式/绑定。</summary>
public class L10nConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => LocalizationService.Get(value as string ?? parameter as string ?? "");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
