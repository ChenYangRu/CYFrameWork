# UI/ · 界面资源

## 放什么

| 子目录 | 内容 | 举例 |
|---|---|---|
| `Common/` | 跨界面复用的组件、通用弹窗底框、按钮九宫、通用遮罩、界面字体 | `ui_common_btn_primary.prefab`、`ui_common_frame_popup.png` |
| `Login/` | 登录、选服、公告 | `ui_login_panel.prefab` |
| `Lobby/` | 主城 / 大厅 / 背包 / 任务 | `ui_lobby_main.prefab` |
| `Battle/` | 战斗 HUD、结算、暂停 | `ui_battle_hud.prefab` |
| `Shop/` | 商店、充值、礼包 | `ui_shop_main.prefab` |

一个界面一个**目录**（内含 prefab + 用到的图），不要把一个界面的图散到 `Common/` 里。

## 不许放

- 界面逻辑脚本（脚本在 `Game/Business/`，本目录只放资源）
- 临时对比图、废弃版本（`_old` / `_bak` / `副本`）—— 版本管理不是靠文件夹
- 未被任何 prefab 引用的孤儿图（打包会全量收进来，白占包体）

## 限制

| 项 | 规范 |
|---|---|
| 图集 | 一屏一图集，1024×1024 起，上限 2048×2048 |
| 单图 | ≤ 512×512；纯色/九宫底图 ≤ 64×64 拉伸 |
| Read/Write | **关闭** |
| Mipmap | **关闭**（UI 开 mipmap 只会让图标发糊） |
| 压缩 | Android ETC2 / iOS ASTC 6×6；带 alpha 的底框用 RGBA 压缩 |
| 字体 | 只放**子集化**后的字体资产；中文用 Noto Sans SC 子集，全字库字体禁进包 |
| prefab 层级 | 深度 ≤ 8；单界面节点数 ≤ 300（超过就拆子 prefab） |
| location | `UI/<界面>/<文件名>`，例 `UI/Login/ui_login_panel` |

## 常见坑

1. **prefab 引用了 `Common/` 的图** → 打包时 `Common` 与 `Login` 若在不同 bundle，会生成依赖链。这是允许的，但 `Common` 必须打 `builtin` tag，否则进登录前要先下载。
2. **界面图没进图集** → 每张图独立成 bundle，包体和 drawcall 双爆。检查 Sprite Atlas 是否覆盖本目录。
3. **九宫图九宫边界为 0** → 拉伸变形。导入设置里 Border 必须填。
