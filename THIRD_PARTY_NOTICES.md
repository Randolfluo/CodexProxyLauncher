# 第三方来源说明

## 技术方案参考

- 标题：《Codex Desktop 代理配置实践》
- 作者：Double Young（microyy）
- [原文](https://microyy.github.io/posts/codex-desktop-proxy/)
- 原文标注许可为 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)

本项目的桌面代理启动思路参考上述文章。仓库中的 C# 启动器、WinForms 界面和内嵌 PowerShell 脚本是独立实现。本仓库采用 MIT 许可，第三方材料适用各自的原始许可。

复用第三方代码、图标或文档时，应核对适用的许可证，并在对应文件记录来源、许可与更改。分发时一并提供适用的许可与署名。

## 构建依赖

轻量版使用微软的 [Microsoft.NETFramework.ReferenceAssemblies.net48 1.0.3](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net48/1.0.3) 编译，运行时由 Windows 提供。

两个 EXE 都内嵌项目的 MIT 许可和本声明。自包含版另外内嵌所用 .NET 运行时包和 Windows Desktop 运行时包中的许可文件，以及 .NET 运行时的第三方声明。
