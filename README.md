# CYFrameWork · Unity 商业项目框架

> **Unity 2021.3.45f2c1** ｜ **HybridCLR 8.15.0**（代码热更）｜ **YooAsset 3.0.5**（资源热更）
> 目标平台：Android 优先，可扩 iOS / PC / WebGL（小游戏）
> 定位：**开箱能跑、能热更、能打包、能给 AI 操作**的生产级骨架。业务写在热更层，框架层只放不发版的东西。

---

## 一、这是什么 / 不是什么

**是**：一套可以直接拉下来就启动的分层框架 —— 资源更新、代码热更、UI 栈、TCP 网络、音频、对象池、编辑器打包工具链、MCP（把 AI 接进编辑器）全部就位，只剩"你的玩法"没写。

**不是**：不是完整游戏、不含账号/支付/埋点/广告 SDK、不含具体玩法系统。这些都属于"业务"，应该写在热更层，接 SDK 时新建 AOT 侧程序集（`GameFramework.Sdk` 之类），**不要塞进热更层**。

| 常见痛点 | 传统做法 | 本框架 |
|---|---|---|
| 改一行逻辑就要重新提审 | 只能等审核 | `Game.HotUpdate` 出 DLL，走 CDN 下发 |
| 资源更新整包重下 | 手写 AB 依赖管理 | YooAsset：版本比对 + 增量下载 + 断点续传 |
| 网络层每个项目重抄一遍 | 复制粘贴 SocketClient | 独立 `GameFramework.Network`：拆包 / 心跳 / 重连 / 请求配对 |
| UI 越做越乱 | 一堆 UIManager 大杂烩 | 五层 Canvas + 面板栈 + 自动遮罩 + 注册式 |
| AOT 裁切炸热更 | 上线才发现"类型不存在" | 补元数据流程固化 + 自检菜单提前拦 |
| 没资源时框架跑不起来 | 手动造资源才能 Play | **降级模式**：无资源也照常进游戏 |
| 新人第一天点 20 次面板 | 靠文档口口相传 | 菜单一条龙 + 一键生成占位资源验证链路 |
| 想让 AI 帮忙改工程 | 人工贴代码截图 | 内置 MCP 桥接：13 个工具 + 一键导出 AI 说明书 |

---

## 二、环境要求

| 项 | 要求 | 说明 |
|---|---|---|
| Unity | **2021.3.45f2c1**（2021.3 LTS 均可） | HybridCLR 8.15 支持 2019.4+，但本框架按 2021.3 验证 |
| 平台模块 | Android Build Support（含 IL2CPP + OpenJDK/SDK/NDK） | 只跑编辑器则不需要 |
| Node.js | ≥ 16（仅 MCP 网关需要） | 不用 MCP 可跳过 |
| 网络 | 首次 `HybridCLR/Installer → Install` 需联网 | 会拉 `il2cpp_plus`（几分钟） |

**依赖已内置为 embedded package**（`Packages/com.tuyoogame.yooasset`、`Packages/com.code-philosophy.hybridclr`），
**不依赖 openupm / 外网 UPM**，离线可编译。

---

## 三、快速开始（首次 5 步）

```
1) Unity Hub 用 2021.3.45f2c1 打开本目录（E:\CYFrameWork）
2) 菜单 框架/资源/初始化 YooAsset 收集器（首次必点）
3) 菜单 框架/热更/0. 登记热更程序集到 HybridCLR
4) 菜单 HybridCLR/Installer → Install（只需一次，会拉 il2cpp_plus）
5) 直接点 Play
```

预期：启动页进度条走完 → 出现「登 录」界面 → 点「单机进入」→ 主界面显示
`业务逻辑版本 v1` / `资源版本` / `网络：未连接`。

### 3.1 首跑为什么会看到「资源降级模式」—— 这是正常的

`Assets/BundleAssets/` 初次只有各目录的 `README.md` + `.gitkeep`，收集器的忽略规则正好把它们全过滤掉，
YooAsset 模拟构建拿不到任何资源 → 框架进入**降级模式**：一条警告日志，游戏照常进登录页，**不报红字、不白屏**。

想验证打包链路真的通，造一份最小占位资源（图 / 音 / 配置各一个，体积可忽略）：

