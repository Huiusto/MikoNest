# MikoNest · Avalonia 跨平台 UI 与 macOS 版

本目录说明 `src/MikoNest.Avalonia` 的定位、构建方式与当前完成度。

## 结构

- `src/MikoNest`：原有 WPF 桌面版（Windows），功能完整，继续作为主版本维护。
- `src/MikoNest.Avalonia`：Avalonia 11 重写的 UI 层，同一套业务服务（设置、抽卡本地库、官方壁纸同步、路径规则）被两个前端共用。
- `dist/`：交叉发布产物，含 macOS `.app` 压缩包。

## 已实现（Avalonia 版）

| 页面 | 能力 |
| --- | --- |
| 启动 | 官方版本壁纸自动同步与缓存、左侧固定尺寸圆点切换、四款游戏切换、开始游戏、安装游戏弹窗 |
| 抽卡记录 | 四列卡池统计（5★ 平均 / 5★ / 4★ / 3★）、限定池金色 `UP!` 标记、每条记录下方 0–90 保底进度条、UID 切换 |
| 设置 | 13 套配色（含跟随系统）、界面不透明度、语言列表、各游戏安装目录、关闭行为、数据目录、开源说明 |
| 账号 / 任务 / 米哈游相关 | 说明与官方入口 |

## 与 WPF 版的差异（尚未对齐）

- 动态壁纸（`.webm`）：Avalonia 无内置视频解码，当前只呈现官方壁纸的静态封面。
- 抽卡「更新记录」：跨平台版暂未接入游戏内缓存抓取（游戏目录与缓存格式按平台不同），只读取与桌面版共用的本地库，支持后续补上。
- 账号登录、每日签到、预下载进度条：仍以 WPF 版为准。
- 主题不透明度目前作用于卡片底色，尚未覆盖侧栏与「米哈游相关」内的框框。

## 构建

```powershell
$env:DOTNET_ROOT="C:\Users\<you>\.dotnet9"
dotnet build src/MikoNest.Avalonia/MikoNest.Avalonia.csproj -c Release
dotnet publish src/MikoNest.Avalonia/MikoNest.Avalonia.csproj -c Release -r osx-arm64 --self-contained -o dist/osx-arm64
dotnet publish src/MikoNest.Avalonia/MikoNest.Avalonia.csproj -c Release -r osx-x64  --self-contained -o dist/osx-x64
```

`.app` 目录结构：

```
MikoNest.app/Contents/
├── Info.plist
├── MacOS/        # 发布产物（可执行文件 MikoNest + 依赖 dll/dylib）
└── Resources/    # readme.txt
```

## 在 macOS 上首次运行

```bash
xattr -cr /Applications/MikoNest.app
chmod +x /Applications/MikoNest.app/Contents/MacOS/MikoNest
open /Applications/MikoNest.app
```

数据目录：`~/Library/Application Support/MikoNest`，与 Windows 版 `%AppData%\MikoNest` 的 `settings.json`、`gacha_records.json` 结构一致，可直接拷贝。

## 发布状态

`dist/0.1.28/MikoNest-0.1.28-osx-arm64.zip`、`dist/0.1.28/MikoNest-0.1.28-osx-x64.zip` 由 Windows 交叉发布，Mach-O 可执行文件与包结构已校验；尚未做代码签名，也未在真机 macOS 上运行验证。若要对外分发，建议在 Mac 上执行 `codesign --deep --force --sign - MikoNest.app` 或使用开发者证书公证。


