# Art/ · 美术资源

## 放什么

| 子目录 | 内容 | 命名例 |
|---|---|---|
| `Character/` | 角色模型、贴图、骨骼动画、Avatar 遮罩 | `char_mage_body.fbx`、`char_mage_albedo.png` |
| `Prop/` | 场景道具、建筑、植被、可交互物 | `prop_crate_01.fbx` |
| `Effect/` | 特效贴图、粒子材质、特效 prefab | `fx_hit_spark.prefab` |
| `Icon/` | 道具/技能/货币/头像图标 | `icon_item_crystal.png` |
| `Scene/` | 场景静态资源：Lightmap、Reflection Probe、Terrain Layer | `scene_forest_lightmap_0.exr` |

## 不许放

- 原始工程文件（PSD / MA / Blend / ZBrush / SP 工程）
- 未压缩的 4K 贴图（进包前必须降到规范尺寸）
- 第三方商店素材原包（要先按本项目规范重命名、重新导入设置）
- 视频（走 `Video/`）

## 限制

### 模型

| 项 | 规范 |
|---|---|
| 单角色三角面 | 主角 ≤ 30k，小怪 ≤ 8k，道具 ≤ 2k |
| 骨骼数 | ≤ 60（移动端蒙皮开销与骨骼数正相关） |
| UV 套数 | ≤ 2 |
| 材质槽 | ≤ 3（每多一个材质槽 = 多一次 drawcall） |
| 动画 | 同角色合并为一个 AnimationClip 集；单个 clip ≤ 2s 的走 `Animator` 状态机，长过场走 `Video/` |
| 缩放 | 导入后统一 `Scale Factor = 1`，禁止靠 Transform 缩放修尺寸 |

### 贴图

| 项 | 规范 |
|---|---|
| 尺寸 | 角色 1024×1024；道具 512×512；特效 256×256 起 |
| 通道 | 只留必要通道；金属/粗糙度用单通道打包（R/G） |
| Mipmap | 3D 资源**开启**；UI/图标关闭 |
| Read/Write | **关闭** |
| 压缩 | Android ETC2 / ASTC；法线贴图必须 `Normal map` 类型（否则压缩会毁法线） |

### 特效

| 项 | 规范 |
|---|---|
| 单特效粒子数 | 峰值 ≤ 200（低端机 Redmi 9A 档） |
| 同屏特效实例 | ≤ 12 |
| 混合模式 | 优先 `Alpha Blend`，`Additive` 同屏 ≤ 4 层（叠加过曝） |
| 贴图 | 256×256，图集化；单特效贴图数 ≤ 4 |

### 图标

| 项 | 规范 |
|---|---|
| 尺寸 | 128×128（源），打包进 1024 图集 |
| 形态 | **白色 alpha 剪影 + 代码染色**（不要在图里烘死颜色，否则换品质色要重出图） |
| 留白 | 实体占比 0.25~0.62（太小看不清、太满显拥挤） |

## 打包注意

- `Character/` 与 `Effect/` 是**运行时按需加载**的，必须在战斗前预下载（见 `docs/03_打包与资源更新.md`）。
- `Scene/` 里 Lightmap 与场景 prefab 必须**同 bundle**，否则移动端加载时会出现"黑场景"。
- 同一角色的 albedo/normal/ORM 放在同一目录，`PackDirectory` 会把它们打成一个 bundle，减少 IO 次数。
