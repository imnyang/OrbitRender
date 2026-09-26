using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace OrbitRender.Renderer
{
    // Unity's VideoPlayer normally advances from DSP/game time. That is fine
    // during normal play, but it makes an accelerated export race the render
    // clock. Keep level background videos on the same deterministic timeline
    // as the song while a render owns the frame.
    internal sealed class BackgroundVideoClock : IDisposable
    {
        private sealed class VideoState
        {
            internal VideoPlayer Player;
            internal VideoTimeReference TimeReference;
            internal VideoTimeUpdateMode TimeUpdateMode;
            internal double ExternalReferenceTime;
            internal double Time;
            internal float PlaybackSpeed;
            internal bool SkipOnDrop;
            internal bool WasPlaying;
            internal bool WasPaused;
            internal bool InitialTimeApplied;
        }

        private readonly List<VideoState> videos = new List<VideoState>();
        private bool disposed;
        private bool warnedAboutFailure;

        private BackgroundVideoClock() { }

        internal static BackgroundVideoClock Capture()
        {
            var clock = new BackgroundVideoClock();
            var level = ADOBase.customLevel;
            if (level != null) clock.Add(level.videoBG);

            var vfx = scrVfxPlus.instance;
            if (vfx != null) clock.Add(vfx.videoBG);

            if (clock.videos.Count == 0) return null;

            Main.Entry.Logger.Log("Deterministic background video clock enabled: players=" + clock.videos.Count);
            return clock;
        }

        private void Add(VideoPlayer player)
        {
            if (player == null) return;
            for (var i = 0; i < videos.Count; i++)
                if (videos[i].Player == player) return;

            videos.Add(new VideoState {
                Player = player,
                TimeReference = player.timeReference,
                TimeUpdateMode = player.timeUpdateMode,
                ExternalReferenceTime = player.externalReferenceTime,
                Time = player.time,
                PlaybackSpeed = player.playbackSpeed,
                SkipOnDrop = player.skipOnDrop,
                WasPlaying = player.isPlaying,
                WasPaused = player.isPaused
            });
        }

        internal void Apply()
        {
            if (disposed || !RendererController.ControlsTime) return;

            var targetTime = ResolveTargetTime();
            for (var i = 0; i < videos.Count; i++)
            {
                var state = videos[i];
                var player = state.Player;
                if (player == null) continue;

                try
                {
                    // ExternalTime prevents the native player from advancing
                    // from wall-clock DSP time between deterministic frames.
                    player.timeReference = VideoTimeReference.ExternalTime;
                    player.externalReferenceTime = targetTime;
                    if (player.canSetPlaybackSpeed) player.playbackSpeed = 1f;
                    if (player.canSetSkipOnDrop) player.skipOnDrop = false;

                    // The first assignment avoids retaining a stale frame when
                    // the level's video was prepared before render ownership.
                    if (!state.InitialTimeApplied && player.canSetTime)
                    {
                        player.time = targetTime;
                        state.InitialTimeApplied = true;
                    }

                    // scrVfxPlus normally starts the player. This fallback is
                    // needed for levels whose background has no active VFX
                    // entry but still owns a prepared level video.
                    if (!player.isPlaying && player.isPrepared
                        && player.gameObject != null && player.gameObject.activeInHierarchy)
                        player.Play();
                }
                catch (Exception ex)
                {
                    if (warnedAboutFailure) continue;
                    warnedAboutFailure = true;
                    Main.Entry.Logger.Log("Could not control background video clock: " + ex.Message);
                }
            }
        }

        private static double ResolveTargetTime()
        {
            var conductor = ADOBase.conductor;
            var vfx = scrVfxPlus.instance;
            if (conductor != null)
            {
                var time = conductor.songposition_minusi + (vfx != null ? vfx.vidOffset : 0f);
                if (!double.IsNaN(time) && !double.IsInfinity(time)) return Math.Max(0.0, time);
            }

            var renderer = RendererController.Instance;
            return renderer != null ? Math.Max(0.0, renderer.Clock.Time) : 0.0;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            for (var i = 0; i < videos.Count; i++)
            {
                var state = videos[i];
                var player = state.Player;
                if (player == null) continue;

                try
                {
                    if (player.isPlaying || player.isPaused) player.Stop();
                    player.timeReference = state.TimeReference;
                    if (player.canSetTimeUpdateMode) player.timeUpdateMode = state.TimeUpdateMode;
                    player.externalReferenceTime = state.ExternalReferenceTime;
                    if (player.canSetPlaybackSpeed) player.playbackSpeed = state.PlaybackSpeed;
                    if (player.canSetSkipOnDrop) player.skipOnDrop = state.SkipOnDrop;
                    if (player.canSetTime) player.time = state.Time;
                    if (state.WasPlaying) player.Play();
                    else if (state.WasPaused) player.Pause();
                }
                catch (Exception ex)
                {
                    Main.Entry.Logger.Log("Could not restore background video state: " + ex.Message);
                }
            }

            videos.Clear();
        }
    }
}
