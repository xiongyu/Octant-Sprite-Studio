using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace FrameAtlas.Parsing
{
    /// <summary>
    /// 一条已经验证的 `.tpsheet` 帧记录。
    /// </summary>
    public sealed class FrameAtlasTpsheetFrameRecord
    {
        internal FrameAtlasTpsheetFrameRecord(
            string name,
            int x,
            int y,
            int width,
            int height,
            Vector2 pivot,
            string actionId,
            int frameNumber,
            int lineNumber)
        {
            Name = name;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Pivot = pivot;
            ActionId = actionId;
            FrameNumber = frameNumber;
            LineNumber = lineNumber;
        }

        /// <summary>配置中的完整 Sprite 名称。</summary>
        public string Name { get; private set; }

        /// <summary>左上角坐标系中的 X。</summary>
        public int X { get; private set; }

        /// <summary>左上角坐标系中的 Y。</summary>
        public int Y { get; private set; }

        /// <summary>帧矩形宽度。</summary>
        public int Width { get; private set; }

        /// <summary>帧矩形高度。</summary>
        public int Height { get; private set; }

        /// <summary>0～1 范围内的归一化轴心。</summary>
        public Vector2 Pivot { get; private set; }

        /// <summary>由帧名称提取的规范化动作 ID。</summary>
        public string ActionId { get; private set; }

        /// <summary>由名称末尾 `_001` 提取的帧号。</summary>
        public int FrameNumber { get; private set; }

        /// <summary>该记录在源文件中的一基行号。</summary>
        public int LineNumber { get; private set; }

        /// <summary>
        /// 将左上角 Y 转换为 Unity 左下角 Y：
        /// unityY = textureHeight - y - height。
        /// </summary>
        public Rect ToUnityRect(int textureHeight)
        {
            return new Rect(
                X,
                textureHeight - Y - Height,
                Width,
                Height);
        }
    }

    /// <summary>
    /// 一条已经验证的镜像动作注释。
    /// </summary>
    public sealed class FrameAtlasTpsheetMirrorRecord
    {
        internal FrameAtlasTpsheetMirrorRecord(
            string targetActionId,
            string sourceActionId,
            bool flipX,
            int lineNumber)
        {
            TargetActionId = targetActionId;
            SourceActionId = sourceActionId;
            FlipX = flipX;
            LineNumber = lineNumber;
        }

        /// <summary>需要播放的目标动作 ID。</summary>
        public string TargetActionId { get; private set; }

        /// <summary>真正提供帧的来源动作 ID。</summary>
        public string SourceActionId { get; private set; }

        /// <summary>目标动作是否需要水平翻转。</summary>
        public bool FlipX { get; private set; }

        /// <summary>该记录在源文件中的一基行号。</summary>
        public int LineNumber { get; private set; }
    }

    /// <summary>
    /// 一条已经验证的动作独立帧率声明。
    /// </summary>
    public sealed class FrameAtlasTpsheetActionFpsRecord
    {
        internal FrameAtlasTpsheetActionFpsRecord(
            string actionId,
            float framesPerSecond,
            int lineNumber)
        {
            ActionId = actionId;
            FramesPerSecond = framesPerSecond;
            LineNumber = lineNumber;
        }

        /// <summary>规范化后的动作 ID。</summary>
        public string ActionId { get; private set; }

        /// <summary>该动作每秒播放的帧数。</summary>
        public float FramesPerSecond { get; private set; }

        /// <summary>该记录在源文件中的一基行号。</summary>
        public int LineNumber { get; private set; }
    }

    /// <summary>
    /// 完整且经过验证的 `.tpsheet` 文档。
    /// </summary>
    public sealed class FrameAtlasTpsheetDocument
    {
        internal FrameAtlasTpsheetDocument(
            string textureFileName,
            int width,
            int height,
            float defaultFramesPerSecond,
            List<FrameAtlasTpsheetActionFpsRecord> actionFramesPerSecond,
            List<FrameAtlasTpsheetFrameRecord> frames,
            List<FrameAtlasTpsheetMirrorRecord> mirrorMappings)
        {
            TextureFileName = textureFileName;
            Width = width;
            Height = height;
            DefaultFramesPerSecond = defaultFramesPerSecond;
            ActionFramesPerSecond = actionFramesPerSecond;
            Frames = frames;
            MirrorMappings = mirrorMappings;
        }

        /// <summary>同目录下的源纹理文件名。</summary>
        public string TextureFileName { get; private set; }

        /// <summary>图集原始宽度。</summary>
        public int Width { get; private set; }

        /// <summary>图集原始高度。</summary>
        public int Height { get; private set; }

        /// <summary>`:fps` 声明的兼容默认帧率。</summary>
        public float DefaultFramesPerSecond { get; private set; }

        /// <summary>全部显式 `:action-fps` 声明。</summary>
        public IReadOnlyList<FrameAtlasTpsheetActionFpsRecord>
            ActionFramesPerSecond
        {
            get;
            private set;
        }

        /// <summary>全部显式帧记录。</summary>
        public IReadOnlyList<FrameAtlasTpsheetFrameRecord> Frames
        {
            get;
            private set;
        }

        /// <summary>全部镜像映射。</summary>
        public IReadOnlyList<FrameAtlasTpsheetMirrorRecord> MirrorMappings
        {
            get;
            private set;
        }
    }

    /// <summary>
    /// `.tpsheet` 内容非法时抛出的聚合解析异常。
    /// </summary>
    public sealed class FrameAtlasTpsheetParseException : Exception
    {
        internal FrameAtlasTpsheetParseException(
            string fileName,
            List<string> errors)
            : base("FrameAtlas 导入失败：" + fileName +
                   Environment.NewLine +
                   string.Join(Environment.NewLine, errors.ToArray()))
        {
            FileName = fileName;
            Errors = errors.AsReadOnly();
        }

        /// <summary>发生错误的文件名或资产路径。</summary>
        public string FileName { get; private set; }

        /// <summary>包含文件名和行号的全部错误。</summary>
        public IReadOnlyList<string> Errors { get; private set; }
    }

    /// <summary>
    /// 解析并严格验证 TexturePacker 风格 `.tpsheet` 文本。
    /// </summary>
    public static class FrameAtlasTpsheetParser
    {
        /// <summary>
        /// 解析完整文本；任何错误都会抛出带文件名和行号的异常。
        /// </summary>
        public static FrameAtlasTpsheetDocument Parse(
            string content,
            string fileName)
        {
            var errors = new List<string>();
            var frames = new List<FrameAtlasTpsheetFrameRecord>();
            var mirrors = new List<FrameAtlasTpsheetMirrorRecord>();
            var actionFramesPerSecond =
                new List<FrameAtlasTpsheetActionFpsRecord>();
            var frameNames = new HashSet<string>(StringComparer.Ordinal);
            var mirrorTargets =
                new HashSet<string>(StringComparer.Ordinal);
            var actionFpsIds =
                new HashSet<string>(StringComparer.Ordinal);
            var textureFileName = string.Empty;
            var atlasWidth = 0;
            var atlasHeight = 0;
            var defaultFramesPerSecond =
                FrameAtlasAsset.DefaultFramesPerSecond;
            var hasDefaultFramesPerSecond = false;

            using (var reader = new StringReader(content ?? string.Empty))
            {
                string rawLine;
                var lineNumber = 0;
                while ((rawLine = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    var line = rawLine.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line[0] == '#')
                    {
                        ParseComment(
                            line,
                            fileName,
                            lineNumber,
                            mirrors,
                            mirrorTargets,
                            errors);
                        continue;
                    }

                    if (line[0] == ':')
                    {
                        ParseHeader(
                            line,
                            fileName,
                            lineNumber,
                            ref textureFileName,
                            ref atlasWidth,
                            ref atlasHeight,
                            ref defaultFramesPerSecond,
                            ref hasDefaultFramesPerSecond,
                            actionFramesPerSecond,
                            actionFpsIds,
                            errors);
                        continue;
                    }

                    ParseFrame(
                        line,
                        fileName,
                        lineNumber,
                        frames,
                        frameNames,
                        errors);
                }
            }

            ValidateDocument(
                fileName,
                textureFileName,
                atlasWidth,
                atlasHeight,
                frames,
                mirrors,
                actionFramesPerSecond,
                errors);

            if (errors.Count > 0)
            {
                throw new FrameAtlasTpsheetParseException(fileName, errors);
            }

            return new FrameAtlasTpsheetDocument(
                textureFileName,
                atlasWidth,
                atlasHeight,
                defaultFramesPerSecond,
                actionFramesPerSecond,
                frames,
                mirrors);
        }

        private static void ParseHeader(
            string line,
            string fileName,
            int lineNumber,
            ref string textureFileName,
            ref int atlasWidth,
            ref int atlasHeight,
            ref float defaultFramesPerSecond,
            ref bool hasDefaultFramesPerSecond,
            List<FrameAtlasTpsheetActionFpsRecord> actionFramesPerSecond,
            HashSet<string> actionFpsIds,
            List<string> errors)
        {
            var separator = line.IndexOf('=');
            if (separator <= 1 || separator == line.Length - 1)
            {
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    "头字段必须使用 :key=value 格式。");
                return;
            }

            var key = line.Substring(1, separator - 1).Trim();
            var value = line.Substring(separator + 1).Trim();
            if (key == "fps")
            {
                if (hasDefaultFramesPerSecond)
                {
                    AddError(
                        errors,
                        fileName,
                        lineNumber,
                        "重复的 :fps 字段。");
                    return;
                }

                hasDefaultFramesPerSecond = true;
                if (!TryParsePositiveFloat(
                        value,
                        out defaultFramesPerSecond))
                {
                    defaultFramesPerSecond =
                        FrameAtlasAsset.DefaultFramesPerSecond;
                    AddError(
                        errors,
                        fileName,
                        lineNumber,
                        ":fps 必须是大于 0 的数字。");
                }
                return;
            }

            if (key == "action-fps")
            {
                ParseActionFramesPerSecond(
                    value,
                    fileName,
                    lineNumber,
                    actionFramesPerSecond,
                    actionFpsIds,
                    errors);
                return;
            }

            if (key == "texture")
            {
                if (!string.IsNullOrEmpty(textureFileName))
                {
                    AddError(
                        errors,
                        fileName,
                        lineNumber,
                        "重复的 :texture 字段。");
                    return;
                }

                textureFileName = value;
                return;
            }

            if (key != "size")
            {
                return;
            }

            if (atlasWidth > 0 || atlasHeight > 0)
            {
                AddError(errors, fileName, lineNumber, "重复的 :size 字段。");
                return;
            }

            var dimensions = value.Split('x');
            if (dimensions.Length != 2 ||
                !TryParsePositiveInt(dimensions[0], out atlasWidth) ||
                !TryParsePositiveInt(dimensions[1], out atlasHeight))
            {
                atlasWidth = 0;
                atlasHeight = 0;
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    ":size 必须是正整数 widthxheight。");
            }
        }

        private static void ParseActionFramesPerSecond(
            string value,
            string fileName,
            int lineNumber,
            List<FrameAtlasTpsheetActionFpsRecord> actionFramesPerSecond,
            HashSet<string> actionFpsIds,
            List<string> errors)
        {
            var values = value.Split(';');
            float framesPerSecond;
            var actionId = values.Length > 0
                ? FrameAtlasAsset.NormalizeActionId(values[0])
                : string.Empty;
            if (values.Length != 2 ||
                string.IsNullOrEmpty(actionId) ||
                !TryParsePositiveFloat(values[1], out framesPerSecond))
            {
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    ":action-fps 必须使用 动作ID;正数FPS 格式。");
                return;
            }

            if (!actionFpsIds.Add(actionId))
            {
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    "动作 FPS 重复：" + actionId);
                return;
            }

            actionFramesPerSecond.Add(
                new FrameAtlasTpsheetActionFpsRecord(
                    actionId,
                    framesPerSecond,
                    lineNumber));
        }

        private static void ParseComment(
            string line,
            string fileName,
            int lineNumber,
            List<FrameAtlasTpsheetMirrorRecord> mirrors,
            HashSet<string> mirrorTargets,
            List<string> errors)
        {
            var comment = line.Substring(1).Trim();
            if (!comment.StartsWith("mirror;", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var values = comment.Split(';');
            if (values.Length != 4)
            {
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    "mirror 注释必须有 4 个字段。");
                return;
            }

            var target =
                FrameAtlasAsset.NormalizeActionId(values[1]);
            var source =
                FrameAtlasAsset.NormalizeActionId(values[2]);
            bool flipX;
            const string Prefix = "flipX=";
            if (string.IsNullOrEmpty(target) ||
                string.IsNullOrEmpty(source) ||
                !values[3].StartsWith(
                    Prefix,
                    StringComparison.OrdinalIgnoreCase) ||
                !bool.TryParse(
                    values[3].Substring(Prefix.Length),
                    out flipX))
            {
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    "mirror 格式应为 # mirror;目标;来源;flipX=true。");
                return;
            }

            if (!mirrorTargets.Add(target))
            {
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    "镜像目标动作重复：" + target);
                return;
            }

            mirrors.Add(new FrameAtlasTpsheetMirrorRecord(
                target,
                source,
                flipX,
                lineNumber));
        }

        private static void ParseFrame(
            string line,
            string fileName,
            int lineNumber,
            List<FrameAtlasTpsheetFrameRecord> frames,
            HashSet<string> frameNames,
            List<string> errors)
        {
            var values = line.Split(';');
            if (values.Length != 7)
            {
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    "帧记录必须恰好包含 7 个字段。");
                return;
            }

            var name = values[0].Trim();
            if (string.IsNullOrEmpty(name))
            {
                AddError(errors, fileName, lineNumber, "Sprite 名称不能为空。");
                return;
            }

            if (!frameNames.Add(name))
            {
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    "Sprite 名称重复：" + name);
                return;
            }

            int x;
            int y;
            int width;
            int height;
            float pivotX;
            float pivotY;
            if (!TryParseInt(values[1], out x) ||
                !TryParseInt(values[2], out y) ||
                !TryParseInt(values[3], out width) ||
                !TryParseInt(values[4], out height) ||
                !TryParseFloat(values[5], out pivotX) ||
                !TryParseFloat(values[6], out pivotY))
            {
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    "帧坐标、尺寸或 pivot 包含非法数字。");
                return;
            }

            string actionId;
            int frameNumber;
            if (!TryParseFrameName(name, out actionId, out frameNumber))
            {
                AddError(
                    errors,
                    fileName,
                    lineNumber,
                    "帧名称必须以数字后缀结尾，例如 walk_001。");
                return;
            }

            frames.Add(new FrameAtlasTpsheetFrameRecord(
                name,
                x,
                y,
                width,
                height,
                new Vector2(pivotX, pivotY),
                actionId,
                frameNumber,
                lineNumber));
        }

        private static void ValidateDocument(
            string fileName,
            string textureFileName,
            int atlasWidth,
            int atlasHeight,
            List<FrameAtlasTpsheetFrameRecord> frames,
            List<FrameAtlasTpsheetMirrorRecord> mirrors,
            List<FrameAtlasTpsheetActionFpsRecord> actionFramesPerSecond,
            List<string> errors)
        {
            if (string.IsNullOrEmpty(textureFileName))
            {
                AddError(errors, fileName, 0, "缺少 :texture 字段。");
            }
            else if (!string.Equals(
                         Path.GetFileName(textureFileName),
                         textureFileName,
                         StringComparison.Ordinal) ||
                     !string.Equals(
                         Path.GetExtension(textureFileName),
                         ".png",
                         StringComparison.OrdinalIgnoreCase))
            {
                AddError(
                    errors,
                    fileName,
                    0,
                    ":texture 必须是同目录下的 PNG 文件名。");
            }

            if (atlasWidth <= 0 || atlasHeight <= 0)
            {
                AddError(errors, fileName, 0, "缺少合法的 :size 字段。");
            }

            if (frames.Count == 0)
            {
                AddError(errors, fileName, 0, "图集中没有任何合法帧。");
            }

            var actions = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                actions.Add(frame.ActionId);
                if (frame.X < 0 ||
                    frame.Y < 0 ||
                    frame.Width <= 0 ||
                    frame.Height <= 0 ||
                    frame.X + frame.Width > atlasWidth ||
                    frame.Y + frame.Height > atlasHeight)
                {
                    AddError(
                        errors,
                        fileName,
                        frame.LineNumber,
                        "帧矩形越界：" + frame.Name);
                }

                if (frame.Pivot.x < 0f ||
                    frame.Pivot.x > 1f ||
                    frame.Pivot.y < 0f ||
                    frame.Pivot.y > 1f)
                {
                    AddError(
                        errors,
                        fileName,
                        frame.LineNumber,
                        "pivot 必须位于 0～1：" + frame.Name);
                }
            }

            for (var i = 0; i < mirrors.Count; i++)
            {
                var mirror = mirrors[i];
                if (!actions.Contains(mirror.SourceActionId))
                {
                    AddError(
                        errors,
                        fileName,
                        mirror.LineNumber,
                        "镜像来源动作不存在：" + mirror.SourceActionId);
                }

                if (mirror.TargetActionId == mirror.SourceActionId)
                {
                    AddError(
                        errors,
                        fileName,
                        mirror.LineNumber,
                        "镜像目标不能与来源动作相同。");
                }
            }

            var availableActions =
                new HashSet<string>(actions, StringComparer.Ordinal);
            for (var i = 0; i < mirrors.Count; i++)
            {
                availableActions.Add(mirrors[i].TargetActionId);
            }

            for (var i = 0; i < actionFramesPerSecond.Count; i++)
            {
                var settings = actionFramesPerSecond[i];
                if (!availableActions.Contains(settings.ActionId))
                {
                    AddError(
                        errors,
                        fileName,
                        settings.LineNumber,
                        "动作 FPS 对应的动作不存在：" +
                        settings.ActionId);
                }
            }
        }

        private static bool TryParseFrameName(
            string name,
            out string actionId,
            out int frameNumber)
        {
            actionId = string.Empty;
            frameNumber = 0;
            var separator = name.LastIndexOf('_');
            if (separator <= 0 || separator >= name.Length - 1)
            {
                return false;
            }

            if (!int.TryParse(
                    name.Substring(separator + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out frameNumber))
            {
                return false;
            }

            actionId = FrameAtlasAsset.NormalizeActionId(
                name.Substring(0, separator));
            return !string.IsNullOrEmpty(actionId);
        }

        private static bool TryParseInt(string value, out int result)
        {
            return int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result);
        }

        private static bool TryParsePositiveInt(
            string value,
            out int result)
        {
            return TryParseInt(value, out result) && result > 0;
        }

        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(
                       value.Trim(),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out result) &&
                   !float.IsNaN(result) &&
                   !float.IsInfinity(result);
        }

        private static bool TryParsePositiveFloat(
            string value,
            out float result)
        {
            return TryParseFloat(value, out result) && result > 0f;
        }

        private static void AddError(
            List<string> errors,
            string fileName,
            int lineNumber,
            string message)
        {
            errors.Add(lineNumber > 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}({1}): {2}",
                    fileName,
                    lineNumber,
                    message)
                : fileName + ": " + message);
        }
    }
}
