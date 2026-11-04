# CYFrameWork · Unity 商业项目框架

> **Unity 2021.3.45f2c1** ｜ **YooAsset 3.0.5**（资源热更）｜ 代码随包发布（不做代码热更）
> **双模**：离线单机（本地 txt 存档 + CRUD） ／ 联网（WebSocket）
> 目标平台：**微信小游戏 / WebGL 优先**，同时兼容 Android / iOS / PC
> 定位：**开箱能跑、资源可热更、能打包、离线联网一套代码、能给 AI 操作**的生产级骨架。业务写在业务层，框架层只放稳定不变的东西。

---

## 一、这是什么 / 不是什么

**是**：一套可以直接拉下来就启动的分层框架 —— 资源更新、UI 栈、WebSocket/TCP 网络、**离线存档（txt + CRUD）**、音频、对象池、编辑器打包工具链、MCP（把 AI 接进编辑器）全部就位，只剩"你的玩法"没写。

**不是**：不是完整游戏、不含账号/支付/埋点/广告 SDK、不含具体玩法系统。这些都属于"业务"，应该写在 `Game/Business`，接 SDK 时新建独立程序集（如 `GameFramework.Sdk`），**不要塞进框架层**。

| 常见痛点 | 传统做法 | 本框架 |
|---|---|---|
| 换图/改数值就要整包重下 | 只能发版 | YooAsset 版本比对 + 增量下载，**资源可热更** |
| 小游戏审核怕"动态加载代码" | 自己实现热更 | **不做代码热更**：平台合规、包体更小、启动更短 |
| 网络层每个项目重抄一遍 | 复制粘贴 SocketClient | 独立 `GameFramework.Network`：拆包 / 心跳 / 重连 / 请求配对 |
| 小游戏连不上 socket | 改协议、换引擎 | **WebSocket 双后端**：原生走 .NET，WebGL/小游戏走 jslib，业务协议不变 |
| 单机与联机两套代码 | 到处 `if (isOnline)` | **一个开关分叉**：离线不起网络，数据本地权威；联网走服务端 |
| 存档自己拼格式 | 二进制/PlayerPrefs 混用 | **txt 一行一条记录** + 增删查改 API + 原子写入 + 自动存档 |
| UI 越做越乱 | 一堆 UIManager 大杂烩 | 五层 Canvas + 面板栈 + 自动遮罩 + 注册式 |
| 反射/裁剪埋雷 | 上线才发现"类型不存在" | 全 AOT 编译，`[Preserve]` 只用在 JS 桥这类必要处 |
| 没资源时框架跑不起来 | 手动造资源才能 Play | **降级模式**：无资源也照常进游戏 |
| 新人第一天点 20 次面板 | 靠文档口口相传 | 菜单一条龙 + 一键生成占位资源验证链路 |
| 想让 AI 帮忙改工程 | 人工贴代码截图 | 内置 MCP 桥接：13 个工具 + 一键导出 AI 说明书 |

---

## 二、环境要求

| 项 | 要求 | 说明 |
|---|---|---|
| Unity | **2021.3.45f2c1**（2021.3 LTS 均可） | 平台模块：Android Build Support（IL2CPP）/ WebGL |
| 平台模块 | Android（IL2CPP + OpenJDK/SDK/NDK）或 WebGL | 只跑编辑器则不需要 |
| Node.js | ≥ 16（仅 MCP 网关需要） | 不用 MCP 可跳过 |

**依赖已内置为 embedded package**（`Packages/com.tuyoogame.yooasset`），
**不依赖 openupm / 外网 UPM**，离线可编译。

---

## 三、快速开始（首次 5 步）

