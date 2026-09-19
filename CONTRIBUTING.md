# 贡献指南

欢迎 Issue 与 Pull Request。本项目为个人维护的小工具，流程保持简单。

## 开发环境

- Windows 10 / 11
- .NET Framework 4.x（自带 `csc.exe`，构建不依赖 Visual Studio）

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Build.ps1
```

本地界面测试可用 `-NoAdmin` 生成不弹 UAC 的临时版本（不要分发该版本）。

## 提交约定

- 在功能分支上开发，通过 Pull Request 合入。
- 提交信息使用中文或英文简述变更，如 `fix: ...` / `feat: ...` / `docs: ...`。
- 修改内存地址或偏移时，必须同步更新 [docs/memory-offsets.md](docs/memory-offsets.md)。

## 代码风格

- 源文件使用 UTF-8、Tab 缩进（见 `.editorconfig`）。
- 注释只写非显然的设计理由、兼容性约束与安全原因。

## 限制

- 不接受联机对战相关功能。
- 不接受引入联网、广告或数据收集的改动。
- 涉及新游戏版本支持的改动，请附实际对局测试结果。
