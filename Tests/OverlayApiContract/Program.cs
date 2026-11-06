using System;
using System.Collections.Generic;
using OrbitRender;
using UnityEngine;

var modern = new Modern();
var legacy = new Legacy();
OverlayApi.Register("modern", modern);
OverlayApi.Register("legacy", legacy);
OverlayApi.DispatchReplayInput(KeyCode.RightAlt, 21, true, 100);
Check(modern.Calls == 0 && legacy.Calls == 0, "Input outside a session");
OverlayApi.BeginSession(false, 1, 1, 60);
OverlayApi.DispatchReplayInput(KeyCode.RightAlt, 21, true, 100);
Check(modern.Calls == 0, "Input in a non-replay session");
OverlayApi.EndSession();
OverlayApi.BeginSession(true, 1, 1, 60);
OverlayApi.DispatchReplayInput(KeyCode.RightAlt, 21, true, 100);
Check(modern.Calls == 1 && modern.Native == 21 && modern.Key == KeyCode.RightAlt && modern.Down && modern.Ticks == 100, "Typed input payload");
Check(modern.LegacyCalls == 0 && legacy.Calls == 1 && legacy.Key == "RightAlt", "Legacy fallback without duplicate dispatch");
OverlayApi.DispatchReplayInput(KeyCode.None, null, true, 101);
Check(modern.Calls == 1, "Missing key ignored");
OverlayApi.DispatchReplayInput(KeyCode.None, 21, false, 102);
Check(modern.Calls == 2 && modern.Native == 21 && !modern.Down, "Native-only release");
OverlayApi.Register("throwing", new Throwing());
var following = new Legacy();
OverlayApi.Register("following", following);
OverlayApi.DispatchReplayInput(KeyCode.RightAlt, null, true, 103);
Check(modern.Calls == 3 && modern.Native == null, "Unknown native code remains null");
Check(following.Calls == 1, "Provider failure does not block later providers");
OverlayApi.EndSession();
OverlayApi.DispatchReplayInput(KeyCode.RightAlt, null, true, 104);
Check(modern.Calls == 3, "No input after end");
Console.WriteLine("Overlay API contract checks passed.");
var stateful = new Stateful();
OverlayApi.Register("stateful", stateful);
const string recordedState = "{\"Attempt\":12,\"KeyTotal\":345}";
OverlayApi.BeginSession(true, 1, 1, 60,
    new Dictionary<string, string> { ["stateful"] = recordedState });
Check(stateful.Json == recordedState && stateful.Calls == 1 && stateful.BeginBeforeState,
    "Recorded state follows begin and precedes canvas enumeration");
OverlayApi.EndSession();
OverlayApi.BeginSession(true, 1, 1, 60);
Check(stateful.Json == null && stateful.Calls == 2, "Older bundles receive null instead of previous session state");
OverlayApi.EndSession();
OverlayApi.BeginSession(false, 1, 1, 60);
Check(stateful.Calls == 2, "Normal rendering never receives replay state");
OverlayApi.EndSession();
OverlayApi.Register("bad-state", new BadState());
OverlayApi.BeginSession(true, 1, 1, 60);
Check(stateful.Calls == 3, "State callback errors are isolated");
OverlayApi.EndSession();
Console.WriteLine("Recorded overlay state contract checks passed.");
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

public class Modern {
    public int Calls, LegacyCalls; public KeyCode Key; public int? Native; public bool Down; public long Ticks;
    public void OnReplayKey(KeyCode key, int? native, bool down, long ticks) { Calls++; Key = key; Native = native; Down = down; Ticks = ticks; }
    public void OnReplayInput(string key, bool down, long ticks) { LegacyCalls++; }
}
public class Legacy {
    public int Calls; public string Key;
    public void OnReplayInput(string key, bool down, long ticks) { Calls++; Key = key; }
}
public class Throwing {
    public void OnReplayKey(KeyCode key, int? native, bool down, long ticks) { throw new Exception("Provider error"); }
}
public class Stateful {
    public string Json; public int Calls; public bool BeginBeforeState;
    private bool begun, applied;
    public void OnRenderSessionBegin(bool replay) { begun = true; applied = false; }
    public void OnReplayState(string json) { BeginBeforeState = begun; Json = json; Calls++; applied = true; }
    public Canvas[] GetOverlayCanvases() {
        if (OverlayApi.IsReplay && !applied) throw new Exception("Canvases read before state");
        return Array.Empty<Canvas>();
    }
}
public class BadState {
    public void OnReplayState(string json) { throw new Exception("Invalid provider state"); }
}
namespace UnityEngine {
    public enum KeyCode { None, RightAlt }
    public class Canvas { public Canvas rootCanvas => this; }
}
namespace OrbitRender {
    public static class Main { public static ModEntry Entry; }
    public class ModEntry { public Logger Logger; }
    public class Logger { public void Error(string message) { } }
}
