using UnityEngine;

namespace FrameAtlas
{
    /// <summary>
    /// FrameAtlasAsset 的完整运行时播放示例，按 FPS 播放并自动应用 flipX。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class FrameAtlasPlayer : MonoBehaviour
    {
        [SerializeField] private FrameAtlasAsset atlas;
        [SerializeField] private SpriteRenderer targetRenderer;
        [SerializeField] private string actionId = "down";
        [SerializeField] private bool loop = true;
        [SerializeField] private bool playOnEnable = true;

        private FrameAtlasFrame[] currentFrames = new FrameAtlasFrame[0];
        private float currentFramesPerSecond =
            FrameAtlasAsset.DefaultFramesPerSecond;
        private float elapsed;
        private int displayedFrame = -1;
        private bool playing;

        /// <summary>当前使用的图集资源。</summary>
        public FrameAtlasAsset Atlas
        {
            get { return atlas; }
            set
            {
                atlas = value;
                Play(actionId);
            }
        }

        /// <summary>当前播放的动作 ID。</summary>
        public string ActionId
        {
            get { return actionId; }
        }

        /// <summary>动画每秒播放的帧数。</summary>
        public float FramesPerSecond
        {
            get { return currentFramesPerSecond; }
        }

        /// <summary>动画是否循环播放。</summary>
        public bool Loop
        {
            get { return loop; }
            set { loop = value; }
        }

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }
        }

        private void OnEnable()
        {
            if (playOnEnable)
            {
                Play(actionId);
            }
        }

        private void Update()
        {
            if (!playing || currentFrames.Length == 0)
            {
                return;
            }

            elapsed += Time.deltaTime;
            var rawIndex = Mathf.FloorToInt(
                elapsed * Mathf.Max(0.01f, currentFramesPerSecond));
            var frameIndex = loop
                ? rawIndex % currentFrames.Length
                : Mathf.Min(rawIndex, currentFrames.Length - 1);
            ApplyFrame(frameIndex);

            if (!loop && rawIndex >= currentFrames.Length - 1)
            {
                playing = false;
            }
        }

        /// <summary>从第一帧开始播放指定动作。</summary>
        public void Play(string nextActionId)
        {
            actionId = nextActionId;
            elapsed = 0f;
            displayedFrame = -1;
            currentFrames = atlas == null
                ? new FrameAtlasFrame[0]
                : atlas.GetFrames(actionId);
            currentFramesPerSecond = atlas == null
                ? FrameAtlasAsset.DefaultFramesPerSecond
                : atlas.GetFramesPerSecond(actionId);
            playing = currentFrames.Length > 0;

            if (targetRenderer != null)
            {
                targetRenderer.flipX =
                    atlas != null && atlas.IsMirrored(actionId);
            }

            if (playing)
            {
                ApplyFrame(0);
            }
        }

        /// <summary>暂停播放并保留当前显示帧。</summary>
        public void Pause()
        {
            playing = false;
        }

        /// <summary>从当前帧继续播放。</summary>
        public void Resume()
        {
            playing = currentFrames.Length > 0;
        }

        private void ApplyFrame(int frameIndex)
        {
            if (targetRenderer == null ||
                frameIndex < 0 ||
                frameIndex >= currentFrames.Length ||
                frameIndex == displayedFrame)
            {
                return;
            }

            targetRenderer.sprite = currentFrames[frameIndex].Sprite;
            targetRenderer.flipX =
                atlas != null && atlas.IsMirrored(actionId);
            displayedFrame = frameIndex;
        }
    }
}
