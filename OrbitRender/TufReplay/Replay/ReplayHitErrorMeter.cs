using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TUFReplayRenderer.Replay;

// The editor does not create/drive the gameplay meter. Use the game's own prefab
// and AddHit implementation without changing controller.gameworld or score state.
internal static class ReplayHitErrorMeter
{
    private static readonly Harmony Patches = new("KGH1113.TUFReplayRenderer.HitErrorMeter");
    private static RecordedReplayDriver owner;
    private static scrController controller;
    private static scrHitErrorMeter original;
    private static scrHitErrorMeter meter;
    private static GameObject meterObject;
    private static bool originalActive;

    internal static void Activate(RecordedReplayDriver driver)
    {
        if (owner != null) throw new InvalidOperationException("The hit error meter already belongs to another render.");
        owner = driver;
        controller = ADOBase.controller;
        if (controller == null) { Deactivate(driver); throw new InvalidOperationException("The game controller is unavailable for the hit error meter."); }
        original = controller.errorMeter;
        originalActive = original != null && original.gameObject.activeSelf;
        try {
            if (Persistence.hitErrorMeterSize != ErrorMeterSize.Off) {
                if (ADOBase.gc == null || ADOBase.gc.errorMeterPrefab == null)
                    throw new InvalidOperationException("The game's hit error meter prefab is unavailable. Reload the original level and try again.");
                meterObject = UnityEngine.Object.Instantiate(ADOBase.gc.errorMeterPrefab);
                meter = meterObject.GetComponent<scrHitErrorMeter>();
                if (meter == null) throw new InvalidOperationException("The game's hit error meter prefab has no meter component.");
                meter.name = "RecordedHitErrorMeter";
                meter.UpdateLayout(Persistence.hitErrorMeterSize, Persistence.hitErrorMeterShape);
                meter.gameObject.SetActive(true); // Include its Canvas in capture before countdown begins.
                if (original != null) original.gameObject.SetActive(false);
                controller.errorMeter = meter;
            }
            Patches.Patch(AccessTools.Method(typeof(scrController), nameof(scrController.UpdateHitErrorMeter)),
                prefix: new HarmonyMethod(typeof(ReplayHitErrorMeter), nameof(ApplyRecordedHit)) { priority = Priority.First });
            Patches.Patch(AccessTools.Method(typeof(scrController), nameof(scrController.UpdateErrorMeterVisibility)),
                prefix: new HarmonyMethod(typeof(ReplayHitErrorMeter), nameof(UpdateVisibility)) { priority = Priority.First });
        }
        catch { Deactivate(driver); throw; }
    }

    internal static IEnumerable<Canvas> CaptureCanvases()
    {
        if (meter == null) yield break;
        foreach (Canvas canvas in meter.GetComponentsInChildren<Canvas>(true))
            yield return canvas.rootCanvas ?? canvas;
        Canvas parent = meter.GetComponentInParent<Canvas>();
        if (parent != null) yield return parent.rootCanvas ?? parent;
    }

    private static bool ApplyRecordedHit(scrController __instance, scrFloor hitFloor,
        scrPlayer player, scrPlanet priorChosenPlanet)
    {
        if (owner == null || __instance != controller) return true;
        RecordedHitEvent? recorded = owner.ActiveHit;
        if (meter == null || recorded == null || player == null || priorChosenPlanet == null || hitFloor == null) return false;
        var hit = recorded.Value;
        if (hit.MidspinInfiniteMargin) return false;
        HitMargin margin = (HitMargin)Enum.Parse(typeof(HitMargin), hit.Margin, false);
        if (!margin.IsPlayerHitMargin()) return false;
        float angle = (float)(hit.CachedAngle - hit.TargetExitAngle);
        if (hitFloor.isCCW) angle = -angle;
        bool automatic = hit.RdcAuto && !RDC.useOldAuto;
        if (automatic) angle = 0;
        meter.AddHit(angle, margin, automatic ? 1f : (float)hitFloor.marginScale, priorChosenPlanet, hitFloor);
        return false;
    }

    private static bool UpdateVisibility(scrController __instance)
    {
        if (owner == null || __instance != controller) return true;
        if (meter != null) meter.gameObject.SetActive(!controller.paused && Persistence.hitErrorMeterSize != ErrorMeterSize.Off);
        return false;
    }

    internal static void Deactivate(RecordedReplayDriver driver)
    {
        if (!ReferenceEquals(owner, driver)) return;
        try { Patches.UnpatchAll(Patches.Id); }
        finally {
            if (controller != null && controller.errorMeter == meter) controller.errorMeter = original;
            if (original != null) original.gameObject.SetActive(originalActive);
            if (meterObject != null) UnityEngine.Object.Destroy(meterObject);
            meterObject = null;
            owner = null; controller = null; original = null; meter = null;
        }
    }
}
