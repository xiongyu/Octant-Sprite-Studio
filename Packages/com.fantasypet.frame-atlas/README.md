# Fantasy Pet Frame Atlas

项目自研的 Unity 图集解析与播放运行库。包内没有游戏业务依赖，可整体复制到其他 Unity 2022.3 / 团结引擎 1.9.2 项目。

## 模块边界

- `Runtime/Parsing/FrameAtlasTpsheetParser.cs`
  - 运行时可调用的 `.tpsheet` 文本解析与严格校验。
  - 入口：`FrameAtlas.Parsing.FrameAtlasTpsheetParser.Parse(content, fileName)`。
- `Runtime/`
  - `FrameAtlasAsset`、帧/动作/镜像数据模型与 `FrameAtlasPlayer`。
- `Editor/`
  - 可选的 Unity `ScriptedImporter` 和 Inspector。
  - 自动把 `.tpsheet` 与同目录 PNG 导入为 `FrameAtlasAsset` 和稳定 Sprite 子资源。
- `Tests/Editor/`
  - 解析器和导入器回归测试。

`FrameAtlas.Runtime` 不引用 `UnityEditor`。播放器构建后只读取已导入的
`FrameAtlasAsset`；若工具需要在运行时检查原始文本，可直接调用解析器。

## 合并到另一个 Unity 项目

复制整个 `com.fantasypet.frame-atlas` 文件夹到目标项目的 `Packages/`
目录。程序集名保持为：

- `FrameAtlas.Runtime`
- `FrameAtlas.Editor`

目标项目的 asmdef 只需引用 `FrameAtlas.Runtime`；只有扩展导入器时才引用
`FrameAtlas.Editor`。

## `.tpsheet` 最小格式

```text
:texture=hero.png
:size=512x512
:fps=12
:action-fps=walk;10
# anchor;walk;x=32;y=60;space=output-pixels;origin=top-left;pivot=0.5,0.0625
# mirror;right;left;flipX=true
walk_001;0;0;64;64;0.5;0.0625
```

帧字段依次为：

`名称;x;y;宽;高;pivotX;pivotY`

坐标原点按图像左上角解析；`ToUnityRect` 会转换为 Unity 左下角坐标。
帧名必须以数字帧号结尾，例如 `walk_001`。动作 ID 会转换为小写连字符形式。

`# anchor` 保存动作在输出帧中的显式脚底点。`x/y` 使用左上角像素坐标，
`pivot` 使用 Unity 左下角归一化坐标；解析器会验证它们彼此一致，并验证同动作
每帧的 Pivot 都与该脚底点一致。旧文件可以不含此注释。

## 运行时解析示例

```csharp
using FrameAtlas.Parsing;

var document = FrameAtlasTpsheetParser.Parse(text, "hero.tpsheet");
foreach (var frame in document.Frames)
{
    UnityEngine.Debug.Log(
        $"{frame.ActionId} #{frame.FrameNumber}: {frame.ToUnityRect(document.Height)}");
}
```

## 导入后的播放示例

```csharp
using FrameAtlas;

public FrameAtlasAsset atlas;

void Start()
{
    var frames = atlas.GetFrames("walk");
    var fps = atlas.GetFramesPerSecond("walk");
    FrameAtlasActionAnchor footAnchor;
    if (atlas.TryGetFootAnchor("walk", out footAnchor))
    {
        UnityEngine.Debug.Log(
            $"Foot: {footAnchor.OutputPixel}, Pivot: {footAnchor.NormalizedPivot}");
    }
}
```
