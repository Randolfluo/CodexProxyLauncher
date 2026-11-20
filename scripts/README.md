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

## CI/CD

本地和 GitHub Actions 使用同一个验证脚本。每次运行都会在 `artifacts/ci/` 的独立子目录构建轻量版和自包含版，校验发布目录和嵌入的许可文本，再运行兼容性测试、界面与回环代理冒烟测试和文档链接检查。

```powershell
.\scripts\verify-release.ps1
.\scripts\verify-release.ps1 -Tag v0.1.0
```

指定标签时，脚本还会核对标签与包版本。所有检查通过后，最后一行输出 `release-assets` 目录的路径。目录包含两个用于下载的 EXE，每个 EXE 各有一个同名 `.exe.sha256` 文件。各项测试见 [测试说明](../tests/README.md)。

[GitHub Actions 工作流](../.github/workflows/release.yml) 在推送 `main`、向 `main` 提交 PR 或推送以 `v` 开头的标签时执行验证。标签对应的验证通过后，发布任务会将两个 EXE 及对应的校验文件附到 GitHub Release，源码归档由 GitHub 自动生成。标签取自项目的 `Version`，例如 `0.1.0` 对应 `v0.1.0`。

```powershell
git tag -a v0.1.0 -m "Release v0.1.0"
git push origin refs/tags/v0.1.0
```

工作流通过 `gh release create --generate-notes` 让 GitHub 生成更新说明。可在 Release 页右上角选择编辑，填写面向用户的 Markdown 更新说明。[GitHub 自动生成发布说明](https://docs.github.com/en/repositories/releasing-projects-on-github/automatically-generated-release-notes)

## 清理

先用 `WhatIf` 预览，再执行清理。

```powershell
.\scripts\clean.ps1 -WhatIf
.\scripts\build.ps1 -Action Clean
```

清理范围为固定清单中的中间输出和 SDK 下载临时文件。发布目录、已安装的 SDK 和 NuGet 缓存可供后续构建使用。