```
1) Unity Hub 用 2021.3.45f2c1 打开本目录（E:\CYFrameWork）
2) 菜单 框架/资源/初始化 YooAsset 收集器（首次必点）
3) 直接点 Play
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

> 编辑器下什么都不用准备：业务代码（`Game.Business`）由 Unity 正常编译，启动器直接调用入口。
> 出包 / 换资源才涉及"构建 → 上传 CDN"，见 [docs/03_打包与资源更新.md](docs/03_打包与资源更新.md)。

### 3.2 可选：连一个测试服务端

改 `Assets/Resources/Config/network.json` 的 `host` / `port`，起一个按
[docs/02_接入指南.md](docs/02_接入指南.md) 协议格式收发的 TCP 服务端即可。
**没有服务端时「单机进入」是唯一兜底出口** —— 框架不允许把玩家卡死在登录页。

---

## 四、双模：离线单机 / 联网

一个开关决定"要不要起网络、数据以谁为准、玩家从哪进"。**同一套业务代码，两种形态都可出包。**

| | 离线（Offline） | 联网（Online） |
|---|---|---|
| 开关 | `launch.json` → `"gameMode": 0` | `"gameMode": 1` |
| 网络 | **不创建** `NetworkService`（业务里是 null） | 创建，走 WebSocket |
| 数据 | 本地 txt **即权威** | 服务端权威，本地只做缓存 |
| 入口 | 跳过登录 → 直接主界面 | 登录页 → 握手 → 登录 |
| 存档 | `persistentDataPath/save/<槽位>/` | 同左（作为断线续玩缓存） |
| 典型场景 | 单机小游戏、离线试玩、无服务端阶段 | 账号/排行/云存档 |

**优先级（高 → 低）**：`Bootstrap.gameMode`（随场景进包，真机生效） → 菜单 `框架/游戏模式/*`（仅编辑器） → `launch.json`。
启动时 Console 会打一行实际生效值：`[Launch] 游戏模式=Offline（离线）（存档槽位=default）`。

**数据层：txt + 增删查改**

```
<persistentDataPath>/save/<槽位>/
├─ player.txt     主角（单条记录）
├─ items.txt      背包（多条记录，一行一条 JSON）
├─ settings.txt   键值设置
└─ slot.txt       槽位元信息（创建时间/最后存档/游玩次数/格式版本）
```

```csharp
var items = GameEntry.Data.Database.Table<ItemSave>();   // 首次访问自动读盘

items.Insert(new ItemSave { name = "药水", count = 2 });  // 增（id 自动生成）
var one = items.Get("items_xxx");                         // 查（按主键）
one.count += 1; items.MarkDirty();                        // 改（引用就地改 + 打脏）
items.Delete("items_xxx");                                // 删
GameEntry.Data.Save();                                    // 存（自动存档默认 20s 一次）
```

零美术也能验：主界面 →「存档 / CRUD」→ 新增 / 修改 / 删除 / 保存 / 重读 / 清档。

**联网：WebSocket（小游戏唯一可行通道）**

| 平台 | 后端 | 说明 |
|---|---|---|
| Editor / Android / iOS / PC | `DotNetWebSocketBackend`（ClientWebSocket） | 真 socket，线程池收发 |
| WebGL / 微信小游戏 | `WebGLWebSocketBackend` + `jslib` | JS 持有连接，C# 通过 `SendMessage` 收、`DllImport` 发 |

**协议完全不变**：仍是「12 字节帧头 + 二进制包体」，WebSocket 只当管道，`Send` / `Request` / `RegisterHandler` 用法一致。
小游戏必须 `wss://`（`webSocketSecure: true`）并在微信后台配 socket 合法域名。

> 细节（文件格式、CRUD 全 API、冲突策略、小游戏注意事项、排障）见 [docs/08_离线与联网.md](docs/08_离线与联网.md)。

---

## 五、目录结构

```
CYFrameWork/
├─ Assets/
│  ├─ Framework/                      ← 框架层（稳定；改动面大，少动）
│  │  ├─ Core/        GameFramework.Core      事件总线/日志/服务定位/状态机/主循环/定时器/对象池/引用池/任务池/路径
│  │  ├─ Network/     GameFramework.Network   TCP + WebSocket 通道/拆包/心跳/重连/请求配对
│  │  ├─ Data/        GameFramework.Data      离线存档：txt 序列化 + 增删查改 + 自动存档 + 文件原子写
│  │  ├─ Resource/    GameFramework.Resource  YooAsset 封装：四模式初始化 + 七段更新流程 + 加载/卸载
│  │  ├─ UI/          GameFramework.UI        五层 Canvas/面板栈/遮罩/代码搭 UI 工具
│  │  ├─ Audio/       GameFramework.Audio     BGM 交叉淡入/SFX 通道池/三组音量（不依赖 AudioMixer）
│  │  └─ Runtime/     GameFramework.Runtime   启动状态机 + 启动/更新界面 + 模式分叉
│  ├─ Game/
│  │  ├─ AOT/         Game.AOT                 唯一 AOT 入口 Bootstrap + AutoBoot 兜底
│  │  └─ Business/    Game.Business            业务层（随包编译，独立程序集）
│  ├─ BundleAssets/                            ← 所有进包资源（YooAsset 收集根，含规范文档）
│  │  ├─ UI/ Art/ Audio/ Config/ Text/ Shader/ Video/
│  ├─ Resources/
│  │  ├─ Config/      launch.json / resource.json / network.json
│  ├─ Plugins/WebGL/websocket.jslib            小游戏/WebGL 的 WebSocket 桥（JS ↔ C#）
│  ├─ Scenes/SampleScene.unity                 示例场景（挂 Bootstrap 即可跑）
│  └─ Editor/         GameFramework.Editor     打包工具链 / 导入规则 / YooAsset 规则 / MCP 桥接
├─ Packages/
│  └─ com.tuyoogame.yooasset                   YooAsset 3.0.5（embedded，离线可用）
├─ ProjectSettings/
├─ CDN/                                        构建产物按 平台/App版本/资源版本 落地（出包后生成）
└─ docs/                                       文档（见文末索引）
```

---

## 六、分层与依赖规则

### 6.1 程序集（10 个 asmdef）

| 程序集 | 位置 | 形式 | autoReferenced | 依赖 | 职责 |
|---|---|---|---|---|---|
| `GameFramework.Core` | Framework/Core | ❌ | ✅ | — | 零 Unity 依赖的工具层（除池子需要 GameObject） |
| `GameFramework.Network` | Framework/Network | ❌ | ✅ | Core | TCP / WebSocket 通道、封拆包、心跳重连 |
| `GameFramework.Data` | Framework/Data | ❌ | ✅ | Core | 离线存档：txt 序列化、增删查改、自动存档 |
| `GameFramework.Resource` | Framework/Resource | ❌ | ✅ | Core, YooAsset | 资源初始化/更新/加载唯一入口 |
| `GameFramework.UI` | Framework/UI | ❌ | ✅ | Core, Resource, UnityEngine.UI | 面板栈与 UI 构建 |
| `GameFramework.Audio` | Framework/Audio | ❌ | ✅ | Core, Resource | 音频服务 |
| `GameFramework.Runtime` | Framework/Runtime | AOT | ✅ | 上述六层, YooAsset | 启动流程 + 模式分叉（不认识业务类型） |
| `Game.AOT` | Game/AOT | ❌ | ✅ | 上述七层 | 场景入口 `Bootstrap` |
| `Game.Business` | Game/Business | **随包编译** | ✅ | 上述七层 | 业务代码（你的主角） |
| `GameFramework.Editor` | Editor | — | — | 全部 + YooAsset.Editor | 编辑器工具链 |

### 6.2 依赖方向（单向，禁止反向）

```
Game.AOT ──▶ GameFramework.Runtime ──▶ { Core, Network, Resource, Data, UI, Audio }
Game.Business ──▶ { Core, Network, Resource, Data, UI, Audio, Runtime }
                       ▲
                       └── Runtime 绝不引用 Business（业务入口由 Game.AOT 以委托传入）
```

**三条铁律**

1. **框架层稳定，业务层随便改** —— 两者分程序集：业务改动不出框架边界，编译也更快。
2. **顺序不可乱**：资源更新 → 初始化数据 → 进业务。反了必炸。
3. **代码不热更**：改代码 = 发新包；能热更的只有资源。别指望"上线后再补逻辑"。

---

## 七、核心能力与用法速查

### 7.1 启动

场景里挂 `Bootstrap`（菜单 `框架/启动/在当前场景添加 Bootstrap`），或什么都不挂 —— `AutoBoot` 会在运行时兜底创建。
`Bootstrap` 上有四个**随场景进包、真机同样生效**的字段：

| 字段 | 作用 |
|---|---|
| `autoStart` | 是否自动启动；关掉可自己调 `GameLauncher.Create().Run()` 写自动化 |
| `resourcePlayMode` | `UseConfig` / `EditorSimulate` / `Offline` / `Host` / `Web` |
| **`gameMode`** | `UseConfig` / `Offline`（单机） / `Online`（联网 WebSocket） |
| `skipResourceSystem` | 纯业务联调时跳过资源系统（资源加载失败但不阻塞启动） |

优先级（高 → 低）：**Bootstrap(Inspector) > 编辑器菜单(EditorPrefs) > json**（资源模式与游戏模式各有一条同规则的链）。
Inspector 属于"这个包明确要什么"；菜单覆盖只是开发期临时手段，不该盖掉打包决策。

### 7.2 资源：四种模式 + 七段更新流程

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
yield return res.LoadRawBytes("Config/battle_config", bytes => { /* 二进制配置 */ });
```

**下载确认必须接**（否则移动网络下静默吃掉几十 MB）：

```csharp
res.ConfirmDownload = (info, answer) => ShowDialog(info.ToPlayerText(), ok => answer(ok));
```

### 7.3 UI

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

### 7.4 网络（TCP / WebSocket，12 字节头 + 变长体，大端序）

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

内置：心跳（间隔/超时可配）、指数退避自动重连、超时回调、断线事件。

**传输方式由 `network.json` 的 `transport` 决定**：`0` = TCP（`TcpChannel`），`1` = WebSocket（`WebSocketChannel`）。

| 平台 | WebSocket 实现 |
|---|---|
| Editor / Android / iOS / PC | `ClientWebSocket`（真 socket） |
| WebGL / 小游戏 | `jslib` → 浏览器 WebSocket / `wx.connectSocket` |

在 WebGL 上即使配成 TCP，`NetworkService` 也会自动切 WebSocket 并告警（避免"编辑器好、真机连不上"）。
`ws://` 在 https 页面会被拦，小游戏必须 `"webSocketSecure": true` 用 `wss://`。

