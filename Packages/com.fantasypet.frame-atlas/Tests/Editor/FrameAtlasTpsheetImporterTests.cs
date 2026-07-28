using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FrameAtlas.Tests
{
    /// <summary>
    /// ScriptedImporter 主资源、Sprite 子资源、镜像与稳定标识测试。
    /// </summary>
    public sealed class FrameAtlasTpsheetImporterTests
    {
        private const string GeneratedFolder =
            "Assets/FrameAtlasTestsGenerated";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
            {
                AssetDatabase.CreateFolder(
                    "Assets",
                    "FrameAtlasTestsGenerated");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(GeneratedFolder))
            {
                AssetDatabase.DeleteAsset(GeneratedFolder);
            }
        }

        [Test]
        public void ImportsMainAssetSpritesAliasesAndMirrorData()
        {
            var sheetPath = CreateAtlas(
                "import",
                ":fps=9\n" +
                ":action-fps=left;7\n" +
                ":action-fps=right;15\n" +
                "# mirror;right;left;flipX=true\n" +
                "# anchor;left;x=1.5;y=4;space=output-pixels;" +
                "origin=top-left;pivot=0.5,0\n" +
                "# anchor;right;x=1.5;y=4;space=output-pixels;" +
                "origin=top-left;pivot=0.5,0\n" +
                "left_010;1;1;3;4;0.5;0\n" +
                "left_002;1;1;3;4;0.5;0\n" +
                "right_001;1;1;3;4;0.5;0\n");

            var atlas =
                AssetDatabase.LoadAssetAtPath<FrameAtlasAsset>(sheetPath);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name)
                .ToArray();

            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.Texture, Is.Not.Null);
            Assert.That(atlas.AtlasWidth, Is.EqualTo(8));
            Assert.That(atlas.AtlasHeight, Is.EqualTo(8));
            Assert.That(atlas.DefaultFps, Is.EqualTo(9f));
            Assert.That(atlas.GetFramesPerSecond("left"), Is.EqualTo(7f));
            Assert.That(atlas.GetFramesPerSecond("right"), Is.EqualTo(15f));
            Assert.That(sprites.Length, Is.EqualTo(3));
            Assert.That(sprites[0].name, Is.EqualTo("left_002"));
            Assert.That(sprites[1].name, Is.EqualTo("left_010"));
            Assert.That(sprites[2].name, Is.EqualTo("right_001"));
            Assert.That(sprites[0].rect.y, Is.EqualTo(3f));
            Assert.That(sprites[0].pivot.x, Is.EqualTo(1.5f));
            Assert.That(sprites[0].pivot.y, Is.EqualTo(0f));
            Assert.That(sprites[0].pixelsPerUnit, Is.EqualTo(100f));
            Assert.That(sprites[0].texture, Is.SameAs(sprites[2].texture));
            Assert.That(sprites[0].texture, Is.SameAs(atlas.Texture));

            var leftFrames = atlas.GetFrames("left");
            Assert.That(leftFrames.Length, Is.EqualTo(2));
            Assert.That(leftFrames[0].Name, Is.EqualTo("left_002"));
            Assert.That(leftFrames[1].Name, Is.EqualTo("left_010"));
            Assert.That(atlas.IsMirrored("right"), Is.True);
            Assert.That(
                atlas.GetSourceAction("right"),
                Is.EqualTo("left"));
            FrameAtlasActionAnchor footAnchor;
            Assert.That(
                atlas.TryGetFootAnchor("right", out footAnchor),
                Is.True);
            Assert.That(
                footAnchor.OutputPixel,
                Is.EqualTo(new Vector2(1.5f, 4f)));
            Assert.That(
                footAnchor.NormalizedPivot,
                Is.EqualTo(new Vector2(0.5f, 0f)));

            var playerObject = new GameObject("FrameAtlasPlayerTest");
            try
            {
                playerObject.AddComponent<SpriteRenderer>();
                var player = playerObject.AddComponent<FrameAtlasPlayer>();
                player.Atlas = atlas;
                player.Play("left");
                Assert.That(player.FramesPerSecond, Is.EqualTo(7f));
                player.Play("right");
                Assert.That(player.FramesPerSecond, Is.EqualTo(15f));
            }
            finally
            {
                Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void KeepsSpriteLocalFileIdentifierStableAcrossReimport()
        {
            var sheetPath = CreateAtlas(
                "stable",
                "walk_001;1;1;3;4;0.5;0\n");
            var atlas =
                AssetDatabase.LoadAssetAtPath<FrameAtlasAsset>(sheetPath);
            Assert.That(
                atlas.GetFramesPerSecond("walk"),
                Is.EqualTo(FrameAtlasAsset.DefaultFramesPerSecond));
            var before = AssetDatabase.LoadAllAssetsAtPath(sheetPath)
                .OfType<Sprite>()
                .Single();
            string beforeGuid;
            long beforeLocalId;
            Assert.That(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    before,
                    out beforeGuid,
                    out beforeLocalId),
                Is.True);

            AssetDatabase.ImportAsset(
                sheetPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            var after = AssetDatabase.LoadAllAssetsAtPath(sheetPath)
                .OfType<Sprite>()
                .Single();
            string afterGuid;
            long afterLocalId;
            Assert.That(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    after,
                    out afterGuid,
                    out afterLocalId),
                Is.True);
            Assert.That(afterGuid, Is.EqualTo(beforeGuid));
            Assert.That(afterLocalId, Is.EqualTo(beforeLocalId));
        }

        private static string CreateAtlas(
            string name,
            string frameLines)
        {
            var pngPath = GeneratedFolder + "/" + name + ".png";
            var sheetPath =
                GeneratedFolder + "/" + name + ".tpsheet";
            var texture =
                new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = Enumerable.Repeat(Color.white, 64).ToArray();
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(
                AssetPathToAbsolute(pngPath),
                texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            File.WriteAllText(
                AssetPathToAbsolute(sheetPath),
                ":format=40300\n" +
                ":texture=" + name + ".png\n" +
                ":size=8x8\n" +
                frameLines);
            AssetDatabase.ImportAsset(
                pngPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(
                sheetPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            return sheetPath;
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null);
            return Path.Combine(
                projectRoot.FullName,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
