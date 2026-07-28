using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrameAtlas
{
    /// <summary>
    /// 一个动作在全局帧表中的稳定索引列表。
    /// </summary>
    [Serializable]
    public sealed class FrameAtlasActionIndex
    {
        [SerializeField] private string actionId;
        [SerializeField] private List<int> frameIndices = new List<int>();

        internal FrameAtlasActionIndex(string actionId, List<int> frameIndices)
        {
            this.actionId = actionId;
            this.frameIndices = frameIndices;
        }

        /// <summary>规范化后的动作 ID。</summary>
        public string ActionId
        {
            get { return actionId; }
        }

        /// <summary>该动作按数字帧号排序后的全局帧索引。</summary>
        public IReadOnlyList<int> FrameIndices
        {
            get { return frameIndices; }
        }
    }

    /// <summary>
    /// 一个动作从 `.tpsheet` 导入的独立播放帧率。
    /// </summary>
    [Serializable]
    public sealed class FrameAtlasActionSettings
    {
        [SerializeField] private string actionId;
        [SerializeField] private float framesPerSecond;

        internal FrameAtlasActionSettings(
            string actionId,
            float framesPerSecond)
        {
            this.actionId = actionId;
            this.framesPerSecond = framesPerSecond;
        }

        /// <summary>规范化后的动作 ID。</summary>
        public string ActionId
        {
            get { return actionId; }
        }

        /// <summary>该动作每秒播放的帧数。</summary>
        public float FramesPerSecond
        {
            get { return framesPerSecond; }
        }
    }

    /// <summary>
    /// `.tpsheet` 的运行时主资源，保存纹理、帧、动作帧率与镜像规则。
    /// </summary>
    public sealed class FrameAtlasAsset : ScriptableObject
    {
        /// <summary>旧图集没有声明 `:fps` 时使用的兼容帧率。</summary>
        public const float DefaultFramesPerSecond = 12f;

        private static readonly FrameAtlasFrame[] EmptyFrames =
            new FrameAtlasFrame[0];

        [SerializeField] private Texture2D texture;
        [SerializeField] private int atlasWidth;
        [SerializeField] private int atlasHeight;
        [SerializeField] private float defaultFramesPerSecond =
            DefaultFramesPerSecond;
        [SerializeField] private List<FrameAtlasFrame> frames =
            new List<FrameAtlasFrame>();
        [SerializeField] private List<FrameAtlasActionIndex> actionIndices =
            new List<FrameAtlasActionIndex>();
        [SerializeField] private List<FrameAtlasActionSettings> actionSettings =
            new List<FrameAtlasActionSettings>();
        [SerializeField] private List<FrameAtlasMirrorMapping> mirrorMappings =
            new List<FrameAtlasMirrorMapping>();

        [NonSerialized] private Dictionary<string, FrameAtlasFrame[]> frameLookup;
        [NonSerialized] private Dictionary<string, float> framesPerSecondLookup;
        [NonSerialized] private Dictionary<string, FrameAtlasMirrorMapping>
            mirrorLookup;

        /// <summary>所有 Sprite 共同引用的源 Texture2D。</summary>
        public Texture2D Texture
        {
            get { return texture; }
        }

        /// <summary>`.tpsheet` 声明的原始图集宽度。</summary>
        public int AtlasWidth
        {
            get { return atlasWidth; }
        }

        /// <summary>`.tpsheet` 声明的原始图集高度。</summary>
        public int AtlasHeight
        {
            get { return atlasHeight; }
        }

        /// <summary>`:fps` 声明的兼容默认帧率。</summary>
        public float DefaultFps
        {
            get
            {
                return defaultFramesPerSecond > 0f
                    ? defaultFramesPerSecond
                    : DefaultFramesPerSecond;
            }
        }

        /// <summary>配置文件中的全部显式帧条目。</summary>
        public IReadOnlyList<FrameAtlasFrame> Frames
        {
            get { return frames; }
        }

        /// <summary>动作 ID 到全局帧列表的索引。</summary>
        public IReadOnlyList<FrameAtlasActionIndex> ActionIndices
        {
            get { return actionIndices; }
        }

        /// <summary>配置文件中显式声明的各动作独立帧率。</summary>
        public IReadOnlyList<FrameAtlasActionSettings> ActionSettings
        {
            get { return actionSettings; }
        }

        /// <summary>全部镜像方向映射。</summary>
        public IReadOnlyList<FrameAtlasMirrorMapping> MirrorMappings
        {
            get { return mirrorMappings; }
        }

        /// <summary>
        /// 取得动作帧。目标动作没有显式帧时，会自动返回镜像来源动作的帧。
        /// </summary>
        /// <param name="actionId">动作 ID；下划线和连字符视为同一种写法。</param>
        public FrameAtlasFrame[] GetFrames(string actionId)
        {
            EnsureLookup();
            var normalized = NormalizeActionId(actionId);
            FrameAtlasFrame[] result;
            if (frameLookup.TryGetValue(normalized, out result))
            {
                return result;
            }

            var source = GetSourceAction(normalized);
            return frameLookup.TryGetValue(source, out result)
                ? result
                : EmptyFrames;
        }

        /// <summary>
        /// 取得动作帧率。优先使用目标动作自己的 `:action-fps`；
        /// 未声明时继承镜像来源动作，最后回退到 `:fps`。
        /// </summary>
        public float GetFramesPerSecond(string actionId)
        {
            EnsureLookup();
            var normalized = NormalizeActionId(actionId);
            float result;
            if (framesPerSecondLookup.TryGetValue(normalized, out result))
            {
                return result;
            }

            FrameAtlasMirrorMapping mapping;
            if (mirrorLookup.TryGetValue(normalized, out mapping) &&
                framesPerSecondLookup.TryGetValue(
                    mapping.SourceActionId,
                    out result))
            {
                return result;
            }

            return DefaultFps;
        }

        /// <summary>判断动作播放时是否需要水平翻转。</summary>
        public bool IsMirrored(string actionId)
        {
            EnsureLookup();
            FrameAtlasMirrorMapping mapping;
            return mirrorLookup.TryGetValue(
                       NormalizeActionId(actionId),
                       out mapping) &&
                   mapping.FlipX;
        }

        /// <summary>取得动作真正复用的来源动作 ID。</summary>
        public string GetSourceAction(string actionId)
        {
            EnsureLookup();
            var normalized = NormalizeActionId(actionId);
            FrameAtlasMirrorMapping mapping;
            return mirrorLookup.TryGetValue(normalized, out mapping)
                ? mapping.SourceActionId
                : normalized;
        }

        /// <summary>
        /// 将动作 ID 规范为小写连字符形式，例如 up_right 会变为 up-right。
        /// </summary>
        public static string NormalizeActionId(string actionId)
        {
            return string.IsNullOrWhiteSpace(actionId)
                ? string.Empty
                : actionId.Trim().Replace('_', '-').ToLowerInvariant();
        }

        internal void SetImportData(
            Texture2D importedTexture,
            int width,
            int height,
            float importedDefaultFramesPerSecond,
            List<FrameAtlasFrame> importedFrames,
            List<FrameAtlasActionIndex> importedActionIndices,
            List<FrameAtlasActionSettings> importedActionSettings,
            List<FrameAtlasMirrorMapping> importedMirrorMappings)
        {
            texture = importedTexture;
            atlasWidth = width;
            atlasHeight = height;
            defaultFramesPerSecond = importedDefaultFramesPerSecond > 0f
                ? importedDefaultFramesPerSecond
                : DefaultFramesPerSecond;
            frames = importedFrames ?? new List<FrameAtlasFrame>();
            actionIndices =
                importedActionIndices ?? new List<FrameAtlasActionIndex>();
            actionSettings =
                importedActionSettings ??
                new List<FrameAtlasActionSettings>();
            mirrorMappings =
                importedMirrorMappings ?? new List<FrameAtlasMirrorMapping>();
            frameLookup = null;
            framesPerSecondLookup = null;
            mirrorLookup = null;
        }

        private void OnEnable()
        {
            frameLookup = null;
            framesPerSecondLookup = null;
            mirrorLookup = null;
        }

        private void EnsureLookup()
        {
            if (frameLookup != null &&
                framesPerSecondLookup != null &&
                mirrorLookup != null)
            {
                return;
            }

            frameLookup = new Dictionary<string, FrameAtlasFrame[]>(
                StringComparer.Ordinal);
            for (var actionIndex = 0;
                 actionIndex < actionIndices.Count;
                 actionIndex++)
            {
                var action = actionIndices[actionIndex];
                var resolved = new List<FrameAtlasFrame>();
                for (var frameIndex = 0;
                     frameIndex < action.FrameIndices.Count;
                     frameIndex++)
                {
                    var index = action.FrameIndices[frameIndex];
                    if (index >= 0 && index < frames.Count)
                    {
                        resolved.Add(frames[index]);
                    }
                }

                frameLookup[action.ActionId] = resolved.ToArray();
            }

            framesPerSecondLookup = new Dictionary<string, float>(
                StringComparer.Ordinal);
            for (var i = 0; i < actionSettings.Count; i++)
            {
                var settings = actionSettings[i];
                if (settings != null &&
                    !string.IsNullOrEmpty(settings.ActionId) &&
                    settings.FramesPerSecond > 0f)
                {
                    framesPerSecondLookup[settings.ActionId] =
                        settings.FramesPerSecond;
                }
            }

            mirrorLookup =
                new Dictionary<string, FrameAtlasMirrorMapping>(
                    StringComparer.Ordinal);
            for (var i = 0; i < mirrorMappings.Count; i++)
            {
                mirrorLookup[mirrorMappings[i].TargetActionId] =
                    mirrorMappings[i];
            }
        }
    }
}
