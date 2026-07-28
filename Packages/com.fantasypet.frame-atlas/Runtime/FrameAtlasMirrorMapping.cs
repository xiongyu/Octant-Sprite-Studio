using System;
using UnityEngine;

namespace FrameAtlas
{
    /// <summary>
    /// 描述一个镜像动作如何复用来源动作以及是否需要水平翻转。
    /// </summary>
    [Serializable]
    public sealed class FrameAtlasMirrorMapping
    {
        [SerializeField] private string targetActionId;
        [SerializeField] private string sourceActionId;
        [SerializeField] private bool flipX;

        internal FrameAtlasMirrorMapping(
            string targetActionId,
            string sourceActionId,
            bool flipX)
        {
            this.targetActionId = targetActionId;
            this.sourceActionId = sourceActionId;
            this.flipX = flipX;
        }

        /// <summary>播放时由外部传入的目标动作 ID。</summary>
        public string TargetActionId
        {
            get { return targetActionId; }
        }

        /// <summary>真正提供帧数据的来源动作 ID。</summary>
        public string SourceActionId
        {
            get { return sourceActionId; }
        }

        /// <summary>播放目标动作时是否设置 SpriteRenderer.flipX。</summary>
        public bool FlipX
        {
            get { return flipX; }
        }
    }
}
