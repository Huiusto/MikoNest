using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace MiNest.Services
{
    /// <summary>
    /// 启动游戏前静默检查 MikoNest 自身更新；发现新版本时弹出带角色插画的更新提醒。
    /// 检查失败不打扰用户，也不影响游戏启动。
    /// </summary>
    public static class UpdatePromptService
    {
        /// <summary>同一进程内两次自动检查的最小间隔，避免频繁弹窗。</summary>
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

        private static DateTime _lastCheck = DateTime.MinValue;
        private static bool _checking;

        /// <summary>返回 true 表示用户选择了更新且安装程序已启动，调用方应中止本次启动流程。</summary>
        public static async Task<bool> CheckAndPromptAsync(Window owner)
        {
            if (_checking) return false;
            var now = DateTime.UtcNow;
            if (now - _lastCheck < CheckInterval) return false;

            _checking = true;
            try
            {
                var (info, _) = await UpdateService.GetLatestAsync();
                _lastCheck = DateTime.UtcNow;
                if (info == null) return false;

                var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0, 0);
                var latest = UpdateService.ParseTag(info.Version);
                if (latest == null || latest <= current) return false;
                if (string.IsNullOrEmpty(info.AssetUrl)) return false;

                // 用户已经取消/关闭过这个版本的提示，则不再自动打扰；只有更高版本出现时才重新弹。
                var ignored = UpdateService.ParseTag(SettingsService.Settings.IgnoredUpdateVersion);
                if (ignored != null && latest <= ignored) return false;

                var win = new MiNest.Windows.UpdateWindow(current, info) { Owner = owner };
                win.ShowDialog();
                if (!win.StartedInstall)
                {
                    SettingsService.Settings.IgnoredUpdateVersion = info.Version;
                    SettingsService.Save();
                }
                return win.StartedInstall;
            }
            catch
            {
                // 网络不可用、GitHub 限流等情况一律静默跳过，不影响启动游戏。
                return false;
            }
            finally
            {
                _checking = false;
            }
        }
    }
}