```
菜单 框架/资源/生成占位资源（验证打包链路）
→ 再 Play，应变成 [Res] 100% 资源就绪（EditorSimulate）
→ 验证完用 菜单 框架/资源/清理占位资源 一键删掉
```

> 编辑器下**不需要**编译 DLL、不需要打包：`Game.HotUpdate` 由 Unity 正常编译，启动器直接取已加载程序集。
> 真机 / 出包才需要"编译 DLL → 打包 → 上传 CDN"，见 [docs/03_热更与打包手册.md](docs/03_热更与打包手册.md)。

### 3.2 可选：连一个测试服务端

改 `Assets/Resources/Config/network.json` 的 `host` / `port`，起一个按
[docs/02_接入指南.md](docs/02_接入指南.md) 协议格式收发的 TCP 服务端即可。
**没有服务端时「单机进入」是唯一兜底出口** —— 框架不允许把玩家卡死在登录页。

---

## 四、目录结构

```
CYFrameWork/
├─ Assets/
│  ├─ Framework/                      ← AOT 稳定层（不热更；改了要发版）
│  │  ├─ Core/        GameFramework.Core      事件总线/日志/服务定位/状态机/主循环/定时器/对象池/引用池/任务池/路径
│  │  ├─ Network/     GameFramework.Network   TCP 通道/拆包/心跳/重连/请求配对/协议号表
│  │  ├─ Resource/    GameFramework.Resource  YooAsset 封装：四模式初始化 + 七段更新流程 + 加载/卸载
│  │  ├─ UI/          GameFramework.UI        五层 Canvas/面板栈/遮罩/代码搭 UI 工具
│  │  ├─ Audio/       GameFramework.Audio     BGM 交叉淡入/SFX 通道池/三组音量（不依赖 AudioMixer）
│  │  └─ Runtime/     GameFramework.Runtime   启动状态机 + HybridCLR 加载器 + 启动/更新界面
│  ├─ Game/
│  │  ├─ AOT/         Game.AOT                 唯一 AOT 入口 Bootstrap + AutoBoot 兜底
│  │  └─ HotUpdate/   Game.HotUpdate           业务层（autoReferenced = false）
│  ├─ BundleAssets/                            ← 所有进包资源（YooAsset 收集根，含规范文档）
│  │  ├─ UI/ Art/ Audio/ Config/ Text/ Shader/ Video/
│  │  └─ HotUpdate/                            热更 DLL（.dll.bytes，工具自动生成）
│  ├─ Resources/
│  │  ├─ Config/      launch.json / resource.json / network.json
│  │  └─ AOTMetadata/                          补元数据 .bytes（随包，工具生成）
│  ├─ Scenes/SampleScene.unity                 示例场景（挂 Bootstrap 即可跑）
│  └─ Editor/         GameFramework.Editor     打包工具链 / 导入规则 / YooAsset 规则 / MCP 桥接
├─ Packages/
│  ├─ com.code-philosophy.hybridclr            HybridCLR 8.15.0（embedded，离线可用）
│  └─ com.tuyoogame.yooasset                   YooAsset 3.0.5（embedded，离线可用）
├─ ProjectSettings/
├─ CDN/                                        构建产物按 平台/App版本/资源版本 落地（出包后生成）
└─ docs/                                       文档（见文末索引）
```

---

## 五、分层与依赖规则

### 5.1 程序集（9 个 asmdef）

| 程序集 | 位置 | 热更 | autoReferenced | 依赖 | 职责 |
|---|---|---|---|---|---|
| `GameFramework.Core` | Framework/Core | ❌ | ✅ | — | 零 Unity 依赖的工具层（除池子需要 GameObject） |
| `GameFramework.Network` | Framework/Network | ❌ | ✅ | Core | TCP 通道、封拆包、心跳重连 |
| `GameFramework.Resource` | Framework/Resource | ❌ | ✅ | Core, YooAsset | 资源初始化/更新/加载唯一入口 |
| `GameFramework.UI` | Framework/UI | ❌ | ✅ | Core, Resource, UnityEngine.UI | 面板栈与 UI 构建 |
| `GameFramework.Audio` | Framework/Audio | ❌ | ✅ | Core, Resource | 音频服务 |
| `GameFramework.Runtime` | Framework/Runtime | ❌ | ✅ | 上述五层, YooAsset, HybridCLR.Runtime | 启动流程 + 热更加载 |
| `Game.AOT` | Game/AOT | ❌ | ✅ | 上述六层 | 场景入口 `Bootstrap` |
| **`Game.HotUpdate`** | Game/HotUpdate | **✅** | **❌** | 上述六层 | 业务代码（你的主角） |
| `GameFramework.Editor` | Editor | — | — | 全部 + YooAsset.Editor + HybridCLR.Editor | 编辑器工具链 |

