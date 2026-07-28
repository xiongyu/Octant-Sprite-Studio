using FrameAtlas.Parsing;
using NUnit.Framework;

namespace FrameAtlas.Tests
{
    /// <summary>
    /// `.tpsheet` 文本解析与坐标验证测试。
    /// </summary>
    public sealed class FrameAtlasTpsheetParserTests
    {
        [Test]
        public void ParsesValidDocument()
        {
            var document = FrameAtlasTpsheetParser.Parse(
                ":format=40300\n" +
                ":texture=hero.png\n" +
                ":size=200x300\n" +
                "walk_001;1;2;30;40;0.5;0\n",
                "valid.tpsheet");

            Assert.That(document.TextureFileName, Is.EqualTo("hero.png"));
            Assert.That(document.Width, Is.EqualTo(200));
            Assert.That(document.Height, Is.EqualTo(300));
            Assert.That(
                document.DefaultFramesPerSecond,
                Is.EqualTo(FrameAtlasAsset.DefaultFramesPerSecond));
            Assert.That(document.Frames.Count, Is.EqualTo(1));
            Assert.That(document.Frames[0].ActionId, Is.EqualTo("walk"));
            Assert.That(document.Frames[0].FrameNumber, Is.EqualTo(1));
        }

        [Test]
        public void ParsesDefaultAndIndependentActionFramesPerSecond()
        {
            var document = FrameAtlasTpsheetParser.Parse(
                ":format=40300\n" +
                ":texture=hero.png\n" +
                ":size=200x300\n" +
                ":fps=9\n" +
                ":action-fps=up;7\n" +
                ":action-fps=down;15\n" +
                "up_001;1;1;30;40;0.5;0\n" +
                "down_001;40;1;30;40;0.5;0\n",
                "fps.tpsheet");

            Assert.That(document.DefaultFramesPerSecond, Is.EqualTo(9f));
            Assert.That(document.ActionFramesPerSecond.Count, Is.EqualTo(2));
            Assert.That(
                document.ActionFramesPerSecond[0].ActionId,
                Is.EqualTo("up"));
            Assert.That(
                document.ActionFramesPerSecond[0].FramesPerSecond,
                Is.EqualTo(7f));
            Assert.That(
                document.ActionFramesPerSecond[1].ActionId,
                Is.EqualTo("down"));
            Assert.That(
                document.ActionFramesPerSecond[1].FramesPerSecond,
                Is.EqualTo(15f));
        }

        [Test]
        public void RejectsInvalidOrDuplicateActionFramesPerSecond()
        {
            var exception = Assert.Throws<FrameAtlasTpsheetParseException>(
                delegate
                {
                    FrameAtlasTpsheetParser.Parse(
                        Header +
                        ":action-fps=walk;0\n" +
                        ":action-fps=walk;12\n" +
                        "walk_001;1;1;30;40;0.5;0\n",
                        "invalid-fps.tpsheet");
                });

            Assert.That(
                exception.Message,
                Does.Contain("invalid-fps.tpsheet(4)"));
            Assert.That(exception.Message, Does.Contain("正数FPS"));
        }

        [Test]
        public void ConvertsTopLeftCoordinatesAndPreservesPivot()
        {
            var document = ParseSingle(
                "down_001;10;20;30;40;0.25;0.75");
            var frame = document.Frames[0];
            var rect = frame.ToUnityRect(document.Height);

            Assert.That(rect.x, Is.EqualTo(10));
            Assert.That(rect.y, Is.EqualTo(240));
            Assert.That(rect.width, Is.EqualTo(30));
            Assert.That(rect.height, Is.EqualTo(40));
            Assert.That(frame.Pivot.x, Is.EqualTo(0.25f));
            Assert.That(frame.Pivot.y, Is.EqualTo(0.75f));
        }

        [Test]
        public void ParsesAndValidatesExplicitFootAnchor()
        {
            var document = FrameAtlasTpsheetParser.Parse(
                Header +
                "# anchor;down;x=15;y=30;space=output-pixels;" +
                "origin=top-left;pivot=0.5,0.25\n" +
                "down_001;1;1;30;40;0.5;0.25\n",
                "anchor.tpsheet");

            Assert.That(document.ActionAnchors.Count, Is.EqualTo(1));
            Assert.That(
                document.ActionAnchors[0].ActionId,
                Is.EqualTo("down"));
            Assert.That(
                document.ActionAnchors[0].PixelPosition,
                Is.EqualTo(new UnityEngine.Vector2(15f, 30f)));
            Assert.That(
                document.ActionAnchors[0].NormalizedPivot,
                Is.EqualTo(new UnityEngine.Vector2(0.5f, 0.25f)));
        }

