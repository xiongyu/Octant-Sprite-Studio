# Octant Sprite Studio

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./public/branding/octant-logo-light.png">
  <source media="(prefers-color-scheme: light)" srcset="./public/branding/octant-logo-dark.png">
  <img alt="Octant Sprite Studio Logo" src="./public/branding/octant-logo-dark.png" width="900">
</picture>

> 中文名：逐格

一个本地优先的多方向 PNG 序列帧工作台，用于管理人物动作、播放预览、裁剪留白、中心校准、非破坏性减帧、方向镜像、轻量动态修饰以及 Unity 图集导出。

## 运行界面

![Octant Sprite Studio 运行界面](./docs/images/octant-sprite-studio.jpg)

## 仓库信息

- **Repository**：`octant-sprite-studio`
- **Product**：Octant Sprite Studio
- **中文名**：逐格
- **Description**：A local-first directional sprite sequence editor for cropping, alignment, frame thinning, mirroring, per-action FPS, and Unity atlas export.

品牌资产位于 `public/branding/`，包含透明背景 App Icon，以及适配深色、浅色背景的横版 Logo。

## 功能

### 项目与动作管理

- 使用“项目 → 人物 → 动作”三级结构管理素材。
- 项目与人物支持折叠和重命名。
- 不同项目、人物中的同名动作彼此独立。
- 每个动作可单独设置 FPS、位置偏移和叠加透明度。
- 每个人物可定义一个由全部方向共用的脚底锚点，可在画布上点选或按当前帧估算。
- 多个动作可同时显示，用半透明叠加方式校正人物中心。
- 右侧检查器收敛为“常规”和“动态”两个停靠面板；裁剪、对齐、镜像、导出与减帧集中在常规面板中。
- 两个面板可拖拽合并为标签，或停靠到目标面板的上、下、左、右。
- 支持拖动分割条与右侧边界调整尺寸，并自动保存当前工作区布局。

### 序列编辑

- 播放、暂停、逐帧切换与循环预览。
- 播放时自动使用当前动作的独立 FPS。
- 拖动裁剪框或输入 X、Y、宽、高，移除人物两侧的多余空白。
- 根据透明像素分析人物边界，并忽略孤立杂点。
- 按当前帧的人物中心和脚底自动对齐动作。
- 每隔 N 帧移除一帧，可重复执行并随时恢复原始帧。

所有减帧、裁剪和校准操作均为非破坏性处理，不会删除或修改磁盘上的原始 PNG。

### 方向镜像

- 缺少左/右、左上/右上或左下/右下动作时，可自动水平镜像补齐。
- 即使左右素材都存在，也能主动选择一侧作为镜像源，以减少图集体积。
- 上、下方向不会互相镜像。
- Unity 图集只写入实体纹理帧，镜像动作通过 `flipX` 配置复用已有区域。

### 轻量动态修饰

- 为每个动作独立设置水平/垂直摆动、旋转、呼吸缩放、周期和起始相位。
- 内置左右漂移、上下浮动、轻微摇摆、呼吸缩放四种预设。
- 动态以正弦曲线围绕人物脚底锚点播放，与序列帧共用播放控制。
- 动态不会烘焙进 PNG；Unity 图集额外导出 `character_walk.motion.json`，并在 `.tpsheet` 中写入 `# motion` 元数据。

### 导出

- 将所选动作导出为裁剪后的 PNG 序列 ZIP。
- PNG 序列按“项目 / 人物 / 动作”目录保存，并附带 `crop.json`。
- 将多个动作合并为一张透明 PNG 图集和一份 `.tpsheet` 配置。
- 支持 25%～100% 等比例缩放。
- 支持无损、轻度、中度、高度四档 PNG 压缩。
- 实时预览缩放、压缩效果和单帧体积。
- 每个动作的独立 FPS、项目归属和镜像关系都会写入导出配置。
- 脚底锚点会写入 `crop.json`、`.tpsheet` 的 `# anchor` 元数据和每帧 Pivot。

## 快速开始

需要 Node.js 20 或更高版本。

```bash
npm install
npm run dev
```

浏览器打开：

```text
http://127.0.0.1:5173/
```

生产构建：

```bash
npm run build
npm run preview
```

## 素材目录

点击“导入项目文件夹”，选择符合以下结构的目录：

```text
我的项目/
├─ 勇者/
│  ├─ 上行走/
│  │  ├─ 上行走_001.png
│  │  └─ 上行走_002.png
│  ├─ 下行走/
│  ├─ 左行走/
│  └─ 右行走/
└─ 法师/
   ├─ 上行走/
   ├─ 下行走/
   └─ 左行走/
```

标准路径为：

```text
项目 / 人物 / 动作 / PNG
```

帧文件会按文件名末尾的数字自然排序。缺少可镜像的水平方向时，编辑器会在对应人物内补齐，不会跨人物复用素材。

## 使用流程

1. 导入项目文件夹。
2. 在左侧选择人物和动作。
3. 设置动作 FPS，并播放检查节奏。
4. 调整公共裁剪框，去除多余透明区域。
5. 叠加多个动作，校准人物中心和脚底。
6. 按需减帧、缩放、压缩或设置方向镜像。
7. 选择要导出的动作。
8. 导出 PNG 序列或 Unity 图集。