### 5.2 依赖方向（单向，禁止反向）

```
Game.AOT ──▶ GameFramework.Runtime ──▶ { Core, Network, Resource, UI, Audio }
Game.HotUpdate ──▶ { Core, Network, Resource, UI, Audio, Runtime }
                       ▲
                       └── Runtime 绝不引用 HotUpdate（否则 AOT 层直接打死热更）
```

**三条铁律**

1. **框架层 AOT，业务层热更** —— 框架层改一次发一次版；业务层随时热更。
2. **顺序不可乱**：资源更新 → 补元数据 → 加载 DLL → 进业务。反了必炸。
3. **热更程序集必须 `autoReferenced: false` 且登记到 HybridCLR Settings** —— 漏一个，真机就是"类型不存在"。

---

## 六、核心能力与用法速查

### 6.1 启动

场景里挂 `Bootstrap`（菜单 `框架/启动/在当前场景添加 Bootstrap`），或什么都不挂 —— `AutoBoot` 会在运行时兜底创建。
`Bootstrap` 上有三个**随场景进包、真机同样生效**的字段：

| 字段 | 作用 |
|---|---|
| `autoStart` | 是否自动启动；关掉可自己调 `GameLauncher.Create().Run()` 写自动化 |
| `resourcePlayMode` | `UseConfig` / `EditorSimulate` / `Offline` / `Host` / `Web` |
| `skipResourceSystem` | 纯业务联调时跳过资源系统（资源加载失败但不阻塞启动） |

优先级（高 → 低）：**Bootstrap(Inspector) > 编辑器菜单(EditorPrefs) > `resource.json`**。
Inspector 属于"这个包明确要什么"；菜单覆盖只是开发期临时手段，不该盖掉打包决策。

### 6.2 资源：四种模式 + 七段更新流程

| 模式 | 枚举 | 资源从哪来 | 更新流程 | 用在哪 |
|---|---|---|---|---|
| `EditorSimulate` | 0 | AssetDatabase（模拟构建） | ❌ | 日常开发，改图即见 |
| `Offline` | 1 | 随包 StreamingAssets | ❌ | 单机包 / 无网验收 |
| `Host` | 2 | 内置兜底 + 下到本地沙盒 | ✅ | Android / iOS / PC 正式包 |
| `Web` | 3 | 全走 HTTP，不落磁盘 | ✅ | WebGL / 微信小游戏 |

```
Initialize → RequestVersion → LoadManifest → Prefetch → WaitConfirm(流量提示) → Downloading → Cleanup → Done
```

开发期切换不用改 json：菜单 `框架/资源/运行模式/*`（覆盖值存 EditorPrefs，**只在编辑器生效**）。

```csharp
// 加载（一律协程，禁止同步加载 —— 小游戏不支持）
yield return res.LoadAsset<Texture2D>("UI/Common/ui_placeholder", tex => { /* 用 */ }, err => Debug.LogError(err));
yield return res.Instantiate("UI/Login/ui_login_panel", canvasTransform, go => { /* 实例 */ });
yield return res.LoadRawBytes("HotUpdate/Game.HotUpdate.dll", bytes => { /* 热更 DLL */ });
```

**下载确认必须接**（否则移动网络下静默吃掉几十 MB）：

```csharp
res.ConfirmDownload = (info, answer) => ShowDialog(info.ToPlayerText(), ok => answer(ok));
```

### 6.3 UI

```csharp
// 注册（PrefabLocation 走资源层 location 规则）
ui.Register<LoginWindow>("Login", UILayer.Normal, "UI/Login/ui_login_panel", fullScreen: true);

ui.Open("Login", args: null, onOpened: w => { /* ... */ });
ui.Close("Login");
ui.CloseTop();                       // 返回键 / 安卓返回
ui.IsOpen("Login")                   // 查询
```

