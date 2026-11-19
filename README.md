# CodexProxyLauncher

Windows 桌面代理启动器，可为 Codex Desktop 指定 HTTP 代理，并按需重启已有实例。

## 安装

轻量版面向 Windows 11 x64，使用系统自带的 .NET Framework 4.8 或 4.8.1 和 Windows PowerShell。请先安装 Codex Desktop，并准备可用的 HTTP 代理。[Windows 运行时说明](https://learn.microsoft.com/zh-cn/dotnet/framework/install/on-windows-and-server#windows-11)

1. 从 GitHub Release 下载 `CodexProxyLauncher-<version>-win-x64.zip`，或按下方步骤从源码构建。
2. 将整个 ZIP 解压到固定目录。轻量版的 DPI 配置已内嵌在 `CodexProxyLauncher.exe` 中。
3. 双击 `CodexProxyLauncher.exe` 打开设置。当前 EXE 未签名。

## 使用

在设置窗口填写代理主机和端口，默认地址是 `http://127.0.0.1:7897`。有效修改会自动保存，点击「使用代理启动 Codex」即可启动。

- 点击「检测代理」查看 TCP 端口和经代理访问 `api.openai.com` 的结果。界面另行显示最近一次 Windows 启动请求的结果。
- 关闭设置窗口后，启动器收进系统托盘。左键点击图标打开设置，右键可使用代理启动 Codex、打开设置或退出。
- 外观默认跟随系统，也可选择浅色或深色。设置窗口和托盘菜单同步切换，选择会自动保存。
- 「启动前清理旧进程」和「登录时启动启动器」默认关闭。开启登录自启后，登录 Windows 时启动器进入托盘。移动便携版前可先关闭登录自启，再在新位置开启。

### 命令行与快捷方式

`--launch` 使用默认代理单次启动。指定代理或应用 ID 也会直接执行单次启动。遇到已有实例时，命令行默认提示手动退出并返回退出码 `3`。

在解压目录打开 PowerShell 后运行所需命令。

```powershell
& '.\CodexProxyLauncher.exe' --launch
& '.\CodexProxyLauncher.exe' -Proxy 'http://127.0.0.1:7890'
& '.\CodexProxyLauncher.exe' --restart-running --proxy 'http://127.0.0.1:7890'
& '.\CodexProxyLauncher.exe' --self-test
& '.\CodexProxyLauncher.exe' --self-test --help
```

用 Windows 快捷方式自动重启时，将 `--restart-running` 加在「目标」中带引号的 EXE 路径之后，中间留一个空格。此开关会强制结束当前登录会话中属于 Codex 安装包且名称包含 `codex` 或 `chatgpt` 的进程，确认退出后再请求启动。仍需手动结束时，提示会列出进程名和 PID。执行前请保存正在进行的工作。

## 从源码构建

### 准备构建环境

在 Windows x64 上安装 PowerShell 7（`pwsh`）和与 [global.json](global.json) 匹配的 .NET 10 SDK（`10.0.403` 或同一 `10.0.4xx` 系列的更高补丁版本）。[PowerShell 7 安装说明](https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell-on-windows)

首次构建时，NuGet 会还原轻量版所需的 .NET Framework 4.8 引用程序集，以及完整版所需的 Windows 运行时包。

<details>
<summary>SDK 安装与版本检查</summary>

可用 WinGet 安装兼容的 x64 SDK。[微软 .NET Windows 安装说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows)

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact --version 10.0.404 --architecture x64 --source winget
```

也可以从微软下载页的 **Build apps - SDK** 区域选择 Windows x64 安装包。[微软 .NET 10 下载页](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) · [.NET SDK 10.0.404 Windows x64 安装包](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-10.0.404-windows-x64-installer)

安装后重新打开 PowerShell 7，在仓库根目录运行 `dotnet --version`。找不到兼容版本时，用 `dotnet --list-sdks` 查看已安装的 SDK。

使用项目本地 SDK 时，从微软下载页选 Windows x64 **Binaries** ZIP，解压到 `.tools\dotnet`。构建脚本会优先使用其中的 `dotnet.exe`，可在仓库根目录运行以下命令确认。

```powershell
.\.tools\dotnet\dotnet.exe --version
```

</details>

### 一次构建轻量版和完整版

在仓库根目录用 PowerShell 7 执行。轻量版打包成功后，命令会继续生成完整版。

```powershell
.\scripts\build.ps1 -Action Package && .\scripts\build.ps1 -Action Publish
```

| 版本 | 运行时 | 默认产物 |
| --- | --- | --- |
| 轻量版（`Package`） | 系统 .NET Framework 4.8 或 4.8.1 | `artifacts/release/CodexProxyLauncher-<version>-win-x64.zip` |
| 完整版（`Publish`） | 内置 .NET 10 运行时 | `artifacts/win-x64/CodexProxyLauncher.exe` |

也可单独运行 `Package` 或 `Publish`。构建选项、发布和清理步骤见 [构建脚本说明](scripts/README.md)。

### 验证

解压本次生成的轻量 ZIP，按 [测试说明](tests/README.md) 运行兼容性和冒烟测试。测试脚本可捕获 GUI 子系统 EXE 的自检输出。

## 来源与许可

方案参考 Double Young（microyy）的 [《Codex Desktop 代理配置实践》](https://microyy.github.io/posts/codex-desktop-proxy/)，原文标注 CC BY 4.0。

本仓库新增内容采用 [MIT](LICENSE)。第三方内容与复用要求见 [第三方来源说明](THIRD_PARTY_NOTICES.md)。这是独立社区项目，不代表 OpenAI 或原文作者。
