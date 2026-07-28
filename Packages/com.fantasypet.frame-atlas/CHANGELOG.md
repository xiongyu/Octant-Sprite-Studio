# Changelog

## 0.2.0

- 新增 `# anchor` 脚底锚点解析、范围校验和帧 Pivot 一致性校验。
- `FrameAtlasAsset` 新增 `ActionAnchors` 与 `TryGetFootAnchor` 运行时接口。
- ScriptedImporter 会保存显式脚底像素坐标与 Unity 归一化 Pivot。
- 保持不含 `# anchor` 的旧 `.tpsheet` 向后兼容。

## 0.1.0

- 抽取为独立 Unity Package。
- 将 `.tpsheet` 解析器移入 `FrameAtlas.Runtime`，允许运行时和编辑器共同调用。
- 保留 `.tpsheet` ScriptedImporter、Sprite 子资源、动作帧率和镜像播放支持。
- 保留解析与导入回归测试。
