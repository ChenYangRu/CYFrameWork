# Audio/ · 音频资源

## 放什么

| 子目录 | 内容 | 命名例 |
|---|---|---|
| `Bgm/` | 背景音乐、循环环境音 | `bgm_lobby_main.ogg` |
| `Sfx/` | 音效：UI、技能、打击、脚步 | `sfx_ui_click.ogg` |
| `Voice/` | 角色语音、剧情配音 | `voice_mage_ult_01.ogg` |

## 不许放

- DAW 工程与未压缩母带（`.wav` 44.1kHz/16bit 以上只允许作为源，进包前必须转 OGG）
- 临时试听、参考曲（版权风险 + 包体）
- 同一音效的多版本（`_v2` / `_new` / `_final`）—— 只保留当前版本

## 限制

| 类型 | 格式 | 码率 | 单文件上限 | 加载方式 |
|---|---|---|---|---|
| BGM | OGG Vorbis | 96~128 kbps | 1.5 MB | Streaming |
| 短音效（< 1s） | OGG 或 WAV | — | 100 KB | DecompressOnLoad |
| 长音效（1~5s） | OGG | 96 kbps | 200 KB | CompressedInMemory |
| 语音 | OGG | 64 kbps | 300 KB | Streaming |

**采样率统一 44.1 kHz**，禁用 48 kHz / 96 kHz（移动端会重采样，白费内存）。

## 命名与播放

- 前缀即分类：`bgm_` / `sfx_` / `voice_`，后接模块与用途（`sfx_battle_hit_heavy`）
- 同一事件的多变体用数字后缀 `_01` `_02`，由代码随机取，不要做进不同文件名语义
- **战斗音效必须走对象池 + 限流**：同帧同音效并发 > 4 会爆音（音频通道数限制）
- BGM 切换用 `AudioManager` 交叉淡入，禁止直接 `Stop()`（会有爆音）

## 打包注意

- `Bgm/` 通常较大，打 `hotfix` tag，**进大厅前**下载完即可，不要放进首包。
- `Voice/` 按剧情章节分子目录（`Voice/Chapter01/`），实现按章下载。
- 音效是最容易被"全量收集"拖大包体的目录：**每次打包前跑一遍未引用资源检查**，孤儿音效必须删。
