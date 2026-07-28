using System;
using UnityEngine;

namespace FrameAtlas
{
    /// <summary>
    /// 图集中一张可播放的 Sprite 帧及其原始导入信息。
    /// </summary>
    [Serializable]
    public sealed class FrameAtlasFrame
    {
        [SerializeField] private string name;
        [SerializeField] private string actionId;
        [SerializeField] private int frameNumber;
        [SerializeField] private Sprite sprite;
        [SerializeField] private Rect atlasRect;
        [SerializeField] private Vector2 normalizedPivot;

        internal FrameAtlasFrame(
            string name,
            string actionId,
            int frameNumber,
            Sprite sprite,
            Rect atlasRect,
            Vector2 normalizedPivot)
        {
            this.name = name;
            this.actionId = actionId;
            this.frameNumber = frameNumber;
            this.sprite = sprite;
            this.atlasRect = atlasRect;
            this.normalizedPivot = normalizedPivot;
        }

        /// <summary>配置文件中的完整帧名称。</summary>
        public string Name
        {
            get { return name; }
        }

        /// <summary>从帧名称中提取并规范化后的动作 ID。</summary>
        public string ActionId
        {
            get { return actionId; }
        }

        /// <summary>从名称末尾提取的数字帧号。</summary>
        public int FrameNumber
        {
            get { return frameNumber; }
        }

        /// <summary>导入器创建的 Sprite 子资源。</summary>
        public Sprite Sprite
        {
            get { return sprite; }
        }

        /// <summary>已经转换为 Unity 左下角坐标系的图集矩形。</summary>
        public Rect AtlasRect
        {
            get { return atlasRect; }
        }

        /// <summary>0～1 范围内的归一化轴心。</summary>
        public Vector2 NormalizedPivot
        {
            get { return normalizedPivot; }
        }
    }
}
