using System;
using System.Collections.Generic;
using System.IO;
using FrameAtlas.Parsing;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FrameAtlas.Editor
{
    /// <summary>
    /// 将 TexturePacker 风格 `.tpsheet` 导入为 FrameAtlasAsset 和 Sprite 子资源。
    /// </summary>
    [ScriptedImporter(2, "tpsheet")]
    public sealed class FrameAtlasTpsheetImporter : ScriptedImporter
    {
        [SerializeField, Min(0.01f)] private float pixelsPerUnit = 100f;
        [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;
        [SerializeField] private TextureWrapMode wrapMode =
            TextureWrapMode.Clamp;
        [SerializeField] private bool mipmapEnabled;
        [SerializeField] private TextureImporterCompression textureCompression =
            TextureImporterCompression.CompressedHQ;

        internal float PixelsPerUnit
        {
            get { return Mathf.Max(0.01f, pixelsPerUnit); }
        }

        internal FilterMode SourceFilterMode
        {
            get { return filterMode; }
        }

        internal TextureWrapMode SourceWrapMode
        {
            get { return wrapMode; }
        }

        internal bool SourceMipmapEnabled
        {
            get { return mipmapEnabled; }
        }

        internal TextureImporterCompression SourceTextureCompression
        {
            get { return textureCompression; }
        }

        /// <summary>
        /// 解析 `.tpsheet`、声明 PNG 依赖，并创建稳定的 Sprite 子资源。
        /// </summary>
        public override void OnImportAsset(AssetImportContext context)
        {
            List<Sprite> createdSprites = null;
            try
            {
                var absoluteSheetPath =
                    AssetPathToAbsolute(context.assetPath);
                var document = FrameAtlasTpsheetParser.Parse(
                    File.ReadAllText(absoluteSheetPath),
                    context.assetPath);
                var textureAssetPath = BuildTextureAssetPath(
                    context.assetPath,
                    document.TextureFileName);
                context.DependsOnSourceAsset(textureAssetPath);

                if (!File.Exists(AssetPathToAbsolute(textureAssetPath)))
                {
                    context.LogImportError(
                        context.assetPath +
                        ": 找不到同目录纹理 " +
                        document.TextureFileName);
                    return;
                }

                var texture =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        textureAssetPath);
                if (texture == null)
                {
                    context.LogImportError(
                        context.assetPath +
                        ": PNG 尚未成功导入为 Texture2D：" +
                        textureAssetPath);
                    return;
                }

                if (texture.width != document.Width ||
                    texture.height != document.Height)
                {
                    context.LogImportError(
                        string.Format(
                            "{0}: :size={1}x{2}，但导入后的 PNG 是 {3}x{4}。" +
                            "请重新导入 PNG，且不要让 Max Size 缩小源纹理。",
                            context.assetPath,
                            document.Width,
                            document.Height,
                            texture.width,
                            texture.height));
                    return;
                }

                createdSprites =
                    new List<Sprite>(document.Frames.Count);
                var runtimeFrames =
                    new List<FrameAtlasFrame>(document.Frames.Count);
                for (var i = 0; i < document.Frames.Count; i++)
                {
                    var sourceFrame = document.Frames[i];
                    var unityRect =
                        sourceFrame.ToUnityRect(document.Height);
                    var sprite = Sprite.Create(
                        texture,
                        unityRect,
                        sourceFrame.Pivot,
                        PixelsPerUnit,
                        0,
                        SpriteMeshType.FullRect,
                        Vector4.zero,
                        false);
                    sprite.name = sourceFrame.Name;
                    createdSprites.Add(sprite);
                    runtimeFrames.Add(new FrameAtlasFrame(
                        sourceFrame.Name,
                        sourceFrame.ActionId,
                        sourceFrame.FrameNumber,
                        sprite,
                        unityRect,
                        sourceFrame.Pivot));
                }

                var actionIndices = BuildActionIndices(runtimeFrames);
                var actionSettings =
                    new List<FrameAtlasActionSettings>(
                        document.ActionFramesPerSecond.Count);
                for (var i = 0;
                     i < document.ActionFramesPerSecond.Count;
                     i++)
                {
                    var settings =
                        document.ActionFramesPerSecond[i];
                    actionSettings.Add(
                        new FrameAtlasActionSettings(
                            settings.ActionId,
                            settings.FramesPerSecond));
                }

                var mirrorMappings =
                    new List<FrameAtlasMirrorMapping>(
                        document.MirrorMappings.Count);
                for (var i = 0;
                     i < document.MirrorMappings.Count;
                     i++)
                {
                    var mapping = document.MirrorMappings[i];
                    mirrorMappings.Add(new FrameAtlasMirrorMapping(
                        mapping.TargetActionId,
                        mapping.SourceActionId,
                        mapping.FlipX));
                }

                var actionAnchors =
                    new List<FrameAtlasActionAnchor>(
                        document.ActionAnchors.Count);
                for (var i = 0;
                     i < document.ActionAnchors.Count;
                     i++)
                {
                    var anchor = document.ActionAnchors[i];
                    actionAnchors.Add(new FrameAtlasActionAnchor(
                        anchor.ActionId,
                        anchor.PixelPosition,
                        anchor.NormalizedPivot));
                }

                var atlas =
                    ScriptableObject.CreateInstance<FrameAtlasAsset>();
                atlas.name =
                    Path.GetFileNameWithoutExtension(context.assetPath);
                atlas.SetImportData(
                    texture,
                    document.Width,
                    document.Height,
                    document.DefaultFramesPerSecond,
                    runtimeFrames,
                    actionIndices,
                    actionSettings,
                    actionAnchors,
                    mirrorMappings);

                context.AddObjectToAsset("frame-atlas-main", atlas);
                context.SetMainObject(atlas);
                for (var i = 0; i < createdSprites.Count; i++)
                {
                    // identifier 只由 Sprite 名称决定；名称重复已在解析期拒绝。
                    // 因此重新导入和删除 Library 后仍能得到同一 Local File ID。
                    context.AddObjectToAsset(
                        "sprite:" + createdSprites[i].name,
                        createdSprites[i]);
                }
            }
            catch (FrameAtlasTpsheetParseException exception)
            {
                DestroyCreatedSprites(createdSprites);
                context.LogImportError(exception.Message);
            }
            catch (Exception exception)
            {
                DestroyCreatedSprites(createdSprites);
                context.LogImportError(
                    context.assetPath +
                    ": FrameAtlas 导入异常：" +
                    exception.Message);
            }
        }

        internal static string BuildTextureAssetPath(
            string sheetAssetPath,
            string textureFileName)
        {
            var directory = Path.GetDirectoryName(sheetAssetPath);
            return string.IsNullOrEmpty(directory)
                ? textureFileName
                : (directory.Replace('\\', '/') + "/" + textureFileName);
        }

        internal static string AssetPathToAbsolute(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            return projectRoot == null
                ? assetPath
                : Path.Combine(
                    projectRoot.FullName,
                    assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static bool TryGetTextureAssetPath(
            string sheetAssetPath,
            out string textureAssetPath)
        {
            textureAssetPath = string.Empty;
            try
            {
                var document = FrameAtlasTpsheetParser.Parse(
                    File.ReadAllText(AssetPathToAbsolute(sheetAssetPath)),
                    sheetAssetPath);
                textureAssetPath = BuildTextureAssetPath(
                    sheetAssetPath,
                    document.TextureFileName);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<FrameAtlasActionIndex> BuildActionIndices(
            List<FrameAtlasFrame> frames)
        {
            var grouped = new Dictionary<string, List<int>>(
                StringComparer.Ordinal);
            for (var i = 0; i < frames.Count; i++)
            {
                List<int> indices;
                if (!grouped.TryGetValue(frames[i].ActionId, out indices))
                {
                    indices = new List<int>();
                    grouped.Add(frames[i].ActionId, indices);
                }

                indices.Add(i);
            }

            var actions = new List<string>(grouped.Keys);
            actions.Sort(StringComparer.Ordinal);
            var result =
                new List<FrameAtlasActionIndex>(actions.Count);
            for (var actionIndex = 0;
                 actionIndex < actions.Count;
                 actionIndex++)
            {
                var actionId = actions[actionIndex];
                var indices = grouped[actionId];
                indices.Sort(delegate(int left, int right)
                {
                    var frameOrder =
                        frames[left].FrameNumber.CompareTo(
                            frames[right].FrameNumber);
                    return frameOrder != 0
                        ? frameOrder
                        : string.CompareOrdinal(
                            frames[left].Name,
                            frames[right].Name);
                });
                result.Add(new FrameAtlasActionIndex(actionId, indices));
            }

            return result;
        }

        private static void DestroyCreatedSprites(List<Sprite> sprites)
        {
            if (sprites == null)
            {
                return;
            }

            for (var i = 0; i < sprites.Count; i++)
            {
                if (sprites[i] != null)
                {
                    DestroyImmediate(sprites[i]);
                }
            }
        }
    }

    /// <summary>
    /// 为带同名 `.tpsheet` 的 PNG 应用导入器 Inspector 中的纹理设置。
    /// 这里只修改当前 TextureImporter，不调用 AssetDatabase，因而不会形成刷新死循环。
    /// </summary>
    internal sealed class FrameAtlasSourceTexturePostprocessor :
        AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!string.Equals(
                    Path.GetExtension(assetPath),
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var sheetAssetPath =
                Path.ChangeExtension(assetPath, ".tpsheet")
                    .Replace('\\', '/');
            if (!File.Exists(
                    FrameAtlasTpsheetImporter.AssetPathToAbsolute(
                        sheetAssetPath)))
            {
                return;
            }

            var sourceImporter = (TextureImporter)assetImporter;
            var sheetImporter =
                AssetImporter.GetAtPath(sheetAssetPath) as
                    FrameAtlasTpsheetImporter;
            sourceImporter.textureType = TextureImporterType.Default;
            sourceImporter.sRGBTexture = true;
            sourceImporter.alphaSource =
                TextureImporterAlphaSource.FromInput;
            sourceImporter.alphaIsTransparency = true;
            sourceImporter.isReadable = false;
            sourceImporter.npotScale = TextureImporterNPOTScale.None;
            sourceImporter.mipmapEnabled =
                sheetImporter != null &&
                sheetImporter.SourceMipmapEnabled;
            sourceImporter.filterMode = sheetImporter == null
                ? FilterMode.Bilinear
                : sheetImporter.SourceFilterMode;
            sourceImporter.wrapMode = sheetImporter == null
                ? TextureWrapMode.Clamp
                : sheetImporter.SourceWrapMode;
            sourceImporter.textureCompression = sheetImporter == null
                ? TextureImporterCompression.CompressedHQ
                : sheetImporter.SourceTextureCompression;
            sourceImporter.maxTextureSize =
                RequiredMaxTextureSize(sheetAssetPath);
        }

        private static int RequiredMaxTextureSize(string sheetAssetPath)
        {
            try
            {
                var document = FrameAtlasTpsheetParser.Parse(
                    File.ReadAllText(
                        FrameAtlasTpsheetImporter.AssetPathToAbsolute(
                            sheetAssetPath)),
                    sheetAssetPath);
                return Mathf.Clamp(
                    Mathf.NextPowerOfTwo(
                        Mathf.Max(document.Width, document.Height)),
                    32,
                    8192);
            }
            catch
            {
                return 8192;
            }
        }
    }
}
