# 测试目录

## 轻量包

在项目根目录用 PowerShell 7 创建轻量包，将 `artifacts/release/` 下的 ZIP 解压到空的 `artifacts/verify-win-x64/`，然后指定解压出的 EXE。

```powershell
.\scripts\build.ps1 -Action Package
$exe = '.\artifacts\verify-win-x64\CodexProxyLauncher.exe'
.\tests\compatibility.ps1 -Executable $exe
powershell.exe -NoProfile -STA -File .\tests\lightweight-smoke.ps1 -Executable $exe -CheckProxy
```

`compatibility.ps1` 在 `--self-test` 模式检查参数、退出码、重启开关和内嵌脚本。`payload-harness.ps1` 由它调用，使用替身验证应用发现、进程结束、超时、并发启动及 Windows 激活参数。

`lightweight-smoke.ps1` 使用 Windows PowerShell 验证单文件 EXE 的目标框架和内嵌 DPI 配置、既有 JSON 设置和错误回退、设置窗口及托盘菜单初始化。`-CheckProxy` 另外在本地回环地址验证代理认证、HTTPS 隧道拒绝和请求超时。

测试使用临时配置和进程替身。实际 Codex 启动和重启、登录自启及跨屏 DPI 需要在目标设备验证。

## 文档链接

在项目根目录用 PowerShell 7 检查本地 Markdown 链接。

```powershell
.\tests\docs-links.ps1
```