五层：`Scene(0) / Normal(1) / Popup(2) / Top(3) / Tips(4)`，每层一个 Canvas，自动挂 `CanvasScaler(1920×1080)` + `GraphicRaycaster`。
全屏面板自动加遮罩并拦截点击。没有 prefab 时可用 `UIUtil` 纯代码搭界面（Demo 的登录页/主界面就是这么做的）。

### 6.4 网络（TCP，12 字节头 + 变长体，大端序）

```
[0..3]  bodyLen (uint32)   包体字节数（不含头）
[4..7]  cmd     (uint32)   协议号
[8..11] seq     (uint32)   序号（请求-响应配对 / 去重）
```

```csharp
net.RegisterHandler(Cmd.LoginAck, p => { var b = new ByteBuffer(p.Body); /* ... */ });

net.Send(Cmd.LoginReq, body);                        // 单向
uint seq = net.Request(Cmd.LoginReq, body, (ok, p) => { });   // 带超时的请求-响应配对
```

内置：收/发双线程、心跳（间隔/超时可配）、指数退避自动重连、超时回调、断线事件。
协议号分段约定见 `Framework/Network/Cmd.cs`：`1~99` 系统 / `100~999` 账号 / `1000~1999` 玩家数据 / `9000+` 调试。

> 只实现 TCP。**WebGL / 小游戏不支持裸 Socket**，需要新写一个 `INetChannel` 实现（WebSocket）挂进 `NetworkService`，其余不动。

### 6.5 音频

```csharp
audio.PlayBgm("Audio/Bgm/bgm_lobby", fadeSeconds: 0.8f);   // 双 source 交叉淡入，不爆音
audio.PlaySfx("Audio/Sfx/sfx_click", volume: 0.8f, pitch: 1.1f);
audio.SetVolume(AudioGroup.Bgm, 0.3f);                    // 立即生效并落 PlayerPrefs
```

三层音量：`Master × Group(Bgm/Sfx/Voice) × Clip`。不依赖 `AudioMixer`，零额外资产。

### 6.6 Core 工具

| 类型 | 一句话 | 关键 API |
|---|---|---|
| `EventBus` | 零反射全局事件 | `Subscribe<T>` / `Unsubscribe<T>` / `Publish<T>` |
| `ServiceLocator` | 服务注册查找 | `Register` / `Get<T>` / `TryGet<T>` / `Unregister<T>` |
| `Fsm` | 轻量状态机 | `Add` / `ChangeState<T>` / `Tick` / `Stop` |
| `GameLoop` | 主线程调度 | `Post(Action)` / `Run(IEnumerator)` |
| `TimerService` | 延时/重复 | `Delay` / `Repeat` / `Cancel` |
| `GameObjectPool` | 预制体池 | `Get` / `Release` / `ReleaseAll` / `Destroy` |
| `ReferencePool` | 按类型分池，消除 GC 峰值 | `Acquire<T>` / `Release` / `Add` / `Remove` |
| `TaskPool` | 限流跑批（防同帧几百个异步操作卡顿） | `Run(tasks, concurrency, onDone, onProgress)` |
| `GameLog` | 分级日志 + 落盘 | `Info/Warn/Error` |

---

## 七、热更新工作流（真机 / 出包）

```
① 框架/热更/3. 一键 GenerateAll（DLL + link.xml + 补元数据）
      ├─ 编译 Game.HotUpdate → .dll
      ├─ 包成 .bytes → Assets/BundleAssets/HotUpdate/
      ├─ 生成补元数据 → Assets/Resources/AOTMetadata/（随包）
      └─ 生成 link.xml（Strip=High 时保活）
② 框架/资源/初始化 YooAsset 收集器        （首次 / 目录变了才需要）
③ HybridCLR/Build → 出包（Build Settings 选 Android）
④ YooAsset/Bundle Builder → 构建资源包
⑤ 框架/资源/同步构建产物 → CDN 目录       → 上传 CDN
```

CDN 目录规范：`CDN/<平台>/<App版本>/<资源版本>/`，`resource.json` 填 `hostServer` 与 `appVersion`。
`.version` 文件**必须禁缓存**（`Cache-Control: no-cache`），否则玩家永远拿不到新版本。

细节（含 `link.xml`、裁剪等级、验证清单）见 [docs/03_热更与打包手册.md](docs/03_热更与打包手册.md)。

---

## 八、编辑器菜单总表

