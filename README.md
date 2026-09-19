# 心灵终结单机修改器 (Mental Omega 3.3.6 Trainer)

面向《命令与征服：心灵终结》3.3.6（Mental Omega，基于 Yuri's Revenge 1.001 + Ares）的 Windows 单机修改器，提供资金、电力、快速建造、超级武器、一键全图和立即胜利六项功能。

> 仅限本地单机使用。程序不联网、无广告、无更新检查。

## Overview

本工具替代一个 2013 年编写的旧版 VB6 修改器。旧版在非简体中文系统上界面乱码，且通过窗口标题识别游戏进程，在 Mental Omega 3.3.6 下完全失效。本项目改为按进程名自动附加 `gamemd.exe`，所有功能在与 Ares 兼容的数据模式下实现。

适用场景：单机战役、遭遇战中降低重复操作或测试游戏机制。

## Features

| 功能 | 快捷键 | 说明 |
| --- | --- | --- |
| 资金 | F5 | 将当前玩家资金设为界面指定数值 |
| 无限电力 | F6 | 锁定电力输出为正、消耗为零（数据模式） |
| 一键全图 | F7 | 调用游戏自身的地图揭开函数，仅对当前玩家生效，一次性操作 |
| 快速建造 | F9 | 锁定各类工厂建造速度，关闭时自动恢复现场值 |
| 无限超级武器 | F10 | 保持超级武器处于充能完成状态（数据模式） |
| 立即胜利 | F11 | 写入胜利标记，直接结束当前对局 |

其他已实现并验证的行为：

- 自动识别并重连 `gamemd.exe`（无需手动选择进程）
- 进入新对局时自动刷新快速建造的现场值
- 退出时自动恢复补丁并注销全局热键
- 固定大小深色界面，适配 Windows 11 与高 DPI
- 运行记录与“一键复制诊断”
- 启动时自动申请管理员权限（游戏进程需要）

## Requirements

- Windows 10 / Windows 11
- .NET Framework 4.x（Windows 内置，无需单独安装）
- 《心灵终结》3.3.6（Yuri's Revenge 1.001 + Ares 3.0）
- 管理员权限（启动时弹出 UAC）

仅支持上述游戏版本；其他版本的内存布局不同，功能不会生效。

## Installation

从 Release 下载 `心灵终结修改器_MO336.exe`，放到任意目录即可，无需安装。

首次启动可能出现 SmartScreen 提示：发布版本使用 `CN=TOOOOOOBY` 自签名证书，不属于公共 CA 信任链，属预期现象。

## Quick Start

1. 启动《心灵终结》并进入一局单机游戏。
2. 运行修改器（同意 UAC 提权），状态栏显示“已连接”。
3. 按对应快捷键或点击界面按钮使用功能。

详细说明见 [docs/usage.md](docs/usage.md)。

## Build from Source

需要 Windows 与 .NET Framework 4.x 自带的 `csc.exe`（无需安装 Visual Studio）：

```powershell
git clone <repository-url>
cd mental-omega-trainer
powershell -ExecutionPolicy Bypass -File scripts\Build.ps1
```

产物输出到 `release\心灵终结修改器_MO336.exe`（已在 CI 中验证；发布时附带 SHA-256）。

签名属于可选步骤，需要自备代码签名证书，详见 [scripts/Build.ps1](scripts/Build.ps1) 头部注释。

## Project Structure

```text
├── src/MOTrainer336.cs    # 全部源代码（单文件 WinForms 程序）
├── src/app.manifest       # 管理员权限与 DPI 清单
├── scripts/Build.ps1      # 构建与可选签名脚本
├── docs/                  # 使用说明、架构与内存偏移文档
└── .github/               # CI 与 Issue/PR 模板
```

## Troubleshooting

- **提示“请先进入一局游戏”**：游戏已连接但不在对局中，进入对局后再操作。
- **提示版本不匹配**：游戏不是 3.3.6 / 1.001 布局，修改器会拒绝写入以保护游戏进程。
- **快捷键无响应**：界面按钮与快捷键等价，可直接点击；并确认没有其他程序占用同一热键。
- **SmartScreen / 杀毒软件提示**：程序需要写其他进程内存并申请管理员权限，属该类工具的正常特征；发布版本已经过 Microsoft Defender 扫描。

## Known Limitations

- 仅支持 Mental Omega 3.3.6（Yuri's Revenge 1.001 + Ares），不支持其他游戏版本。
- 仅限 Windows；仅限单机，不能也不应用于联机对战。
- “一键全图”调用游戏内部函数，属于一次性操作，重新开始对局后需再次执行。
- 发布签名为自签名证书，Windows 仍可能提示证书链不受信任。

## Security

本工具会对游戏进程执行内存读写与远程线程调用，且以管理员身份运行。请仅从本仓库 Release 获取程序并核对 SHA-256。安全政策见 [SECURITY.md](SECURITY.md)。

## License

当前版本暂未附带开源许可证；在未补充 LICENSE 文件前，默认保留所有权利。仅允许从本仓库下载并按上述用途使用。

## Acknowledgements

架构与兼容性思路参考了 [RA2YurisRevengeTrainer](https://github.com/AdjWang/RA2YurisRevengeTrainer)（Yuri 1.001 + Ares 修改器，未复制其代码）。
