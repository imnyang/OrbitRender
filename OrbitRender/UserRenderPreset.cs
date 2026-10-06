using System;
using System.Collections.Generic;

namespace OrbitRender
{
    // Only portable export options belong here; machine paths and selected tiles
    // stay with the current installation and editor session.
    public sealed class UserRenderPreset
    {
        public string Name = "";
        public int Width = 1920;
        public int Height = 1080;
        public int Fps = 60;
        public int VideoFps = 60;
        public int BitrateMbps = 18;
        public float EndDelaySeconds = 2f;
        public bool CaptureAudio = true;
        public float AudioGainDb = 0f;
        public AudioCodec AudioCodec = AudioCodec.Auto;
        public bool ShowRenderPreview = true;
        public bool BgaMode = false;
        public bool ShowPlanetRings = true;
        public bool ShowSongTitle = true;
        public bool ShowCountdown = true;
        public bool ShowResultText = true;
        public bool ShowHitJudgments = false;
        public EncoderSpeed Encoding = EncoderSpeed.Quality;
        public VideoEncoder Encoder = VideoEncoder.Auto;
        public VideoCodec Codec = VideoCodec.H264;
        public VideoBitDepth BitDepth = VideoBitDepth.Eight;
        public VideoContainer Container = VideoContainer.Auto;
        public string FileNameFormat = OutputFormat.DefaultFileName;
        public bool OpenOutputFolder = true;

        internal static UserRenderPreset Capture(string name, RendererSettings settings)
        {
            var profile = RendererSettings.GetPresetProfile(settings.Preset);
            return new UserRenderPreset {
                Name = name,
                Width = settings.Preset == RendererPreset.Custom ? settings.Width : profile.Width,
                Height = settings.Preset == RendererPreset.Custom ? settings.Height : profile.Height,
                Fps = settings.Fps,
                VideoFps = settings.VideoFps,
                BitrateMbps = settings.Preset == RendererPreset.Custom ? settings.BitrateMbps : profile.BitrateMbps,
                EndDelaySeconds = settings.EndDelaySeconds,
                CaptureAudio = settings.CaptureAudio,
                AudioGainDb = settings.AudioGainDb,
                AudioCodec = settings.AudioCodec,
                ShowRenderPreview = settings.ShowRenderPreview,
                BgaMode = settings.BgaMode,
                ShowPlanetRings = settings.ShowPlanetRings,
                ShowSongTitle = settings.ShowSongTitle,
                ShowCountdown = settings.ShowCountdown,
                ShowResultText = settings.ShowResultText,
                ShowHitJudgments = settings.ShowHitJudgments,
                Encoding = settings.Encoding,
                Encoder = settings.Encoder,
                Codec = settings.Codec,
                BitDepth = settings.BitDepth,
                Container = settings.Container,
                FileNameFormat = settings.FileNameFormat,
                OpenOutputFolder = settings.OpenOutputFolder,
            };
        }

        internal void ApplyTo(RendererSettings settings)
        {
            settings.Preset = RendererPreset.Custom;
            settings.Width = Width;
            settings.Height = Height;
            settings.Fps = Fps;
            settings.VideoFps = VideoFps;
            settings.BitrateMbps = BitrateMbps;
            settings.EndDelaySeconds = EndDelaySeconds;
            settings.CaptureAudio = CaptureAudio;
            settings.AudioGainDb = AudioGainDb;
            settings.AudioCodec = AudioCodec;
            settings.ShowRenderPreview = ShowRenderPreview;
            settings.BgaMode = BgaMode;
            settings.ShowPlanetRings = ShowPlanetRings;
            settings.ShowSongTitle = ShowSongTitle;
            settings.ShowCountdown = ShowCountdown;
            settings.ShowResultText = ShowResultText;
            settings.ShowHitJudgments = ShowHitJudgments;
            settings.Encoding = Encoding;
            settings.Encoder = Encoder;
            settings.Codec = Codec;
            settings.BitDepth = BitDepth;
            settings.Container = Container;
            settings.FileNameFormat = FileNameFormat;
            settings.OpenOutputFolder = OpenOutputFolder;
            settings.OnChange();
        }
    }

    internal static class UserRenderPresets
    {
        internal static List<UserRenderPreset> Items(RendererSettings settings)
            => settings.UserPresets ?? (settings.UserPresets = new List<UserRenderPreset>());

        internal static string ValidateName(RendererSettings settings, string name, UserRenderPreset except = null)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 64 || ContainsControl(name))
                return "user-preset-invalid-name";
            if (Items(settings).Exists(p => p != null && p != except
                && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                return "user-preset-duplicate-name";
            return null;
        }

        private static bool ContainsControl(string name)
        {
            foreach (var c in name) if (char.IsControl(c)) return true;
            return false;
        }

        internal static string Save(RendererSettings settings, string name, RendererSettings source,
            UserRenderPreset replacement = null)
        {
            var error = ValidateName(settings, name, replacement);
            if (error != null) return error;
            var items = Items(settings);
            var preset = UserRenderPreset.Capture(name.Trim(), source);
            var index = replacement == null ? -1 : items.IndexOf(replacement);
            if (replacement != null && index < 0) return "user-preset-missing";
            if (index < 0) items.Add(preset); else items[index] = preset;
            return null;
        }

        internal static string Rename(RendererSettings settings, UserRenderPreset preset, string name)
        {
            if (preset == null || !Items(settings).Contains(preset)) return "user-preset-missing";
            var error = ValidateName(settings, name, preset);
            if (error != null) return error;
            preset.Name = name.Trim();
            return null;
        }
    }
}