协议号分段约定见 `Framework/Network/Cmd.cs`：`1~99` 系统 / `100~999` 账号 / `1000~1999` 玩家数据 / `9000+` 调试。

### 7.5 数据存档（离线核心）

```csharp
var data  = GameEntry.Data;                                  // DataService
var items = data.Database.Table<ItemSave>();                 // 懒加载：首次访问读盘

items.Insert(new ItemSave { name = "药水", count = 2 });      // 增
var one = items.Get("items_xxx");                             // 查（主键，O(1)）
one.count += 1; items.MarkDirty();                            // 改（引用就地改 + 打脏）
items.DeleteWhere(x => x.count <= 0);                         // 删（条件）
data.Save();                                                  // 存（含原子写 + .bak）
```

实体只要求 `[Serializable]` + `implements IDataRecord` + **public 字段**（`JsonUtility` 不序列化属性）。
自动存档默认 20s 一次（`AutoSaveSeconds`），只在有脏表时写盘；退出时 `Dispose` 会补一次落盘。

### 7.6 音频

```csharp
audio.PlayBgm("Audio/Bgm/bgm_lobby", fadeSeconds: 0.8f);   // 双 source 交叉淡入，不爆音
audio.PlaySfx("Audio/Sfx/sfx_click", volume: 0.8f, pitch: 1.1f);
audio.SetVolume(AudioGroup.Bgm, 0.3f);                    // 立即生效并落 PlayerPrefs
```

