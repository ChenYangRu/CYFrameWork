# AssetImportRules · 资源导入规则（自动生效）

> 规则数值的唯一真相源 = `ImportRuleTable.cs`；本文解释"为什么这么定"和"怎么用"。
> 只管 `Assets/BundleAssets/` 下的资源，**不碰** YooAsset 包内与工程其它资源。

---

## 一、文件

| 文件 | 职责 |
|---|---|
| `ImportRuleTable.cs` | 规则表（路径前缀 → 纹理/音频/模型参数），最长前缀优先 |
| `AssetImportRuleBase.cs` | 公共底座：范围判定、取规则、userData 版本戳 |
| `TextureImportRule.cs` | `OnPreprocessTexture`：尺寸/类型/mipmap/Read-Write/压缩 + Android·iOS 平台覆盖 |
| `AudioImportRule.cs` | `OnPreprocessAudio`：加载方式/格式/质量/单声道 |
| `ModelImportRule.cs` | `OnPreprocessModel`：网格压缩/关 Read-Write/优化/动画开关 |
| `AssetImportAudit.cs` | 菜单：**审计**存量资源 + **按规则重导** |

---

## 二、为什么必须设"平台覆盖"

这是移动端最容易忽略、代价最大的一条：

| 情况 | 真机内存 |
|---|---|
| 只设 `maxTextureSize`，不设平台格式 | 编辑器里看着正常，真机退回 **RGBA32 未压缩** |
| 1024×1024 RGBA32 | **4 MB** |
| 1024×1024 ETC2/ASTC | **0.5~1 MB** |

所以 `TextureImportRule` 对每张图都写 `Android`（ETC2）与 `iPhone`（ASTC 6×6）两套覆盖。
**只改 TextureImporter 顶层参数是无效的。**

---

## 三、规则表（摘要，完整见代码）

| 路径前缀 | 类型 | 关键设置 |
|---|---|---|
| `UI/` | Sprite | 2048、关 mipmap、关 Read/Write、CompressedHQ |
| `Art/Icon/` | Sprite | 128、关 mipmap、crunched |
| `Art/Character/` | 纹理 + 模型 | 1024、开 mipmap；模型 Medium 压缩、导入动画 |
| `Art/Prop/` `Art/Scene/` | 纹理 + 模型 | 1024、开 mipmap；模型不导动画 |
| `Art/Effect/` | 纹理 | 256、开 mipmap |
| `Audio/Bgm/` | 音频 | Streaming + Vorbis q0.7 |
| `Audio/Sfx/` | 音频 | DecompressOnLoad + q0.5 + **单声道** |
| `Audio/Voice/` | 音频 | Streaming + q0.5 + 单声道 |
| `Config/` `Text/` `Shader/` `Video/` | — | 不干预 |

---

## 四、刻意不自动改的项（改错代价 > 收益）

| 项 | 原因 |
|---|---|
| `ModelImporter.animationType` | Humanoid / Generic 猜错，骨架绑定直接废，需美术确认 |
| 材质导入模式 | 项目用自研 shader 时生成规则不同 |
| `globalScale` | 改缩放会让所有挂点/碰撞体错位 |
| `spriteImportMode` 之外的 Sprite 设置（Packing Tag、Pivot） | 与美术工作流耦合 |
| Shader / 配置表 / 视频 | 没有通用最优解 |

---

## 五、存量资源怎么办（重点）

`AssetPostprocessor` **只在导入时触发**。所以：

```
① 菜单 框架/资源/导入规则/审计（只报告，不改文件）
   → 输出「受管资源 N 个，不合规 M 个」+ 分类统计
② 菜单 框架/资源/导入规则/按规则重导（耗时，改规则后用）
   → 只重导"有规则且不合规"的，不会全量刷 500 个文件
③ 再跑一次审计，确认 0 不合规
```

### 版本戳机制

每个受管资源导入后会在 `AssetImporter.userData` 写入 `gfrule:<版本号>`。

- 改了规则 → 把 `ImportRuleTable.RuleVersion` **+1** → 审计立刻把全部存量标为"版本落后"
- 不 +1 则只有实际参数不符的会被标出来（比如有人手工关了 mipmap）

---

## 六、新增目录时

1. 在 `ImportRuleTable.Rules` 里加一条（`PathPrefix` 必须与 `BundleAssets/` 下的真实目录前缀一致，**区分大小写**）
2. 同步更新该目录的 `README.md`
3. `RuleVersion + 1`
4. 跑「审计」→「按规则重导」

漏第 1 步的后果：新目录资源按 Unity 默认导入（未压缩纹理 / 立体声 wav），
编辑器里一切正常，**真机内存和包体才会爆**。
