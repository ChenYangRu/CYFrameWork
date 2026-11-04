# Video/ · 视频资源

## 放什么

- 过场 CG、剧情动画
- 开场 logo 动画（若确实需要视频形态）
- 宣传 / 分享用视频

## 不许放

- 首包必需的小动效（用序列帧 / Spine / 程序化动画，视频解压开销太大）
- 未转码的母版（ProRes / 无压缩 AVI）
- BGM 音频（视频自带音轨即可，不要再单独放一份）

## 限制

| 项 | 规范 |
|---|---|
| 编码 | H.264（Android）/ H.265 可选（iOS 更优） |
| 分辨率 | ≤ 1280×720（手机屏幕够用，1080p 包体翻倍） |
| 码率 | ≤ 2 Mbps |
| 单文件 | ≤ 20 MB（超过就切片） |
| 音频轨 | AAC 96 kbps，立体声 |
| 播放方式 | `VideoPlayer` + URL 流式播放，**禁止整段加载进内存** |

## 打包与运行

- 打 `hotfix` tag，**永不预加载**：点播时才下载（`YooAsset` 的 `DownloadByTag` 按需拉取）。
- 微信小游戏端不支持 `VideoPlayer` 播放包内视频，必须用平台 `Video` 组件 + CDN 直链，
  见 `docs/小游戏技术路线v2_微信优先.md` 的平台限制章节。
- 视频不进 bundle 更优：YooAsset 的 `RawFile` 管线可直接下发原始 mp4，
  避免"mp4 塞进 ab 再解包"的双倍磁盘占用。

> 当前框架用 `ScriptableBuildPipeline`，视频走 RawFile 需要单独建 package 或改收集规则，
> 一期先按普通资源处理，二期改 RawFile。
