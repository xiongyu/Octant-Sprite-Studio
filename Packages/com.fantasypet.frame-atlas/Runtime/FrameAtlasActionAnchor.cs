using System;
using UnityEngine;

namespace FrameAtlas
{
    /// <summary>
    /// 一个动作在输出帧中的显式脚底锚点。
    /// </summary>
    [Serializable]
    public sealed class FrameAtlasActionAnchor
    {
        [SerializeField] private string actionId;
        [SerializeField] private Vector2 outputPixel;
        [SerializeField] private Vector2 normalizedPivot;

        internal FrameAtlasActionAnchor(
            string actionId,
            Vector2 outputPixel,
            Vector2 normalizedPivot)
        {
            this.actionId = actionId;
            this.outputPixel = outputPixel;
            this.normalizedPivot = normalizedPivot;
        }

        /// <summary>规范化后的动作 ID。</summary>
        public string ActionId
        {
            get { return actionId; }
        }

        /// <summary>以输出帧左上角为原点的脚底像素位置。</summary>
        public Vector2 OutputPixel
        {
            get { return outputPixel; }
        }

        /// <summary>以 Unity 左下角为原点的归一化 Sprite Pivot。</summary>
        public Vector2 NormalizedPivot
        {
            get { return normalizedPivot; }
        }
    }
}