三层音量：`Master × Group(Bgm/Sfx/Voice) × Clip`。不依赖 `AudioMixer`，零额外资产。

### 7.7 Core 工具

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

## 八、打包与资源更新（真机 / 出包）

```
① 框架/资源/初始化 YooAsset 收集器        （首次 / 目录变了才需要）
② 框架/资源/打开 YooAsset 打包器           → 选平台 → Build
③ 框架/资源/同步构建产物 → CDN 目录
④ 上传 CDN：CDN/<平台>/<App版本>/<资源版本>/
⑤ 出包：Build Settings → Build（勾 Copy Buildin 才有首包资源）
```

> **代码改动必须重新出包**（本框架不做代码热更）；只有资源能通过 CDN 增量更新。

CDN 目录规范：`CDN/<平台>/<App版本>/<资源版本>/`，`resource.json` 填 `hostServer` 与 `appVersion`。
`.version` 文件**必须禁缓存**（`Cache-Control: no-cache`），否则玩家永远拿不到新版本。

细节（CDN 规范、更新流程七段、验证清单、回滚）见 [docs/03_打包与资源更新.md](docs/03_打包与资源更新.md)。

---

## 九、编辑器菜单总表

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
| `框架/游戏模式/离线单机（Offline）` | 编辑器内切到单机（不启网络、本地存档权威） |
| `框架/游戏模式/联网（Online）` | 编辑器内切到联网（WebSocket） |
| `框架/游戏模式/打印当前游戏模式` | json 值 / 覆盖值 / 实际生效，三行对照 |
| `框架/启动/在当前场景添加 Bootstrap` | 建一个带序列化启动参数的入口对象 |
| `框架/启动/打开主场景` | 打开样例场景 |
| `Tools/MCP 桥接/*` | 一键配置 / 启动 / 停止 / 复制客户端配置 / 导出 AI 说明 |

