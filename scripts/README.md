# 构建脚本

在仓库根目录使用 PowerShell 7。`build.ps1` 默认执行 .NET 10 的 Release 构建，优先使用 `.tools/dotnet/dotnet.exe`，也可使用 PATH 中的 `dotnet`。配置要求见 [项目 README](../README.md)。

## 构建与发布

```powershell
.\scripts\build.ps1 -Action Build
.\scripts\build.ps1 -Action Run
.\scripts\build.ps1 -Action Package
.\scripts\build.ps1 -Action Publish
```

- `Build` 和 `Run` 使用 `net10.0-windows` 目标。
- `Package` 生成 Windows x64 轻量 ZIP，包含单文件 EXE `CodexProxyLauncher.exe`、MIT 许可和第三方来源说明。输出目录中的同一份 EXE 可直接运行。
- `Publish` 生成压缩的 .NET 10 自包含单文件，默认输出到 `artifacts/win-x64/`。可用 `-Runtime win-arm64` 生成 ARM64 完整版，运行行为请在目标设备上验证。

`Package` 和 `Publish` 均接受 `-OutputDirectory`。轻量 ZIP 默认输出到 `artifacts/release/`，文件名和 EXE 的版本由 [项目文件](../src/CodexProxyLauncher/CodexProxyLauncher.csproj) 中的 `Version` 提供。

发布前将 ZIP 解压到单独目录，按 [测试说明](../tests/README.md) 验证，再将 ZIP 附在同版本源码标签对应的 GitHub Release。

## 清理

先用 `WhatIf` 预览，再执行清理。

```powershell
.\scripts\clean.ps1 -WhatIf
.\scripts\build.ps1 -Action Clean
```

清理范围为固定清单中的中间输出和 SDK 下载临时文件。发布目录、已安装的 SDK 和 NuGet 缓存可供后续构建使用。
