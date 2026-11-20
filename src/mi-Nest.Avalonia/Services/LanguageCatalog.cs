using System;
using System.Collections.Generic;

namespace MiNest.Avalonia.Services;

/// <summary>跨平台语言目录（不依赖 WPF 的依赖属性，仅提供语言列表与显示名）。</summary>
public static class LanguageCatalog
{
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

    public static string DisplayOf(string? code)
    {
        foreach (var (c, d) in Languages)
            if (string.Equals(c, code ?? "", StringComparison.OrdinalIgnoreCase)) return d;
        return "跟随系统";
    }
}