| 菜单 | 用途 |
|---|---|
| `框架/资源/初始化 YooAsset 收集器（首次必点）` | 按 `BundleAssets/` 目录建 6 个分组 18 个收集器，并写入忽略/寻址规则 |
| `框架/资源/生成占位资源（验证打包链路）` | 造 png/wav/json 三件占位，用于验证链路 |
| `框架/资源/清理占位资源` | 删除上面三个文件 |
| `框架/资源/运行模式/*` | 编辑器内临时切换四种运行模式 |
| `框架/资源/打印当前资源模式` | json 值 / 覆盖值 / 实际生效，三行对照 |
| `框架/资源/导入规则/审计（只报告）` | 检查 `BundleAssets` 内资源是否符合手游体积/格式限制 |
| `框架/资源/导入规则/按规则重导` | 规则改了之后把存量资源重新导入一遍 |
| `框架/资源/同步构建产物 → CDN 目录` | 把 YooAsset 构建产物按 `平台/App版本/资源版本` 归档 |
| `框架/资源/打开 YooAsset 打包器 / 收集器` | 直连 YooAsset 原生窗口 |
| `框架/热更/0. 登记热更程序集到 HybridCLR` | 写 `HybridCLRSettings`（漏这步真机必炸） |
| `框架/热更/1. 编译热更 DLL → .bytes` | 只编译，不打包 |
| `框架/热更/2. 生成 AOT 补元数据 → Resources` | 生成 `AOTMetadata/*.bytes` |
| `框架/热更/3. 一键 GenerateAll` | 上面三步 + link.xml |
| `框架/热更/9. 自检：配置一致性` | 检查程序集名/路径/入口类型是否对得上 |
| `框架/启动/在当前场景添加 Bootstrap` | 建一个带序列化启动参数的入口对象 |
| `框架/启动/打开主场景` | 打开样例场景 |
| `Tools/MCP 桥接/*` | 一键配置 / 启动 / 停止 / 复制客户端配置 / 导出 AI 说明 |

---

## 九、配置项

| 文件 | 关键字段 | 说明 |
|---|---|---|
| `Resources/Config/launch.json` | `hotUpdateAssemblies` / `aotMetadataAssemblies` / `entryType` / `entryMethod` / `hotUpdateLocationRoot` / `aotMetadataResourceRoot` | 热更程序集名单与业务入口 |
| `Resources/Config/resource.json` | `packageName` / `playMode` / `hostServer` / `fallbackHostServer` / `appVersion` / `downloadMaxConcurrency` / `requestTimeout` | 资源系统 |
| `Resources/Config/network.json` | `host` / `port` / `heartbeatInterval` / `heartbeatTimeout` / `autoReconnect` / `maxReconnectTimes` / `requestTimeout` | 网络 |

> `JsonUtility` **静默忽略未声明字段** —— 给 json 加字段必须同步加 `ConfigModels` / `*Config` 的 `public` 字段，否则白配。

---

## 十、资源目录规范

所有进包资源放 `Assets/BundleAssets/`，按 `UI / Art / Audio / Config / Text / Shader / Video / HotUpdate` 划分，
每个目录有 `README.md` 写清职责与限制（手游体积上限、命名规则、禁止项）。
编辑器侧还有一套 `AssetImportRules` 自动套导入参数（纹理尺寸/压缩/平台覆盖、音频加载方式、模型压缩）。

详见 [docs/05_资源目录规范.md](docs/05_资源目录规范.md)。

---

## 十一、把 AI 接进编辑器（MCP）

```
Tools/MCP 桥接/① 一键配置 → 填项目 ID → 自动派生 → 换到空闲端口 → ② 保存并重启
Tools/MCP 桥接/③ 生成 AI 说明（MD）      → 自动复制到剪贴板，直接发给 AI
Tools/MCP 桥接/复制客户端配置             → 粘到 ~/.workbuddy/mcp.json 或 .cursor/mcp.json
```

13 个工具（8 只读 + 5 危险默认关闭）：层级/组件/资源/日志/编辑器状态查询，改工程类操作需显式开启。
标识全部收进 `Assets/Editor/Mcp/McpProjectConfig.asset`，**代码零硬编码**。

详见 [docs/06_MCP接入.md](docs/06_MCP接入.md)。

---

## 十二、常见问题（先查这里，别猜）