        [Test]
        public void RejectsAnchorThatDisagreesWithFramePivot()
        {
            var exception = Assert.Throws<FrameAtlasTpsheetParseException>(
                delegate
                {
                    FrameAtlasTpsheetParser.Parse(
                        Header +
                        "# anchor;down;x=15;y=30;" +
                        "space=output-pixels;origin=top-left;" +
                        "pivot=0.5,0.25\n" +
                        "down_001;1;1;30;40;0.5;0\n",
                        "anchor-mismatch.tpsheet");
                });

            Assert.That(
                exception.Message,
                Does.Contain("脚底锚点、声明 Pivot 与帧 Pivot 不一致"));
        }

        [Test]
        public void AllowsTwoNamesToShareOneRectangle()
        {
            var document = FrameAtlasTpsheetParser.Parse(
                Header +
                "left_001;1;1;30;40;0.5;0\n" +
                "right_001;1;1;30;40;0.5;0\n",
                "aliases.tpsheet");

            Assert.That(document.Frames.Count, Is.EqualTo(2));
            Assert.That(document.Frames[0].Name, Is.EqualTo("left_001"));
            Assert.That(document.Frames[1].Name, Is.EqualTo("right_001"));
            Assert.That(
                document.Frames[0].ToUnityRect(document.Height),
                Is.EqualTo(
                    document.Frames[1].ToUnityRect(document.Height)));
        }

        [Test]
        public void ParsesMirrorComment()
        {
            var document = FrameAtlasTpsheetParser.Parse(
                Header +
                "# mirror;up-right;up-left;flipX=true\n" +
                "up_left_001;1;1;30;40;0.5;0\n",
                "mirror.tpsheet");

            Assert.That(document.MirrorMappings.Count, Is.EqualTo(1));
            Assert.That(
                document.MirrorMappings[0].TargetActionId,
                Is.EqualTo("up-right"));
            Assert.That(
                document.MirrorMappings[0].SourceActionId,
                Is.EqualTo("up-left"));
            Assert.That(document.MirrorMappings[0].FlipX, Is.True);
        }

        [Test]
        public void AcceptsCrLfLineEndings()
        {
            var document = FrameAtlasTpsheetParser.Parse(
                ":format=40300\r\n" +
                ":texture=hero.png\r\n" +
                ":size=200x300\r\n" +
                "\r\n" +
                "# ordinary comment\r\n" +
                "walk_001;1;2;30;40;0.5;0\r\n",
                "crlf.tpsheet");

            Assert.That(document.Frames.Count, Is.EqualTo(1));
        }

        [Test]
        public void RejectsWrongFieldCountWithFileAndLine()
        {
            var exception = Assert.Throws<FrameAtlasTpsheetParseException>(
                delegate
                {
                    FrameAtlasTpsheetParser.Parse(
                        Header + "walk_001;1;2;30\n",
                        "fields.tpsheet");
                });

            Assert.That(exception.Message, Does.Contain("fields.tpsheet(4)"));
            Assert.That(exception.Message, Does.Contain("7 个字段"));
        }

        [Test]
        public void RejectsOutOfBoundsRectangle()
        {
            var exception = Assert.Throws<FrameAtlasTpsheetParseException>(
                delegate
                {
                    FrameAtlasTpsheetParser.Parse(
                        Header + "walk_001;190;290;30;40;0.5;0\n",
                        "bounds.tpsheet");
                });

            Assert.That(exception.Message, Does.Contain("bounds.tpsheet(4)"));
            Assert.That(exception.Message, Does.Contain("矩形越界"));
        }

        [Test]
        public void RejectsDuplicateSpriteName()
        {
            var exception = Assert.Throws<FrameAtlasTpsheetParseException>(
                delegate
                {
                    FrameAtlasTpsheetParser.Parse(
                        Header +
                        "walk_001;1;1;30;40;0.5;0\n" +
                        "walk_001;40;1;30;40;0.5;0\n",
                        "duplicate.tpsheet");
                });

            Assert.That(
                exception.Message,
                Does.Contain("duplicate.tpsheet(5)"));
            Assert.That(exception.Message, Does.Contain("名称重复"));
        }

        private const string Header =
            ":format=40300\n" +
            ":texture=hero.png\n" +
            ":size=200x300\n";

        private static FrameAtlasTpsheetDocument ParseSingle(string frame)
        {
            return FrameAtlasTpsheetParser.Parse(
                Header + frame + "\n",
                "single.tpsheet");
        }
    }
}
