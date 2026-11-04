# BundleAssets · YooAsset 打包资源根目录

> 本目录下的**一切**都是要进 AssetBundle 的运行时资源。放进来之前先读完本文。
> 收集器配置由菜单 `框架/资源/初始化 YooAsset 收集器` 生成，分组见下表，**不要手工改收集器**。

---

## 一、目录职责（谁放什么）

| 目录 | 放什么 | 不许放 |
|---|---|---|
| `UI/` | 界面 prefab、图集、九宫底图、界面字体 | 界面逻辑脚本、临时对比图 |
| `Art/` | 角色 / 道具 / 特效 / 图标 / 场景静态资源 | 原始 PSD、FBX 源工程、ZBrush 工程 |
| `Audio/` | BGM / 音效 / 语音 | 未压缩 wav 母带（先去 DAW 导出） |
| `Config/` | 运营可调的数值表 / 活动表（json 或 bytes） | 客户端硬编码配置、密钥 |
| `Text/` | 本地化文案表 | 代码里的硬编码文案 |
| `Shader/` | 自定义 shader、ShaderVariantCollection | 引擎内置 shader 的副本 |
| `Video/` | 过场 CG、宣传片 | 首包必需的小动画（用序列帧或 Spine） |

---

## 二、手游硬性限制（超过就不许进包）

### 体积

| 项 | 上限 | 说明 |
|---|---|---|
| 首包（Android AAB） | **≤ 30 MB** | 商店弱网安装转化率分水岭 |
| 微信小游戏首包 | **≤ 4 MB** | 平台硬限制；超出走分包 + CDN |
| 单个 bundle | ≤ 4 MB | 弱网单文件重试成本；超了要拆 |
| 单个资源文件 | ≤ 2 MB | 大图先切图集，长音频切段 |
| `Video/` 单文件 | ≤ 20 MB | 只允许流式播放，不进首包 |

### 纹理

| 项 | 规范 |
|---|---|
| 图集尺寸 | 优先 1024×1024，上限 2048×2048 |
| 单图尺寸 | ≤ 512×512（图标 ≤ 128×128） |
| Read/Write | **必须关闭**（开着内存翻倍） |
| Mipmap | UI 关闭、3D 角色/场景开启 |
| 压缩 | Android `ETC2`（或 ASTC 6×6）、iOS `ASTC` |
| 格式 | 只允许 PNG / TGA（源），禁 JPG（有色块） |

### 音频

| 类型 | 格式 | 上限 |
|---|---|---|
| BGM | OGG Vorbis 96~128 kbps | 单曲 ≤ 1.5 MB |
| 音效 | OGG / 短 WAV | 单文件 ≤ 100 KB |
| 语音 | OGG 64 kbps | 单条 ≤ 300 KB（约 10s） |
| 加载方式 | 全部 `Streaming`（BGM）/ `DecompressOnLoad`（短音效） | 禁 `CompressedInMemory` 长音频 |

### 命名（这条最容易踩）

- **全小写 + 下划线**：`ui_login_panel.prefab` ✅ ｜ `UI_LoginPanel.prefab` ❌
- 禁中文、空格、大写、`#` `@` `$` `%` 等特殊字符
- 目录深度 **≤ 4 层**
- 资源定位地址（location）= 相对 `BundleAssets/` 的路径去扩展名，例如
  `UI/Login/ui_login_panel` —— 所以**改名 = 改代码里的加载路径**
- 同一目录下**禁止同名不同扩展名**（会撞 location）

### 禁止项

- ❌ `.cs` / `.js` / `.dll` 源文件（代码随包发布，资源目录只放资源）
- ❌ `Editor/` 目录、编辑器专用资源
- ❌ 原始美术工程（PSD / MA / Blend / ZBrush）
- ❌ 第三方 Demo、示例场景、测试图
- ❌ 密钥、Token、内网地址

---

## 三、分组与首包策略

收集器把本目录切成这些分组（`Package = GamePackage`）：

| 分组 | 路径 | 打包 tag | 首发策略 |
|---|---|---|---|
| `Builtin` | `UI/Common`、`Config`、`Text`、`Shader`、`Art/Icon` | `builtin` | **进首包**（`Copy Buildin`） |
| `UI` | `UI/Login`、`Lobby`、`Battle`、`Shop` | `hotfix` | CDN 增量 |
| `Art` | `Art/Character`、`Prop`、`Effect`、`Scene` | `hotfix` | CDN 增量，进战斗前预下载 |
| `Audio` | `Audio/**` | `hotfix` | CDN 增量，按场景预下载 |
| `Video` | `Video/**` | `hotfix` | 按需点播，**永不预载** |

> 首包内容 = `builtin` tag 的分组；构建时在 YooAsset 打包面板勾 `Copy Buildin` 才会进 `StreamingAssets`。

---


## 五、目录内的 `README.md` 会不会被打进包？

不会。收集器使用自定义忽略规则 `DocIgnoreRule`（`Assets/Editor/YooAssetRules/DocIgnoreRule.cs`），
它静默跳过 `.md` / `.txt` / `.keep` / `.gitkeep`，**不打日志、不进包**。

> 如果你新增了别的文档格式（比如 `.pdf`），要在这个规则里补一行，否则会被当普通资源打进包。

**空目录用 `.gitkeep` 占位**：git 不跟踪空目录，删掉占位文件会导致同事拉代码后目录消失、
收集器路径失效。放第一个真实资源时可以删掉，但目录不能删。

---

## 六、新增一个资源目录的完整流程

1. 在 `BundleAssets/<分类>/` 下建目录（小写下划线命名）
2. 放一个 `README.md` 写清"放什么 / 不许放什么 / 上限"
3. 打开菜单 `框架/资源/打开 YooAsset 收集器`，在该分组里 **添加收集器**，路径指向新目录
   - 收集规则：`MainAssetCollector` / `PackDirectory` / `AddressByPathInBundleAssets` / `CollectAll`
   - 打上对应 tag（`builtin` 或 `hotfix`）
4. 保存 → 重新构建 → 真机验证 location 能加载到

**漏了第 3 步 = 资源在编辑器里能看见、打包后加载不到**，这是最常见的"资源丢了"根因。
