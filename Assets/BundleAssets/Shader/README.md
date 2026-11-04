# Shader/ · 自定义着色器

## 放什么

- 自研 `.shader` / `.shadergraph` 及其材质
- `ShaderVariantCollection`（变体裁剪集合）
- 依赖的 `.cginc` / `.hlsl`（**只放被引用的**，不要整目录拷第三方库）

## 不许放

- 引擎内置 shader 的副本（URP/Built-in 自带的不要复制进来）
- 第三方包里的 shader 原样拷贝（会与包管理器版本冲突）
- 未使用的实验性 shader（变体会全量编进包，包体和首帧编译时间双涨）

## 限制

| 项 | 规范 |
|---|---|
| 变体数 | 单个 shader ≤ 80 个 keyword 组合；超了必须拆或改用 `multi_compile_local` |
| `multi_compile` | 全量 `multi_compile` 最多 2 组；其余用 `shader_feature` |
| 变体集合 | 必须提供 `ShaderVariantCollection`，并在 `ProjectSettings/Graphics` 里登记 |
| 平台 | 需在 Android / iOS 各编译一次验证（ES3.0 与 Metal 语法差异会炸） |
| 移动端 | 禁 `#pragma target 4.5`、禁几何着色器、禁 `discard` 高频使用 |

## 为什么单独一个目录

shader 与材质的 bundle 依赖关系最容易出错：

- **shader 与使用它的材质分属不同 bundle** → 运行时材质丢 shader（粉红）
- 所以 `Shader/` 打 `builtin` tag 进首包，材质所在 bundle 依赖它，加载顺序天然正确

## 打包注意

- 打包前跑一遍 `shaderlab_lint`（见 `tools/`），语法错误在小游戏平台会直接白屏。
- `ShaderVariantCollection` 不参与热更（编进包体），变体策略改动 = 发版，**设计阶段就要定下来**。
