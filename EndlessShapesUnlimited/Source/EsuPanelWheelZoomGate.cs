using System;
using System.Collections.Generic;
using System.Globalization;
using BrilliantSkies.Ui.Displayer;
using BrilliantSkies.PlayerProfiles;
using DecoLimitLifter.AutomationBuilderMode;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SmartBuildMode;
using HarmonyLib;
using UnityEngine;

namespace DecoLimitLifter
{
    internal struct EsuPanelUiHit
    {
        internal readonly bool IsHit;
        internal readonly string Editor;
        internal readonly string Region;
        internal readonly Vector2 Mouse;

        private EsuPanelUiHit(bool isHit, string editor, string region, Vector2 mouse)
        {
            IsHit = isHit;
            Editor = string.IsNullOrWhiteSpace(editor) ? "none" : editor;
            Region = string.IsNullOrWhiteSpace(region) ? "none" : region;
            Mouse = mouse;
        }

        internal static EsuPanelUiHit Found(string editor, string region, Vector2 mouse) =>
            new EsuPanelUiHit(true, editor, region, mouse);

        internal static EsuPanelUiHit Miss(Vector2 mouse) =>
            new EsuPanelUiHit(false, "none", "none", mouse);

        internal string ToDiagnosticString() =>
            "hit=" + (IsHit ? "true" : "false") +
            "\neditor=" + Editor +
            "\nregion=" + Region +
            "\nmouse=" + Mouse.x.ToString("0.###", CultureInfo.InvariantCulture) +
            "," + Mouse.y.ToString("0.###", CultureInfo.InvariantCulture);
    }

    internal static class EsuPanelUiHitTestRegistry
    {
        private sealed class Registration
        {
            internal readonly object Owner;
            internal readonly Func<Vector2, EsuPanelUiHit> HitTest;

            internal Registration(object owner, Func<Vector2, EsuPanelUiHit> hitTest)
            {
                Owner = owner;
                HitTest = hitTest;
            }
        }

        private static readonly List<Registration> Registrations = new List<Registration>();

        internal static void Register(object owner, Func<Vector2, EsuPanelUiHit> hitTest)
        {
            if (owner == null || hitTest == null)
                return;

            Unregister(owner);
            Registrations.Add(new Registration(owner, hitTest));
        }

        internal static void Unregister(object owner)
        {
            if (owner == null)
                return;

            for (int index = Registrations.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(Registrations[index].Owner, owner))
                    Registrations.RemoveAt(index);
            }
        }

        internal static EsuPanelUiHit HitTestCurrentMouse()
        {
            Vector2 mouse = CurrentGuiMousePosition();
            for (int index = Registrations.Count - 1; index >= 0; index--)
            {
                Func<Vector2, EsuPanelUiHit> hitTest = Registrations[index].HitTest;
                if (hitTest == null)
                    continue;

                try
                {
                    EsuPanelUiHit hit = hitTest(mouse);
                    if (hit.IsHit)
                        return hit;
                }
                catch (Exception exception)
                {
                    EsuRuntimeLog.Exception(
                        "Panel wheel zoom gate",
                        exception,
                        "ESU panel hit-test failed");
                }
            }

            return EsuPanelUiHit.Miss(mouse);
        }

        private static Vector2 CurrentGuiMousePosition() =>
            new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
    }

    internal static class EsuPanelWheelZoomGate
    {
        private const float ZoomEpsilon = 0.0001f;

        internal static EsuPanelUiHit CurrentUiHit()
        {
            EsuPanelUiHit liveHit = EsuPanelUiHitTestRegistry.HitTestCurrentMouse();
            if (liveHit.IsHit)
                return liveHit;

            Vector2 mouse = liveHit.Mouse;
            if (DecorationEditorInputScope.MouseOverEditorUi)
                return EsuPanelUiHit.Found("Decoration/Surface", "scope_fallback", mouse);
            if (SmartBuildInputScope.MouseOverUi)
                return EsuPanelUiHit.Found("Smart Builder", "scope_fallback", mouse);
            if (AutomationBuilderInputScope.MouseOverUi)
                return EsuPanelUiHit.Found("Automation Builder", "scope_fallback", mouse);

            return liveHit;
        }

        internal static bool PointerOverEsuUi() =>
            CurrentUiHit().IsHit;

        internal static void ApplyHybridZoomInputGate(ref bool ignoreInput)
        {
            if (ignoreInput)
                return;

            EsuPanelUiHit hit = CurrentUiHit();
            if (!hit.IsHit)
            {
                EsuHudDiagnostics.RecordHybridZoomAllowed(hit);
                return;
            }

            ignoreInput = true;
            EsuHudDiagnostics.RecordEsuWheelOverUiGate();
            EsuHudDiagnostics.RecordHybridZoomInputIgnored();
            EsuHudDiagnostics.RecordHybridZoomBlocked(hit);
        }

        internal static void ApplyGetZoomGate(float rawZoom, ref float zoom)
        {
            if (Mathf.Abs(zoom) <= ZoomEpsilon)
                return;

            EsuPanelUiHit hit = CurrentUiHit();
            bool mouseWheelInUseThisFrame = MouseWheelInUseThisFrame();
            EsuHudDiagnostics.RecordGetZoomSeen(rawZoom, hit, mouseWheelInUseThisFrame);
            if (hit.IsHit)
            {
                zoom = 0f;
                BlockMouseWheelThisFrame();
                EsuHudDiagnostics.RecordGetZoomBlocked(rawZoom, hit, mouseWheelInUseThisFrame);
                return;
            }

            EsuHudDiagnostics.RecordZoomLeakCandidate(rawZoom, hit, mouseWheelInUseThisFrame);
        }

        private static bool MouseWheelInUseThisFrame()
        {
            try
            {
                return GuiDisplayBase.MouseWheelInUseThisFrame;
            }
            catch
            {
                return false;
            }
        }

        private static void BlockMouseWheelThisFrame()
        {
            try
            {
                GuiDisplayBase.MouseWheelInUse.Now();
            }
            catch
            {
                // The GetZoom result is already zeroed; this is only FtD compatibility bookkeeping.
            }
        }
    }

    [HarmonyPatch(typeof(FtdKeyMap), nameof(FtdKeyMap.GetZoom), new[] { typeof(float), typeof(float), typeof(float) })]
    internal static class EsuPanelWheelZoomGate_FtdKeyMap_GetZoom_Patch
    {
        private static void Postfix(ref float __result)
        {
            float rawZoom = __result;
            EsuPanelWheelZoomGate.ApplyGetZoomGate(rawZoom, ref __result);
        }
    }

    [HarmonyPatch(typeof(HybridZoom), nameof(HybridZoom.Update), new[] { typeof(float), typeof(bool) })]
    internal static class EsuPanelWheelZoomGate_HybridZoom_Update_Patch
    {
        private static bool Prefix(ref bool ignoreInput)
        {
            EsuPanelWheelZoomGate.ApplyHybridZoomInputGate(ref ignoreInput);
            return true;
        }
    }

    [HarmonyPatch(typeof(HybridZoom), nameof(HybridZoom.Update), new[] { typeof(float), typeof(float), typeof(bool) })]
    internal static class EsuPanelWheelZoomGate_HybridZoom_UpdateCurrentValue_Patch
    {
        private static bool Prefix(ref bool ignoreInput)
        {
            EsuPanelWheelZoomGate.ApplyHybridZoomInputGate(ref ignoreInput);
            return true;
        }
    }
}
