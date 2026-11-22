using System;

namespace MiNest.Services;

/// <summary>
/// 更新安装包下载进度广播：更新弹窗下载时把 0–100 的百分比同步给设置页的「检查更新」按钮。
/// 传 -1 表示结束或失败，接收方应恢复按钮原文。
/// </summary>
public static class UpdateProgressService
{
    public static event Action<int>? PercentChanged;

    public static void Report(int percent)
    {
        if (percent < 0) percent = -1;
        else if (percent > 100) percent = 100;
        PercentChanged?.Invoke(percent);
    }
}
