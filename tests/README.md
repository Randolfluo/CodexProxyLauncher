# 测试目录

## 完整发布验证

在 Windows 上用 PowerShell 7 从项目根目录运行 `.\scripts\verify-release.ps1`。加上 `-Tag v0.1.1` 可以同时校验发布标签。脚本构建 .NET Framework 4.8 轻量版和 .NET 10 自包含版。

`release-package.ps1` 检查轻量版的中间 ZIP。`release-assets.ps1` 验证发布目录中的两个 EXE、各自的 SHA-256 校验文件和内嵌许可内容。兼容性测试使用两个最终 EXE，`StartupBehavior` 验证两个版本的启动和退出行为，界面和代理冒烟测试使用轻量版。所有测试通过后，脚本输出发布文件目录。[构建脚本说明](../scripts/README.md#cicd)

## 轻量包

在项目根目录用 PowerShell 7 创建轻量包，将 `artifacts/release/` 下的 ZIP 解压到空的 `artifacts/verify-win-x64/`，然后指定解压出的 EXE。

```powershell
.\scripts\build.ps1 -Action Package
$exe = '.\artifacts\verify-win-x64\CodexProxyLauncher.exe'
.\tests\compatibility.ps1 -Executable $exe
powershell.exe -NoProfile -STA -File .\tests\lightweight-smoke.ps1 -Executable $exe -CheckProxy
```

`compatibility.ps1` 在 `--self-test` 模式检查参数、退出码、重启开关和内嵌脚本。`payload-harness.ps1` 由它调用，使用替身验证应用发现、进程结束、超时、并发启动及 Windows 激活参数。

`lightweight-smoke.ps1` 使用 Windows PowerShell 验证单文件 EXE 的目标框架和内嵌 DPI 配置、既有 JSON 设置和错误回退、设置窗口及托盘菜单。`-CheckProxy` 另外在本地回环地址验证代理认证、HTTPS 隧道拒绝和请求超时。

`tray-menu.ps1` 检查浅色、深色和高对比度渲染、键盘导航、代理地址提示、启动中状态和 100%／200% 布局。轻量包的冒烟测试会调用它。也可以在构建 .NET 10 版本后，用基于 .NET 10 的 PowerShell 7 单独运行。

```powershell
.\tests\tray-menu.ps1 -AssemblyPath .\src\CodexProxyLauncher\bin\Release\net10.0-windows\CodexProxyLauncher.dll
```

`StartupBehavior` 使用临时设置、临时注册表键和 Codex 启动替身，检查旧配置、登录登记修复、手动打开与登录自启、重复启动及托盘退出。窗口测试覆盖版本号、启动时间、重要选项的问号说明和控件动效。实际 Windows 登录验证在独立的测试会话运行。开启两项自启后重新登录，确认启动器和 Codex 依次启动，再按静默开关的状态核对窗口或托盘。实际 Codex 启动和重启及跨屏 DPI 也需在目标设备验证。

## 文档链接

在项目根目录用 PowerShell 7 检查 README 的下载入口、交互预览入口、本地 Markdown 链接和图片，以及交互预览的版本号、嵌入片段和控件引用。

```powershell
.\tests\docs-links.ps1
```
