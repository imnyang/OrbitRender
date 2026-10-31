using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRender.Renderer
{
    // Keeps the game's text lifecycle running while selectively preventing
    // disabled categories from reaching captured frames. This lets countdown
    // and result logic complete normally and restores the editor UI afterward.
    internal sealed class DefaultTextRenderState : IDisposable
    {
        private sealed class TextSnapshot
        {
            internal Behaviour Text;
            internal bool Enabled;
            internal bool VisibleDuringRender;
        }

        private readonly List<TextSnapshot> controlledTexts = new List<TextSnapshot>();
        private readonly HashSet<Behaviour> seen = new HashSet<Behaviour>();
        private readonly HashSet<Behaviour> visibleTexts = new HashSet<Behaviour>();
        private readonly List<Canvas> captureCanvases = new List<Canvas>();
        private GameObject replayModifiers, replayNoFail;
        private bool modifiersActive, noFailActive;
        private Image replayDifficulty;
        private Sprite difficultySprite;
        private bool disposed;

        internal IEnumerable<Canvas> CaptureCanvases => captureCanvases;
        private DefaultTextRenderState() { }

        internal static DefaultTextRenderState Capture(bool showSongTitle, bool showCountdown,
            bool showResultText, bool showHitJudgments, bool showGameplayHud = false)
        {
            var state = new DefaultTextRenderState();
            var controller = ADOBase.controller;
            var ui = scrUIController.instance;

            if (ui != null && (showGameplayHud || showSongTitle || showCountdown || showResultText || showHitJudgments))
                state.AddCaptureCanvas(ui.canvas);

            if (showGameplayHud)
            {
                // Editor playback uses its own difficulty, no-fail and auto
                // indicators rather than the normal gameplay modifier UI.
                var editor = scnEditor.instance;
                if (editor != null)
                {
                    state.AddCaptureCanvas(editor.editorDifficultySelector);
                    state.AddCaptureCanvas(editor.buttonNoFail);
                    state.AddCaptureCanvas(editor.buttonUnlockKeyLimiter);
                    state.AddCaptureCanvas(editor.autoImage);
                }
                // The normal gameplay modifier strip has a different layout.
                // Do not force it on during editor playback, where the editor
                // already supplies the indicators in their usual position.
                if (editor == null && ui != null && ui.modifiersContainer != null)
                {
                    state.replayModifiers = ui.modifiersContainer.gameObject;
                    state.modifiersActive = state.replayModifiers.activeSelf;
                    state.replayModifiers.SetActive(true);
                    if (ui.noFailImage != null)
                    {
                        state.replayNoFail = ui.noFailImage.gameObject;
                        state.noFailActive = state.replayNoFail.activeSelf;
                    }
                    state.replayDifficulty = ui.difficultyImage;
                    if (state.replayDifficulty != null)
                    {
                        state.difficultySprite = state.replayDifficulty.sprite;
                        var sprites = RDConstants.data.bullseyeSprites;
                        int difficulty = (int)GCS.difficulty;
                        if (sprites != null && difficulty >= 0 && difficulty < sprites.Length)
                            state.replayDifficulty.sprite = sprites[difficulty];
                    }
                    state.AddCaptureCanvas(ui.modifiersContainer);
                }
                foreach (var item in UnityEngine.Object.FindObjectsByType<scrHUDText>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    state.AddCaptureCanvas(item);
                foreach (var item in UnityEngine.Object.FindObjectsByType<scrShowIfDebug>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    state.AddCaptureCanvas(item);
                foreach (var item in UnityEngine.Object.FindObjectsByType<scrVersionText>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    state.AddCaptureCanvas(item);
                var hitManager = ADOBase.playerManager != null ? ADOBase.playerManager.hitTextManager : null;
                if (hitManager != null && hitManager.hitTextContainer != null)
                    state.AddCaptureCanvas(hitManager.hitTextContainer.GetComponentInParent<Canvas>());
                return state;
            }

            if (showSongTitle)
            {
                state.Allow(controller != null ? controller.txtLevelName : null);
                state.Allow(ui != null ? ui.txtLevelName : null);
            }

            if (showCountdown)
                state.Allow(ui != null ? ui.txtCountdown : null);

            if (showResultText)
            {
                state.Allow(controller != null ? controller.txtCongrats : null);
                state.Allow(controller != null ? controller.txtAprilCongrats : null);
                state.Allow(ui != null ? ui.txtCongrats : null);
                state.Allow(ui != null ? ui.txtAprilCongrats : null);
            }

            // The gameplay HUD shares one screen-space canvas with pause,
            // autoplay, modifier, and editor controls. Preserve only the text
            // categories explicitly selected for the exported frame.
            var hitTextContainer = showHitJudgments && ADOBase.playerManager != null
                && ADOBase.playerManager.hitTextManager != null
                ? ADOBase.playerManager.hitTextManager.hitTextContainer : null;
            if (hitTextContainer != null)
                state.AddCaptureCanvas(hitTextContainer.GetComponentInParent<Canvas>());
            for (var i = 0; i < state.captureCanvases.Count; i++)
                foreach (var graphic in state.captureCanvases[i].GetComponentsInChildren<Graphic>(true))
                    if (!state.visibleTexts.Contains(graphic)
                        && (hitTextContainer == null
                            || (graphic.transform != hitTextContainer.transform
                                && !graphic.transform.IsChildOf(hitTextContainer.transform))))
                        state.Hide(graphic);

            state.Apply();
            return state;
        }

        private void AddCaptureCanvas(Component component)
        {
            if (component == null) return;
            AddCaptureCanvas(component.GetComponentInParent<Canvas>());
        }

        private void AddCaptureCanvas(Canvas canvas)
        {
            if (canvas == null) return;
            var root = canvas.rootCanvas;
            if (root != null && !captureCanvases.Contains(root)) captureCanvases.Add(root);
        }

        private void Allow(Behaviour text)
        {
            if (text == null || !visibleTexts.Add(text)) return;
            AddCaptureCanvas(text);
            Add(text, true);
        }

        private void Hide(Behaviour text)
        {
            Add(text, false);
        }

        private void Add(Behaviour text, bool visibleDuringRender)
        {
            if (text == null || !seen.Add(text)) return;
            controlledTexts.Add(new TextSnapshot {
                Text = text,
                Enabled = text.enabled,
                VisibleDuringRender = visibleDuringRender
            });
        }

        internal void Apply()
        {
            if (disposed) return;
            if (replayModifiers != null && !replayModifiers.activeSelf) replayModifiers.SetActive(true);
            if (replayNoFail != null && ADOBase.controller != null
                && replayNoFail.activeSelf != ADOBase.controller.noFail)
                replayNoFail.SetActive(ADOBase.controller.noFail);
            for (var i = 0; i < controlledTexts.Count; i++)
            {
                var snapshot = controlledTexts[i];
                var text = snapshot.Text;
                if (text != null && text.enabled != snapshot.VisibleDuringRender)
                    text.enabled = snapshot.VisibleDuringRender;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (replayModifiers != null) replayModifiers.SetActive(modifiersActive);
            if (replayNoFail != null) replayNoFail.SetActive(noFailActive);
            if (replayDifficulty != null) replayDifficulty.sprite = difficultySprite;
            for (var i = 0; i < controlledTexts.Count; i++)
            {
                var snapshot = controlledTexts[i];
                if (snapshot.Text != null) snapshot.Text.enabled = snapshot.Enabled;
            }
            controlledTexts.Clear();
            seen.Clear();
            visibleTexts.Clear();
            captureCanvases.Clear();
        }
    }
}