| 现象 | 判据 | 处置 |
|---|---|---|
| Play 后一条警告「资源降级运行」 | `BundleAssets` 里无可打包资源（只有 `.md`/`.gitkeep`） | 菜单「生成占位资源」或放入真资源 |
| 真机报"类型不存在" | 缺补元数据 / 该 AOT 类型被裁切 | 跑 `框架/热更/3. 一键 GenerateAll`；Strip=High 时确认 `link.xml` |
| `BadImageFormatException` | DLL 与当前 HybridCLR/Unity 版本不匹配 | 重新 GenerateAll，别复用旧 DLL |
| 热更后行为没变 | ① 主界面版本号变没变 ② CDN `.version` 变没变 | `.version` 必须 `no-cache`；查 DNS/缓存 |
| 编辑器热更代码不生效 | `AppDomain` 里是旧程序集 | 关掉 Enter Play Mode 的 Domain Reload 禁用；或重启编辑器 |
| URL 404 但浏览器能打开 | 拼出来的 URL 有双斜杠/端口 | 统一走 `PathUtil.CombineUrl` |

完整 30+ 条（现象 → 判据 → 处置）见 [docs/04_踩坑与判据.md](docs/04_踩坑与判据.md)。

---

## 十三、扩展点

| 我要… | 怎么做 |
|---|---|
| 加一个面板 | 继承 `UIWindow` → `ui.Register<T>(...)` → `ui.Open(...)`；有 prefab 放 `BundleAssets/UI/<模块>/` 并在收集器里 |
| 加一条协议 | `Cmd.cs` 加常量 → `net.RegisterHandler(...)` → 服务端同步改号 |
| 加一个业务模块 | 写进 `Game/HotUpdate`，**不要写进 Framework** |
| 加一个新程序集 | 新建 asmdef，`references` 指向 Core（或需要的层），**禁止反向依赖** |
| 加一个新 MCP 工具 | `Assets/Editor/Mcp/Tools/` 下加 `[McpTool("xxx")] public static object` 方法，重启桥接自动生效 |
| 换资源系统 | 只改 `GameFramework.Resource`，业务层不感知（禁直连 YooAsset） |

---

## 十四、已知边界

| 边界 | 说明 |
|---|---|
| 网络只有 TCP | WebGL/小游戏需补 WebSocket 通道实现（`INetChannel`） |
| UI 字体用系统字体 | 仅够 Demo；正式项目换 TMP + Noto Sans SC 并做字体子集 |
| 无账号/支付/埋点/广告 SDK | 属于业务，接 SDK 时新建 AOT 侧程序集，别塞热更层 |
| 无引导/新手流程 | 框架不管玩法 |
| Web 模式未真机验证 | 代码路径完整（`WebServerFileSystem` + `WebNetworkFileSystem`），但需在 WebGL 上实测 |

---

## 十五、文档索引

| 文档 | 什么时候看 |
|---|---|
| [docs/00_总目录.md](docs/00_总目录.md) | 不知道看哪篇时，从索引进 |
| [docs/01_架构设计.md](docs/01_架构设计.md) | 想知道"为什么这么分层"、启动时序、热更原理 |
| [docs/02_接入指南.md](docs/02_接入指南.md) | 要加面板 / 加协议 / 加载资源 / 加业务模块 |
| [docs/03_热更与打包手册.md](docs/03_热更与打包手册.md) | 要出包、要验证热更、要摆 CDN |
| [docs/04_踩坑与判据.md](docs/04_踩坑与判据.md) | 出了诡异问题先查这里（现象 → 判据 → 处置） |
| [docs/05_资源目录规范.md](docs/05_资源目录规范.md) | 往 `BundleAssets/` 放资源前必读 |
| [docs/06_MCP接入.md](docs/06_MCP接入.md) | 要把 AI 接进编辑器 |
| [docs/07_设计取舍与参考.md](docs/07_设计取舍与参考.md) | 对标开源框架的决策记录：借什么、不借什么、为什么 |

---

## 十六、使用约定

- 本框架**不含**任何具体项目的业务代码、美术资源、配置数据；`BundleAssets/` 里只有目录规范与占位资源。
- 框架层（`Assets/Framework`、`Assets/Game/AOT`）是 AOT 边界：**改动即发版**，落地前想清楚。
- 依赖版本锁定在 `Packages/manifest.json` + embedded package，升级 Unity / HybridCLR / YooAsset 需要同步回归 `docs/04` 里的判据。
