# AGENTS.md

CodexProxyLauncher 是 Windows 上的 C# WinForms 启动器，源码位于 `src/CodexProxyLauncher/`，同时支持 `net48` 和 `net10.0-windows`。

## 注意事项

- 每次改动都必须编写或更新相关测试，并在交付前确保所有测试和验证均已通过。
- 每次改动完成并通过验证后，都必须创建一个对应的 Git commit。提交仅包含本次改动，便于追踪和回滚。

## 界面预览

- 用浏览器打开 `docs/ui-preview.html` 可以试用设置窗口、托盘菜单和弹窗。需要在 Codex 聊天中展示时，复用 `<!-- BEGIN CODEX PREVIEW -->` 和 `<!-- END CODEX PREVIEW -->` 之间的片段。讨论局部设计时，按涉及的区域查阅预览。
- 修改用户可见的布局、文案、主题或交互反馈时，同步更新预览中的对应区域，检查受影响的布局和操作，并与实现及相关测试一起提交。涉及 DPI 或本机窗口行为时，用实际 WinForms 窗口验证。