## Unity 图集格式

点击“Unity 图集”后会得到一个 ZIP，主要包含：

```text
character_walk.png
character_walk.tpsheet
character_walk.motion.json
```

`.tpsheet` 使用 TexturePacker 风格的文本切片格式，并扩展了项目、动作 FPS 和镜像元数据：

```text
:format=40300
:texture=character_walk.png
:size=2048x2048
:fps=12
:action-fps=down;12
:action-fps=left;8
# action;down;默认项目;主角;下行走;fps=12
# anchor;down;x=49.5;y=172;space=output-pixels;origin=top-left;pivot=0.5,0.060109
# action;left;默认项目;主角;左行走;fps=8
# mirror;right;left;flipX=true
# motion;down;x=12;y=0;rotation=0;scale=0;duration=3.2;phase=0;wave=sine;pivot=0.5,0.060109
down_001;2;2;99;183;0.5;0.060109
left_001;105;2;99;183;0.5;0.060109
```

字段说明：

- `:fps`：兼容旧解析逻辑的默认帧率。
- `:action-fps=动作ID;帧率`：动作独立 FPS，Unity 解析时应优先使用。
- `# action`：动作 ID、项目名、人物名、动作名和 FPS。
- `# mirror`：目标动作、源动作和水平翻转标记。
- `# anchor`：动作脚底点的输出像素坐标、坐标系和 Unity 归一化 Pivot。
- `# motion`：运行时位移、旋转、缩放、周期、相位、波形和 Pivot；位移单位为缩放后的输出像素。
- 普通帧行：名称、X、Y、宽、高、Pivot X、Pivot Y。

该文件用于自定义 Unity 解析库。官方 TexturePacker Importer 可能会校验文件来源并拒绝手写 `.tpsheet`，因此不要依赖官方插件直接导入。

图集内 Sprite 的 Pivot 来自人物脚底锚点。脚底坐标使用输出帧左上角像素坐标，
导出时会自动换算为 Unity 左下角归一化 Pivot。所选人物缺少脚底定义，或锚点落在
裁剪框外时，编辑器会阻止 Unity 图集导出。如果图集超过 Unity 默认纹理尺寸，请
提高 Texture Import Settings 中的 `Max Size`，或在导出前降低缩放比例。

## Unity Package

仓库内置标准 Unity Package：
[`Packages/com.fantasypet.frame-atlas`](./Packages/com.fantasypet.frame-atlas)。
它包含运行时数据模型、严格的 `.tpsheet` 解析器、播放器，以及可选的
Unity Editor `ScriptedImporter`。支持 Unity 2022.3 和团结引擎 1.9.2，
不依赖具体游戏业务代码。

关键文件：

- [Package 接入说明](./Packages/com.fantasypet.frame-atlas/README.md)
- [运行时解析器](./Packages/com.fantasypet.frame-atlas/Runtime/Parsing/FrameAtlasTpsheetParser.cs)
- [Package 配置](./Packages/com.fantasypet.frame-atlas/package.json)

### 安装到 Unity 项目

1. 将整个 `Packages/com.fantasypet.frame-atlas` 文件夹复制到目标 Unity
   项目的 `Packages/` 目录。
2. 在业务程序集的 `.asmdef` 中引用 `FrameAtlas.Runtime`。
3. 只有需要扩展编辑器导入功能时，才引用 `FrameAtlas.Editor`。
4. 将编辑器导出的 `.png` 与 `.tpsheet` 放在同一目录。Unity 会通过
   `ScriptedImporter` 生成 `FrameAtlasAsset` 和稳定的 Sprite 子资源。

### 运行时解析

需要直接读取 `.tpsheet` 文本时：

```csharp
using FrameAtlas.Parsing;

var document = FrameAtlasTpsheetParser.Parse(content, fileName);
foreach (var frame in document.Frames)
{
    UnityEngine.Debug.Log(
        $"{frame.ActionId} #{frame.FrameNumber}: {frame.ToUnityRect(document.Height)}");
}
```

解析入口的完整名称：

```csharp
FrameAtlas.Parsing.FrameAtlasTpsheetParser.Parse(content, fileName)
```

### 播放已导入图集

```csharp
using FrameAtlas;

public FrameAtlasAsset atlas;

void Start()
{
    var frames = atlas.GetFrames("walk");
    var fps = atlas.GetFramesPerSecond("walk");
}
```

## 快捷键

| 快捷键 | 操作 |
| --- | --- |
| `Space` | 播放或暂停 |
| `←` / `→` | 上一帧 / 下一帧 |
| `Shift` + 方向键 | 微调当前动作位置 |

## 技术栈

- Vue 3
- Vite
- JSZip
- UPNG.js
- Phosphor Icons
- Canvas 2D

## 本地数据

- 导入的本地图片通过浏览器临时对象地址读取，不会上传到服务器。
- 内置素材的裁剪、偏移、镜像、减帧和 FPS 设置保存在浏览器 `localStorage`。
- PNG 序列和 Unity 图集使用编辑器当前帧列表导出。
- 发布仓库前，请自行确认示例人物素材的版权和分发权限。
