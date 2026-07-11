using System;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SerializationHud;
using UnityEngine;

namespace DecoLimitLifter.AutomationBuilderMode
{
    internal sealed partial class AutomationBuilderSession
    {
        private bool _workInProgressWarningOpen;
        private bool _deferWorkInProgressWarningDraw;

        private static bool ShouldShowWorkInProgressWarning()
        {
            try
            {
                return SerializationHudProfile.Data?.AutomationBuilderWipWarningAcknowledged != true;
            }
            catch
            {
                return true;
            }
        }

        private Rect WorkInProgressWarningRect()
        {
            float margin = EsuHudLayout.Scale(20f);
            float width = Mathf.Min(
                EsuHudLayout.Scale(620f),
                Mathf.Max(1f, Screen.width - margin * 2f));
            float height = Mathf.Min(
                EsuHudLayout.Scale(270f),
                Mathf.Max(1f, Screen.height - margin * 2f));
            return new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);
        }

        private void DrawWorkInProgressWarning()
        {
            if (EsuHudPreferences.FadeHudBehindModalPopups)
            {
                GUI.DrawTexture(
                    new Rect(0f, 0f, Screen.width, Screen.height),
                    DecorationEditorTheme.DimTexture);
            }

            Rect rect = WorkInProgressWarningRect();
            GUI.Box(rect, GUIContent.none, DecorationEditorTheme.Panel);

            Rect inner = EsuHudLayout.PanelInnerRect(rect, 10f);
            float gap = EsuHudLayout.Scale(8f);
            float headerHeight = EsuHudLayout.Scale(38f);
            float warningHeight = EsuHudLayout.Scale(30f);
            float actionHeight = EsuHudLayout.Scale(42f);
            Rect headerRect = new Rect(inner.x, inner.y, inner.width, headerHeight);
            GUI.Box(headerRect, GUIContent.none, DecorationEditorTheme.DialogHeader);
            GUI.Label(
                headerRect,
                new GUIContent(
                    "Automation Builder is work in progress",
                    DecorationEditorIconCatalog.Get("risk")),
                DecorationEditorTheme.DialogTitle);

            float y = headerRect.yMax + gap;
            Rect warningRect = new Rect(inner.x, y, inner.width, warningHeight);
            GUI.Label(
                warningRect,
                "Experimental and potentially very buggy",
                DecorationEditorTheme.DialogWarning);

            Rect actionRect = new Rect(
                inner.x,
                inner.yMax - actionHeight,
                inner.width,
                actionHeight);
            Rect bodyRect = new Rect(
                inner.x,
                warningRect.yMax + EsuHudLayout.Scale(4f),
                inner.width,
                Mathf.Max(
                    EsuHudLayout.Scale(48f),
                    actionRect.y - gap - warningRect.yMax));
            GUI.Label(
                bodyRect,
                "Automation Builder is unfinished and can fail to represent or apply complex breadboard logic correctly. Back up your craft before using it, then verify every result in the vanilla breadboard editor.",
                DecorationEditorTheme.DialogBody);

            if (GUI.Button(
                    actionRect,
                    new GUIContent(
                        "I understand - continue",
                        DecorationEditorIconCatalog.Get("save"),
                        "Acknowledge this warning for the current player profile and open Automation Builder."),
                    DecorationEditorTheme.DialogActiveButton))
            {
                AcknowledgeWorkInProgressWarning();
            }

            Event current = Event.current;
            if (current != null &&
                current.type == EventType.KeyDown &&
                (current.keyCode == KeyCode.Return ||
                 current.keyCode == KeyCode.KeypadEnter))
            {
                AcknowledgeWorkInProgressWarning();
                current.Use();
            }
        }

        private void AcknowledgeWorkInProgressWarning()
        {
            _workInProgressWarningOpen = false;
            try
            {
                SerializationHudProfile.ProfileData data = SerializationHudProfile.Data;
                if (data == null)
                    return;

                data.AutomationBuilderWipWarningAcknowledged = true;
                ProfileManager.Instance.Save(module => module is SerializationHudProfile);
            }
            catch (Exception exception)
            {
                EsuRuntimeLog.Exception(
                    "Automation Builder",
                    exception,
                    "Could not persist the Automation Builder work-in-progress warning acknowledgement");
                InfoStore.Add("Automation Builder warning acknowledgement could not be saved; it may appear again next time.");
            }
        }
    }
}
