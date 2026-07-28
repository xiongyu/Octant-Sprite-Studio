using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FrameAtlas.Editor
{
    /// <summary>
    /// `.tpsheet` 导入设置面板，支持 PPU、采样、寻址、mipmap 和压缩。
    /// </summary>
    [CustomEditor(typeof(FrameAtlasTpsheetImporter))]
    [CanEditMultipleObjects]
    public sealed class FrameAtlasTpsheetImporterEditor :
        ScriptedImporterEditor
    {
        private SerializedProperty pixelsPerUnit;
        private SerializedProperty filterMode;
        private SerializedProperty wrapMode;
        private SerializedProperty mipmapEnabled;
        private SerializedProperty textureCompression;

        /// <summary>绑定导入器的序列化设置。</summary>
        public override void OnEnable()
        {
            base.OnEnable();
            pixelsPerUnit =
                serializedObject.FindProperty("pixelsPerUnit");
            filterMode = serializedObject.FindProperty("filterMode");
            wrapMode = serializedObject.FindProperty("wrapMode");
            mipmapEnabled =
                serializedObject.FindProperty("mipmapEnabled");
            textureCompression =
                serializedObject.FindProperty("textureCompression");
        }

        /// <summary>绘制自定义导入器 Inspector。</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(
                pixelsPerUnit,
                new GUIContent("Pixels Per Unit"));
            EditorGUILayout.PropertyField(
                filterMode,
                new GUIContent("Filter Mode"));
            EditorGUILayout.PropertyField(
                wrapMode,
                new GUIContent("Wrap Mode"));
            EditorGUILayout.PropertyField(
                mipmapEnabled,
                new GUIContent("Generate Mip Maps"));
            EditorGUILayout.PropertyField(
                textureCompression,
                new GUIContent("Compression"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.HelpBox(
                "Apply 会先保存 .tpsheet 设置，再重新导入同目录 PNG；" +
                "PNG 完成后依赖系统会自动重导入 FrameAtlas，过程不会形成循环。",
                MessageType.Info);
            ApplyRevertGUI();
        }

        /// <summary>
        /// 保存设置后延迟重导入源 PNG，使 TextureImporter 使用同一组设置。
        /// </summary>
        public override void SaveChanges()
        {
            var sheetPaths = new List<string>();
            for (var i = 0; i < targets.Length; i++)
            {
                var importer =
                    targets[i] as FrameAtlasTpsheetImporter;
                if (importer != null)
                {
                    sheetPaths.Add(importer.assetPath);
                }
            }

            base.SaveChanges();
            EditorApplication.delayCall += delegate
            {
                var textures = new HashSet<string>();
                for (var i = 0; i < sheetPaths.Count; i++)
                {
                    string texturePath;
                    if (FrameAtlasTpsheetImporter.TryGetTextureAssetPath(
                            sheetPaths[i],
                            out texturePath))
                    {
                        textures.Add(texturePath);
                    }
                }

                foreach (var texturePath in textures)
                {
                    AssetDatabase.ImportAsset(
                        texturePath,
                        ImportAssetOptions.ForceUpdate);
                }
            };
        }
    }
}
