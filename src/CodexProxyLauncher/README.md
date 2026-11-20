# 应用源码目录

启动器使用 C# 和 WinForms。`net48` 与 `net10.0-windows` 共用应用源码、图标、启动脚本和版本号，并使用同一命令行接口和 JSON 设置格式。无参数启动时打开设置，由用户手动启动 Codex。Windows 登录入口 `--boot` 应用静默启动和自动启动 Codex 的设置。`--tray` 直接进入托盘。单次启动和诊断使用各自的命令行选项。

- Program.cs：参数入口、退出码、消息框和诊断输出。
- LauncherOptions.cs：启动参数、默认值、长度校验与可选重启开关。
- `PowerShellLauncher.cs` 隐藏调用系统 Windows PowerShell，将代理地址和 App ID 作为数据传递，并控制执行超时。
- `Launcher.ps1` 发现 Codex App ID，核验并按需结束现有进程，再经 Windows 应用激活接口传入 Chromium 代理参数。
- TrayApplication.cs、TrayQuickMenu.cs 与 SettingsWindow.cs 管理托盘生命周期、快捷操作和设置。
- LauncherTheme.cs 管理外观、系统高对比度与窗口边框。
- LauncherSettings.cs 保存代理和启动器偏好，StartupRegistration.cs 校验、修复及更新当前用户的登录启动项。
- `ProxyDiagnostics.cs` 分别检测 TCP 连通性与经代理的 HTTPS 请求结果。Windows 激活结果由启动流程另行报告。
- App.ico 为应用和托盘图标。轻量版将 `App.NETFramework.config` 嵌入 EXE。运行时把配置写入用户临时目录，启用 Windows Forms 的每显示器 DPI 感知，正常退出时清理。`App.NETFramework.manifest` 声明 Windows 兼容性。

启动器默认在已有实例运行时给出手动处理提示。使用 `--restart-running` 可结束同一登录会话里的 Codex 安装包进程，并在确认退出后调用 Windows 应用激活接口。自检只解析脚本。

启动器为 Codex Desktop 传递桌面代理参数。后端代理与环境变量由用户另行配置。

构建和发布步骤见 [构建脚本说明](../../scripts/README.md)。