---

## 十、配置项

| 文件 | 关键字段 | 说明 |
|---|---|---|
| `Resources/Config/launch.json` | `gameMode` | 游戏模式（0 离线 / 1 联网）。代码不热更，所以没有程序集名单 |
| `Resources/Config/resource.json` | `packageName` / `playMode` / `hostServer` / `fallbackHostServer` / `appVersion` / `downloadMaxConcurrency` / `requestTimeout` | 资源系统 |
| `Resources/Config/network.json` | `transport` / `webSocketSecure` / `webSocketPath` / `webSocketUrl` / `host` / `port` / `heartbeatInterval` / `heartbeatTimeout` / `autoReconnect` / `requestTimeout` | 传输方式（0 TCP / 1 WebSocket）与连接参数 |

> `JsonUtility` **静默忽略未声明字段** —— 给 json 加字段必须同步加 `ConfigModels` / `*Config` 的 `public` 字段，否则白配。

---

## 十一、资源目录规范

所有进包资源放 `Assets/BundleAssets/`，按 `UI / Art / Audio / Config / Text / Shader / Video` 划分，
每个目录有 `README.md` 写清职责与限制（手游体积上限、命名规则、禁止项）。
编辑器侧还有一套 `AssetImportRules` 自动套导入参数（纹理尺寸/压缩/平台覆盖、音频加载方式、模型压缩）。

详见 [docs/05_资源目录规范.md](docs/05_资源目录规范.md)。

---

## 十二、把 AI 接进编辑器（MCP）

```
Tools/MCP 桥接/① 一键配置 → 填项目 ID → 自动派生 → 换到空闲端口 → ② 保存并重启
Tools/MCP 桥接/③ 生成 AI 说明（MD）      → 自动复制到剪贴板，直接发给 AI
Tools/MCP 桥接/复制客户端配置             → 粘到 ~/.workbuddy/mcp.json 或 .cursor/mcp.json
```

13 个工具（8 只读 + 5 危险默认关闭）：层级/组件/资源/日志/编辑器状态查询，改工程类操作需显式开启。
标识全部收进 `Assets/Editor/Mcp/McpProjectConfig.asset`，**代码零硬编码**。

详见 [docs/06_MCP接入.md](docs/06_MCP接入.md)。

---

## 十三、常见问题（先查这里，别猜）

| 现象 | 判据 | 处置 |
|---|---|---|
| Play 后一条警告「资源降级运行」 | `BundleAssets` 里无可打包资源（只有 `.md`/`.gitkeep`） | 菜单「生成占位资源」或放入真资源 |
| 真机报"类型不存在" | 用了 `Type.GetType("字符串")` 被 Strip 裁掉 | 改直接引用，或给该类加 `[Preserve]` / 配 `link.xml` |
| 换资源后没变化 | ① CDN `.version` 变没变 ② 下载是否真的发生了 | `.version` 必须 `no-cache`；查 DNS/缓存 |
| 改了代码线上没变 | 本框架不做代码热更 | 代码随包发布，改代码必须重新出包 |
| URL 404 但浏览器能打开 | 拼出来的 URL 有双斜杠/端口 | 统一走 `PathUtil.CombineUrl` |
| 存档写了却读回旧值 | 实体用了属性而不是 public 字段 | `JsonUtility` 只序列化字段，改成字段 + 属性转发 |
| 改了数据没落盘 | `Get` 返回引用，改完没打脏 | 就地改后调 `table.MarkDirty()` |
| 联网模式进不去 / `Network` 是 null | `gameMode` 还是离线 | 改 `launch.json` 为 1，或 Bootstrap 里选 `Online` |
| 小游戏连不上 wss | `ws://` 混合内容 / 域名白名单 | `webSocketSecure: true` + 微信后台配 socket 合法域名 |

