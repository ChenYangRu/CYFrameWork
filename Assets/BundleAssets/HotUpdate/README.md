# HotUpdate/ · 热更程序集

> ⚠ **本目录由工具生成，不要手工放任何文件。**

## 内容

| 文件 | 来源 |
|---|---|
| `Game.HotUpdate.dll.bytes` | 菜单 `框架/热更/1. 编译热更 DLL → .bytes` |
| 其他热更程序集（若 `launch.json` 配了多个） | 同上，按 `hotUpdateAssemblies` 名单逐个生成 |

## 为什么后缀是 `.bytes`

Unity 不把 `.dll` 当可加载资源，无法进 AssetBundle。改成 `.bytes` 后导入为 `TextAsset`，
才能被 `ResourceService.LoadRawBytes` 读成 `byte[]` 交给 `Assembly.Load`。

**后缀不能改**，`launch.json` 的加载路径按 `<root>/<程序集名>.dll.bytes` 拼接。

## 不许放

- 手写的 DLL
- AOT 程序集（`mscorlib` 等）—— 那是**补元数据**，走 `Resources/AOTMetadata/`，随包不热更
- 任何 `.cs` 源文件

## 限制

| 项 | 规范 |
|---|---|
| 体积 | 单程序集 ≤ 4 MB（超了拆程序集，别指望压缩） |
| 程序集数量 | ≤ 3（每个都要在 `launch.json` 里按依赖顺序登记） |
| 版本一致性 | 必须与主包使用的 HybridCLR 版本一致，跨版本 DLL 会在 `Assembly.Load` 时抛 `BadImageFormatException` |
| 依赖 | 只能引用 AOT 层已存在的程序集；引用新 AOT 程序集必须发版 |

## 校验方法

菜单 `框架/热更/9. 自检：配置一致性` 会检查：

1. 程序集是否登记到 HybridCLR Settings
2. `.dll.bytes` 是否已生成到本目录
3. 补元数据文件是否存在
4. 热更 asmdef 是否关闭了 Auto Referenced

**四项全绿才允许打包。**
