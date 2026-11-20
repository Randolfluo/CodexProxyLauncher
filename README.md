# CodexProxyLauncher

为 Windows 11 x64 上的 Codex Desktop 指定 HTTP 代理的社区启动器。

![Codex Proxy Launcher 设置界面](docs/images/settings.png)

## 安装

先安装 Microsoft Store 版 Codex Desktop，并准备可用的 HTTP 代理。两个版本均使用系统自带的 Windows PowerShell。

| 版本 | 下载文件 | Windows 11 x64 运行时 |
| --- | --- | --- |
| 轻量版 | `CodexProxyLauncher-<version>-win-x64-net48.exe` | 21H2 自带 .NET Framework 4.8，22H2 及更新版自带 4.8.1，均满足轻量版要求。[微软说明](https://learn.microsoft.com/zh-cn/dotnet/framework/get-started/system-requirements#client-operating-systems) |
| 完整版 | `CodexProxyLauncher-<version>-win-x64-net10.exe` | 内置 .NET 10 桌面运行时。[Windows 版本要求](https://learn.microsoft.com/zh-cn/dotnet/core/install/windows#supported-versions) |

下载对应的 EXE，放在固定目录后双击打开。EXE 未签名，可用 `Get-FileHash -Algorithm SHA256` 计算哈希，与对应的 `.exe.sha256` 文件核对。[GitHub Releases](https://github.com/Randolfluo/CodexProxyLauncher/releases)

## 使用

填写代理主机和端口，默认地址为 `http://127.0.0.1:7897`。有效设置会自动保存。点击「检测代理」可检查 TCP 端口和经代理访问 `api.openai.com` 的结果。点击「使用代理启动 Codex」后，界面会显示 Windows 是否接受启动请求。

关闭窗口后，启动器留在系统托盘。左键打开设置，右键可启动 Codex 或退出。启动前清理旧进程和登录自启默认关闭。开启登录自启后，启动器会在登录 Windows 时进入托盘。

### 命令行与快捷方式

两种方式都可启动 Codex 一次。`--proxy` 指定本次使用的代理，省略时使用内置默认地址 `http://127.0.0.1:7897`。

在 EXE 所在目录打开 PowerShell，将文件名和代理地址换成实际值。

```powershell
.\CodexProxyLauncher-0.1.0-win-x64-net48.exe --launch --proxy http://127.0.0.1:7890
```

创建 Windows 快捷方式，在「属性 → 目标」中填写带引号的实际 EXE 路径，后接空格和参数。

```text
"C:\Tools\Codex Proxy Launcher\CodexProxyLauncher-0.1.0-win-x64-net48.exe" --launch --proxy http://127.0.0.1:7890
```

Codex 已运行时，启动器默认提示先退出旧实例。添加 `--restart-running` 会强制结束旧进程后重新启动，可能中断正在运行的任务。

## 从源码构建

在 Windows x64 上使用 PowerShell 7 和 `global.json` 指定的 .NET 10 SDK。在仓库根目录运行以下命令。[SDK 配置](global.json)

```powershell
.\scripts\build.ps1 -Action Package && .\scripts\build.ps1 -Action Publish
```

`Package` 生成轻量版 ZIP，`Publish` 生成完整版 EXE。运行 `.\scripts\verify-release.ps1` 可构建并验证两个发布版本。[构建说明](scripts/README.md) · [测试说明](tests/README.md)

## 来源与许可

项目采用 MIT 许可。桌面代理启动方案参考 Double Young（microyy）的《Codex Desktop 代理配置实践》（CC BY 4.0）。[MIT 许可](LICENSE) · [第三方来源说明](THIRD_PARTY_NOTICES.md)
