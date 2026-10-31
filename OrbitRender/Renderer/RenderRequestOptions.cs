namespace OrbitRender.Renderer
{
    // Per-render values selected from the editor's Export Video dialog.
    // Nullable fields deliberately fall back to the normal UMM settings.
    public sealed class RenderRequestOptions
    {
        internal RenderRequestOptions Snapshot() => (RenderRequestOptions)MemberwiseClone();
        public string ReplayManifestPath;
        public string OutputDirectory;
        public RendererPreset? Preset;
        public int? Width;
        public int? Height;
        public int? TargetFps;
        public int? VideoFps;
        public int? BitrateMbps;
        public float? EndDelaySeconds;
        public bool? CaptureAudio;
        public float? AudioGainDb;
        public AudioCodec? AudioCodec;
        public bool? ShowRenderPreview;
        public bool? UseRustRenderer;
        public FrameBufferPolicy? BufferPolicy;
        public bool? BgaMode;
        public bool? ShowPlanetRings;
        public bool? ShowSongTitle;
        public bool? ShowCountdown;
        public bool? ShowResultText;
        public bool? ShowHitJudgments;
        public bool? ShowModOverlay;
        public bool? ShowAllGameplayHud;
        public EncoderSpeed? Encoding;
        public VideoEncoder? Encoder;
        public VideoContainer? Container;
        public string FileNameFormat;
        public VideoCodec? VideoCodec;
        public VideoBitDepth? BitDepth;
        public bool? OpenOutputFolder;
        public int? SelectionStartTile;
        public int? SelectionEndTile;
    }
}
