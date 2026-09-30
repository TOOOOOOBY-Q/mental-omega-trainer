# 内存地址与偏移

以下数值仅适用于 Yuri's Revenge 1.001 / Mental Omega 3.3.6 的 `gamemd.exe` 布局，定义于 `GameMemory`。游戏或 Ares 更新后必须重新核对。

## 全局地址

| 名称 | 地址 (十进制) | 地址 (十六进制) | 含义 |
| --- | --- | --- | --- |
| `CurrentPlayerAddress` | 11025740 | 0xA83D4C | 当前玩家 `HouseClass*` 指针 |
| `WinFlagAddress` | 11025737 | 0xA83D49 | 立即胜利标记（写 1 生效） |
| `MapClassAddress` | 8910824 | 0x87F7E8 | 全局 `MapClass*` 指针 |
| `RevealMapFunctionAddress` | 5733776 | 0x577D90 | `MapClass::Reveal` 函数入口 |

## HouseClass 字段偏移

| 名称 | 偏移 | 含义 |
| --- | --- | --- |
| `BalanceOffset` | 0x30C | 资金 |
| 工厂计数（旧版） | 0x5378 | 3.3.6.6 起不再改写，以免实际增减工厂或换局后恢复过时快照 |
| `PowerOutputOffset` | 0x53A4 | 电力输出 |
| `PowerDrainOffset` | 0x53A8 | 电力消耗 |
| `RecheckPower` | 0x5778 | 1 字节，置 1 请求游戏重新结算电力 |
| `RecheckRadar` | 0x5779 | 1 字节，置 1 请求游戏重新计算雷达状态 |
| `SuperItemsOffset` | 0x258 | 超级武器指针数组 |
| `SuperCountOffset` | 0x264 | 超级武器数量 |

## 超级武器条目偏移

| 名称 | 偏移 | 含义 |
| --- | --- | --- |
| `SuperIsChargedOffset` | 0x6F | 充能完成标记（0 → 写 1） |
| `SuperCameoChargeStateOffset` | 0x78 | 充能状态（-1 表示不可用，跳过） |

## 指针有效性约定

有效玩家 / 对象指针判定为 `[0x10000, 0x7FFF0000)`，超武数量上限按 256 防御性校验，避免野指针导致游戏崩溃。

## 参考

### 3.3.6.5 电力指令核对

- `HouseClass::UpdatePower`：`0x508C30`；在 `0x508C79` 清除 `RecheckPower`，在 `0x508DE0` 设置 `RecheckRadar`。
- 电力补丁位置：`0x508D81`，原指令 `E8 5A BF F4 FF`（`call 0x454CE0`），返回 `0x508D86`。只覆盖一条完整指令；跳板保留寄存器和标志，并重放该调用。
- 该点先于 `0x4CA6E0` 的工厂计时更新以及之后的供电状态判断。只检查电力表或在函数更靠后的位置修改，不能充分验证此问题。
- 核对的 `gamemd.exe` SHA-256：`7CD005D263FDE203D9C84548200A057A8DF61D724DA3C6BD1E521EEB61CD0747`；Ares 文件版本 `20.333.289`（3.0）。其 `.inj` 声明在 `0x508C7F`、`0x508D32`、`0x508D4A` 等处有补丁，未覆盖 `0x508D81`。启动时仍须检查实际进程内的指令。
- 以上字段偏移由该二进制指令核验，不能用其他版本头文件的推算值替代。

偏移与 Ares 兼容思路参考 [RA2YurisRevengeTrainer](https://github.com/AdjWang/RA2YurisRevengeTrainer)（未复制代码），并与本机游戏二进制只读核对；不代表真实对局验证通过。

### 3.3.6.6 建造指令核对

- `FactoryClass::Update`：`0x4C9B20`；补丁位置 `0x4C9B68`，原指令 `E8 C3 CA F5 FF`（`call 0x426630`），返回 `0x4C9B6D`。该位置先于进度递增和扣款，且在游戏检查暂停/完成状态之后。
- 工厂字段：进度 `+0x24`，变化标志 `+0x28`，计时起点 `+0x2C`，剩余时间 `+0x34`，周期 `+0x38`，步长 `+0x3C`，生产对象 `+0x58`，缺钱标志 `+0x5C`，未付余额 `+0x60`，原始余额 `+0x64`，所属玩家 `+0x6C`，暂停 `+0x70`。
- 补丁只改进度为 53 和剩余时间为 0；标准步长为 1。游戏将其推进至 54 后，按未付余额检查可用资金；足够时正常扣款并完成，否则退回 53 等待资金。已经完成、空队列及非标准步长不会被补丁改写。
- 本机同一 `gamemd.exe` 指纹与 Ares 3.0 的注入清单已核对，此位置没有 Ares 钩子；实际安装仍检查进程内原字节，不覆盖未知指令。
- 建筑、防御、步兵、车辆、舰船和飞机均通过同一工厂生产函数。对应 House 主工厂槽位分别为 `0x53BC`、`0x53CC`、`0x53B0`、`0x53B4`、`0x53B8`、`0x53AC`，新补丁无需从外部遍历这些指针。
