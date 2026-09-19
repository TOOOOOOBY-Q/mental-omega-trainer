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
| `FactoryCountOffset` | 0x5378 | 各类型工厂计数（连续 5 个 int32，快速建造锁定为 15） |
| `PowerOutputOffset` | 0x53A4 | 电力输出 |
| `PowerDrainOffset` | 0x53A8 | 电力消耗 |
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

偏移与 Ares 兼容思路参考 [RA2YurisRevengeTrainer](https://github.com/AdjWang/RA2YurisRevengeTrainer)（未复制代码），并与本机 3.3.6 游戏实测核对。
