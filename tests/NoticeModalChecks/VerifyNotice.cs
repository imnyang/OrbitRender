using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OrbitRender;
using OrbitRender.UI;
using OrbitRender.Renderer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public static class VerifyNotice {
    static GameObject Root => GameObject.Find("OrbitRender.Notice");
    static Button Button(string key) => Root.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == Localization.Get(key));
    static void Assert(bool value, string message) { if(!value) throw new Exception(message); }
    public static void Run() {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.update += AwaitPlay;
        EditorApplication.EnterPlaymode();
    }
    static void AwaitPlay() {
        if(!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.update -= AwaitPlay;
        RunChecks();
    }
    static void RunChecks() {
        try {
            typeof(UguiFactory).GetField("font", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Font.CreateDynamicFontFromOSFont("Malgun Gothic", 14));
            foreach(var language in new[]{"ko", "en", "es"}) {
                Localization.Language = language;
                ConflictRecovery.Mods = new[]{ new UnityModManagerNet.UnityModManager.ModEntry { Info = new UnityModManagerNet.UnityModManager.Info {
                    Id="OrbitRender.DiagnosticConflict", DisplayName="OrbitRender Diagnostic Conflict TEST ONLY", Version="0.0.1" } } };
                NoticeModal.Open("PlanetRenderer.LateUpdate: expected UnityEngine.Time.get_unscaledTime() was not found", true);
                Layout(); Screenshot(language);
                Button("copy-error-diagnostics").onClick.Invoke();
                Assert(GUIUtility.systemCopyBuffer == RendererController.Instance.DiagnosticReport, "Copy failed");
                ConflictRecovery.FailDisable = true;
                Button("conflict-disable-next-launch").onClick.Invoke();
                Assert(Root.GetComponentsInChildren<Text>().Any(t => t.text.Contains("fixture save denied")), "Disable error missing");
                ConflictRecovery.FailDisable = false;
                Button("conflict-disable-next-launch").onClick.Invoke();
                Assert(!Button("conflict-disabled-next-launch").interactable, "Disable feedback missing");
                var before = ConflictRecovery.Restarted;
                Button("conflict-restart-game").onClick.Invoke();
                Assert(ConflictRecovery.Restarted == before, "Restart lacked confirmation");
                ConflictRecovery.FailRestart = true;
                Button("conflict-restart-confirm").onClick.Invoke();
                Assert(Root.GetComponentsInChildren<Text>().Any(t => t.text.Contains("fixture launch denied")), "Restart error missing");
                ConflictRecovery.FailRestart = false;
                Button("conflict-restart-game").onClick.Invoke();
                Button("conflict-restart-confirm").onClick.Invoke();
                Assert(ConflictRecovery.Restarted == before+1, "Restart callback missing");
                Button("notice-confirm").onClick.Invoke();
                Assert(!NoticeModal.IsOpen, "Close failed");
            }
            ConflictRecovery.Mods = Enumerable.Range(0, 12).Select(i => new UnityModManagerNet.UnityModManager.ModEntry {
                Info = new UnityModManagerNet.UnityModManager.Info { Id="fixture."+i, DisplayName="Long candidate list fixture "+i, Version="0.0.1" }
            }).ToArray();
            NoticeModal.Open("Long candidate list fixture", true); Layout();
            var scroll=Root.GetComponentInChildren<ScrollRect>();
            Assert(scroll.content.rect.height > scroll.viewport.rect.height, "Candidate list does not scroll");
            var last=scroll.content.GetComponentsInChildren<Button>().Last(); last.Select();
            Canvas.ForceUpdateCanvases();
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,last.transform);
            Assert(bounds.min.y >= scroll.viewport.rect.yMin-1 && bounds.max.y <= scroll.viewport.rect.yMax+1, "Focused candidate not visible");
            Assert(last.GetComponent<Outline>().enabled, "Keyboard focus indicator missing");
            NoticeModal.Close();
            ConflictRecovery.Mods = new UnityModManagerNet.UnityModManager.ModEntry[0];
            ConflictRecovery.CanRestart = false;
            NoticeModal.Open("No attributable mod fixture", true); Layout();
            Assert(!Button("conflict-restart-manually").interactable, "Unsupported restart enabled");
            Assert(Root.GetComponentsInChildren<Text>().Any(t => t.text == Localization.Get("conflict-no-candidate")), "No-candidate explanation missing");
            NoticeModal.Close();
            RendererController.Instance.HasTimingCompatibilityFailure = false;
            bool retried=false;
            NoticeModal.Open("Recovery fixture", true, () => retried=true); Layout();
            Button("recovery-retry").onClick.Invoke(); Assert(retried, "Recovery retry regressed");
            NoticeModal.Open("Ordinary notice"); Layout(); Button("notice-confirm").onClick.Invoke();
            Debug.Log("NOTICE CHECK PASS: Korean/English/Spanish layout, copy, disable success/failure, restart confirmation/success/failure, long-list scrolling, keyboard focus visibility, no candidates, unsupported platform, retry, close.");
            EditorApplication.Exit(0);
        } catch(Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    static void Layout() {
        Canvas.ForceUpdateCanvases();
        var content=Root.transform.Find("Panel/Message/Viewport/Content").GetComponent<RectTransform>();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content); Canvas.ForceUpdateCanvases();
        var panel=Root.transform.Find("Panel").GetComponent<RectTransform>();
        foreach(var b in Root.GetComponentsInChildren<Button>()) {
            var rect=b.GetComponent<RectTransform>();
            if(rect.parent==panel) Assert(rect.rect.height>=36, "Footer too small");
        }
    }
    static void Screenshot(string name) {
        var cameraObject=new GameObject("Fixture camera");
        var camera=cameraObject.AddComponent<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.09f,.09f,.09f);
        var target=new RenderTexture(Screen.width,Screen.height,24); camera.targetTexture=target;
        var canvas=Root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
        Canvas.ForceUpdateCanvases(); camera.Render();
        RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
        File.WriteAllBytes(Path.Combine(Application.dataPath,"../notice-"+name+".png"),image.EncodeToPNG());
        RenderTexture.active=null; canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
    }
}
