# 架构说明

## 总体结构

程序为 WinForms 应用，无第三方运行时依赖。`src/MOTrainer336.cs` 包含数据功能、热键和事件逻辑；`src/TrainerUi.cs` 包含 `TrainerForm` 的布局、状态显示和绘制控件。两项游戏线程补丁在 `src/PowerOverride.cs` 和 `src/BuildOverride.cs`，共用 `src/RemoteCallHook.cs`：

```text
Program        入口：DPI 感知、视觉样式、启动主窗体
TrainerForm    界面、全局热键、定时器、状态与日志
GameMemory     进程附加与内存读写（全部游戏逻辑）
PowerOverride  供电结算补丁及重算请求
BuildOverride  当前玩家生产进度加速
RemoteCallHook 指令校验、线程暂停、补丁开关和原指令恢复
```

## 数据流

窗体使用 TableLayoutPanel 分区和 Dock / Anchor；AutoScaleMode 为 None，界面和字体固定为像素尺寸，避免随系统 DPI 单独放大文字；超出屏幕高度时通过外层 AutoScroll 容器访问全部内容。功能开关和按钮继承原生 CheckBox / Button，保留键盘、状态和可访问名称。隔离预览构造参数仅供测试使用，不启动定时器或注册全局热键。

```text
refreshTimer (250ms)
  └─ GameMemory.TryAttach()  按进程名 gamemd / gamemd-spawn / gameares / game 查找
       ├─ OpenProcess(VM 读写 + 查询 + 创建线程)
       └─ 校验 0x400000 处 MZ 头，确认布局受支持

featureTimer (15ms)
  ├─ 超武每 tick 重写目标字段
  ├─ 电力仅维护补丁启用状态及玩家变化，不在定时器中改写电量
  └─ 建造只在开关时写控制位；定时器处理异常恢复，不外部遍历工厂

热键 / 按钮
  └─ 一次性功能（资金 / 全图 / 胜利）按需调用 GameMemory 方法
```

## Ares 兼容策略

Mental Omega 3.3.6 通过 Syringe 将 Ares 注入 `gamemd.exe`，Ares 会在运行时改写游戏代码段。因此：

- 超级武器直接写 `HouseClass` 数据字段。
- 电力在 `UpdatePower` 的 `0x508D81` 单条调用位置安装经签名检查的跳转，在游戏线程中、下游工厂计时和状态判断前，仅为当前玩家写入电量。跳板保存标志、保持寄存器，并重放原调用。运行时原字节不匹配时拒绝安装。
- 电力开关切换请求游戏重算电力及雷达。关闭保留禁用的跳板供下次复用，正常退出恢复原调用。已被游戏执行过的跳板保留到游戏进程结束，避免已有执行流返回到已释放的内存；每个功能每个修改器连接占用 8 KiB，代码页 RX、控制页 RW。
- 建造在 `FactoryClass::Update` 的 `0x4C9B68` 单条计时调用位置安装经签名检查的跳转，仅当前玩家、存在生产对象、未暂停且未完成的工厂生效。将进度置 53/54 并清零本步剩余时间，重放原计时调用；游戏在最后一步检查、扣除剩余费用并设置完成状态，资金不足则继续等待。所有生产类别走同一函数。
- 建造不改工厂计数、无需保存或恢复工厂快照；对象读取与修改在游戏线程内完成。关闭保留当前进度并停止后续加速。
- 一键全图不修改任何代码，而是远程分配一小段 stub，调用游戏自身的 `MapClass::Reveal(CurrentPlayer)` 后释放内存。
- 调用前校验 `Reveal` 入口机器码；Ares 写入的跳转钩子（E9/EB）视为有效入口。

## 安全边界

- 仅读写游戏进程内存，不写文件、不联网、无自启动。
- 数据功能写入前校验当前玩家指针范围（`HasCurrentPlayer`）；这不等价于完整的游戏版本验证。
- 安装或拆除电力、建造跳转时短暂停止目标线程，检查指令归属，写入后刷新指令缓存，再恢复已暂停的线程。
- 退出时尝试恢复电力及建造原指令并注销热键。任一恢复失败时取消退出，界面同步两个实际开关状态并保留诊断。
- 管理员权限仅用于打开游戏进程句柄。

## 错误处理

所有内存操作返回 `(bool, error)`，失败原因（含 Win32 错误码）写入界面“运行记录”。同一错误不重复刷屏（`LogOnce` / `lastFeatureError` 去重）。
