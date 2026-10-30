using OrbitRender;
using System;
using System.Diagnostics;
using System.Threading;
using OrbitRender.Renderer;
using SkyHook;
using UnityEngine;

namespace TUFReplayRenderer.Replay;

// Observe the common game input hub only. No mod handlers, fields or settings
// are accessed. Counters contain no key identities; callbacks allocate nothing.
internal sealed class CommonInputDiagnostics : MonoBehaviour
{
    private long pressed, released, previousPressed = -1, previousReleased = -1, nextCheck;
    private string previousState;
    private void OnEnable() => SkyHookManager.KeyUpdated.AddListener(Observe);
    private void OnDisable() => SkyHookManager.KeyUpdated.RemoveListener(Observe);
    private void Observe(SkyHookEvent input)
    {
        if (input.Type == SkyHook.EventType.KeyPressed) Interlocked.Increment(ref pressed);
        else if (input.Type == SkyHook.EventType.KeyReleased) Interlocked.Increment(ref released);
    }
    private void Update()
    {
        long now = Stopwatch.GetTimestamp();
        if (now < nextCheck) return;
        nextCheck = now + 10 * Stopwatch.Frequency;
        try
        {
            var manager = SkyHookManager.Instance;
            string state = "hookRunning=" + manager.isHookActive + ", hookFocused=" + SkyHookManager.IsFocused
                + ", requireFocus=" + manager.requireFocus + ", unityFocused=" + Application.isFocused
                + ", renderInputBlocked=" + RendererController.InputBlocked
                + ", replayInjection=" + SharedOverlayInput.Active;
            long downs = Interlocked.Read(ref pressed), ups = Interlocked.Read(ref released);
            if (state != previousState || downs != previousPressed || ups != previousReleased)
                Main.Entry.Logger.Log("[Input/Diagnostics] " + state + ", sharedDowns=" + downs + ", sharedUps=" + ups);
            previousState = state; previousPressed = downs; previousReleased = ups;
        }
        catch (Exception error)
        {
            string state = error.GetType().Name + ": " + error.Message;
            if (state != previousState) Main.Entry.Logger.Log("[Input/Diagnostics] Common input state unavailable: " + state);
            previousState = state;
        }
    }
}