完整 30+ 条（现象 → 判据 → 处置）见 [docs/04_踩坑与判据.md](docs/04_踩坑与判据.md)。

---

## 十四、扩展点

| 我要… | 怎么做 |
|---|---|
| 加一个面板 | 继承 `UIWindow` → `ui.Register<T>(...)` → `ui.Open(...)`；有 prefab 放 `BundleAssets/UI/<模块>/` 并在收集器里 |
| 加一条协议 | `Cmd.cs` 加常量 → `net.RegisterHandler(...)` → 服务端同步改号 |
| 加一个业务模块 | 写进 `Game/Business`，**不要写进 Framework** |
| 加一个新程序集 | 新建 asmdef，`references` 指向 Core（或需要的层），**禁止反向依赖** |
| 加一个新 MCP 工具 | `Assets/Editor/Mcp/Tools/` 下加 `[McpTool("xxx")] public static object` 方法，重启桥接自动生效 |
| 换资源系统 | 只改 `GameFramework.Resource`，业务层不感知（禁直连 YooAsset） |
| 加一张数据表 | 写 `[Serializable]` + `[DataTable("xxx")]` + `IDataRecord` 的类，`Data.Database.Table<T>()` 即可用 |
| 换传输协议 | 实现 `INetChannel`（或 `IWebSocketBackend`），`NetworkService.CreateChannel` 里加一个分支 |

---

## 十五、已知边界

| 边界 | 说明 |
|---|---|
| 联网只有 WebSocket / TCP | KCP、QUIC 需自建 `INetChannel` 实现（接口已抽好） |
| UI 字体用系统字体 | 仅够 Demo；正式项目换 TMP + Noto Sans SC 并做字体子集 |
| 无账号/支付/埋点/广告 SDK | 属于业务，接 SDK 时新建独立程序集（如 `GameFramework.Sdk`），别塞进框架层 |
| 无引导/新手流程 | 框架不管玩法 |
| Web 资源模式未真机验证 | 代码路径完整（`WebServerFileSystem` + `WebNetworkFileSystem`），需在 WebGL 上实测 |
| 小游戏 jslib 未在真机跑过 | 逻辑与主流做法一致（base64 + 主线程队列 + `[Preserve]`），上线前务必在微信开发者工具 + 真机各验一次 |

---

## 十六、文档索引

| 文档 | 什么时候看 |
|---|---|
| [docs/00_总目录.md](docs/00_总目录.md) | 不知道看哪篇时，从索引进 |
| [docs/01_架构设计.md](docs/01_架构设计.md) | 想知道"为什么这么分层"、启动时序、代码/资源发布策略 |
| [docs/02_接入指南.md](docs/02_接入指南.md) | 要加面板 / 加协议 / 加载资源 / 加业务模块 |
| [docs/03_打包与资源更新.md](docs/03_打包与资源更新.md) | 要出包、要验证资源更新、要摆 CDN |
| [docs/04_踩坑与判据.md](docs/04_踩坑与判据.md) | 出了诡异问题先查这里（现象 → 判据 → 处置） |
| [docs/05_资源目录规范.md](docs/05_资源目录规范.md) | 往 `BundleAssets/` 放资源前必读 |
| [docs/06_MCP接入.md](docs/06_MCP接入.md) | 要把 AI 接进编辑器 |
| [docs/07_设计取舍与参考.md](docs/07_设计取舍与参考.md) | 对标开源框架的决策记录：借什么、不借什么、为什么 |
| [docs/08_离线与联网.md](docs/08_离线与联网.md) | 双模设计 / txt 存档格式 / CRUD 全 API / WebSocket 桥 / 小游戏注意事项 |

---

## 十七、使用约定

- 本框架**不含**任何具体项目的业务代码、美术资源、配置数据；`BundleAssets/` 里只有目录规范与占位资源。
- 框架层（`Assets/Framework`、`Assets/Game/AOT`）是 AOT 边界：**改动即发版**，落地前想清楚。
- 依赖版本锁定在 `Packages/manifest.json` + embedded package，升级 Unity / YooAsset 需要同步回归 `docs/04` 里的判据。
