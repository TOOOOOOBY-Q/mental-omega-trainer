# 架构说明

## 总体结构

程序为单文件 WinForms 应用（`src/MOTrainer336.cs`），无第三方依赖，包含三个类型：

```text
Program        入口：DPI 感知、视觉样式、启动主窗体
TrainerForm    界面、全局热键、定时器、状态与日志
GameMemory     进程附加与内存读写（全部游戏逻辑）
```

## 数据流

```text
refreshTimer (250ms)
  └─ GameMemory.TryAttach()  按进程名 gamemd / gamemd-spawn / gameares / game 查找
       ├─ OpenProcess(VM 读写 + 查询 + 创建线程)
       └─ 校验 0x400000 处 MZ 头，确认布局受支持

featureTimer (15ms)
  └─ 已开启的开关类功能（电力 / 建造 / 超武）每 tick 重写一次目标字段，
     抵消游戏自身每帧的数值回写

热键 / 按钮
  └─ 一次性功能（资金 / 全图 / 胜利）按需调用 GameMemory 方法
```

## Ares 兼容策略

Mental Omega 3.3.6 通过 Syringe 将 Ares 注入 `gamemd.exe`，Ares 会在运行时改写游戏代码段。因此：

- 电力与超级武器不使用代码补丁，直接写 `HouseClass` 数据字段（数据模式）。
- 一键全图不修改任何代码，而是远程分配一小段 stub，调用游戏自身的 `MapClass::Reveal(CurrentPlayer)` 后释放内存。
- 调用前校验 `Reveal` 入口机器码；Ares 写入的跳转钩子（E9/EB）视为有效入口。

## 安全边界

- 仅读写游戏进程内存，不写文件、不联网、无自启动。
- 所有写操作前校验当前玩家指针有效性（`HasCurrentPlayer`）。
- 退出时恢复快速建造的现场值并注销热键。
- 管理员权限仅用于打开游戏进程句柄。

## 错误处理

所有内存操作返回 `(bool, error)`，失败原因（含 Win32 错误码）写入界面“运行记录”。同一错误不重复刷屏（`LogOnce` / `lastFeatureError` 去重）。
