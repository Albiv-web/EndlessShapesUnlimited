using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.SerializationHud;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed partial class SmartBuildSession
    {
        private const int MaximumDiagnosticWireCells =
            SmartBuildLimits.MaximumFloodFillCells;
        private int _diagnosticIssueIndex = -1;
        private Vector3i _diagnosticPulseCell;
        private bool _hasDiagnosticPulseCell;
        private float _diagnosticPulseUntil;

        private void DrawSmartBuildDiagnosticOverlay()
        {
            if (_planDirty || _plan?.Construct == null || _plan.Diagnostics == null)
                return;

            SmartBuildCellDiagnostic[] visible = _plan.Diagnostics
                .Where(diagnostic => diagnostic.HasCell &&
                                     diagnostic.State != SmartBuildCellDiagnosticState.Valid)
                .Take(MaximumDiagnosticWireCells)
                .ToArray();
            foreach (SmartBuildCellDiagnostic diagnostic in visible)
            {
                DrawCellWire(
                    _plan.Construct,
                    diagnostic.Cell,
                    DiagnosticColor(diagnostic.State),
                    diagnostic.State == SmartBuildCellDiagnosticState.PreviewOverlap ||
                    diagnostic.State == SmartBuildCellDiagnosticState.CraftCollision
                        ? 4.6f
                        : 3.1f);
            }

            if (_hasDiagnosticPulseCell && Time.unscaledTime <= _diagnosticPulseUntil)
            {
                float pulse = 4.5f + 2f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 8f));
                DrawCellWire(
                    _plan.Construct,
                    _diagnosticPulseCell,
                    Color.white,
                    pulse);
            }
            else if (_hasDiagnosticPulseCell)
            {
                _hasDiagnosticPulseCell = false;
            }
        }

        private void DrawSmartBuildDiagnosticLegend()
        {
            if (_plan == null || _planDirty)
                return;

            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Preview diagnostics", DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                "Yellow skipped | Magenta overlap | Red craft collision | Blue disconnected | " +
                "Orange removal | Cyan replacement",
                DecorationEditorTheme.MiniWrap);

            int issueCount = _plan.Diagnostics.Count(
                diagnostic => diagnostic.State != SmartBuildCellDiagnosticState.Valid);
            bool previous = GUI.enabled;
            GUI.enabled = previous && issueCount > 0;
            if (SmartGUILayoutButton(
                    new GUIContent(
                        issueCount > 0
                            ? "Next issue (" + issueCount.ToString(CultureInfo.InvariantCulture) + ")"
                            : "No preview issues",
                        "Select the responsible node when known and pulse the next affected cell without moving the camera."),
                    issueCount > 0 ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                FocusNextDiagnosticIssue();
            }
            GUI.enabled = previous;

            if (SerializationHudProfile.DeveloperModeEnabled)
                DrawPlanCoordinatorDiagnostics();
        }

        private void DrawPlanCoordinatorDiagnostics()
        {
            SmartBuildPlanCoordinatorDiagnostics diagnostics =
                _planCoordinator.Diagnostics;
            GUILayout.Space(EsuHudLayout.Scale(3f));
            GUILayout.Label("Plan coordinator", DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                "Last " +
                diagnostics.LastPlanningMilliseconds.ToString("0.00", CultureInfo.InvariantCulture) +
                " ms | nodes " + diagnostics.NodeCount.ToString("N0", CultureInfo.InvariantCulture) +
                " | cells " + diagnostics.CellCount.ToString("N0", CultureInfo.InvariantCulture) +
                " | placements " + diagnostics.PlacementCount.ToString("N0", CultureInfo.InvariantCulture),
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Plans " + diagnostics.PlanningPassCount.ToString("N0", CultureInfo.InvariantCulture) +
                " | reused " + diagnostics.PlanReuseCount.ToString("N0", CultureInfo.InvariantCulture) +
                " | " + (diagnostics.NormalPlanIsCurrent ? "current" : "stale") +
                " | last " + diagnostics.LastRevisionKind,
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Revisions G" + diagnostics.GeometryRevision.ToString(CultureInfo.InvariantCulture) +
                " C" + diagnostics.CraftRevision.ToString(CultureInfo.InvariantCulture) +
                " M" + diagnostics.MaterialRevision.ToString(CultureInfo.InvariantCulture) +
                " S" + diagnostics.SymmetryRevision.ToString(CultureInfo.InvariantCulture) +
                " O" + diagnostics.OccupancyRevision.ToString(CultureInfo.InvariantCulture) +
                " | Sel" + diagnostics.SelectionRevision.ToString(CultureInfo.InvariantCulture) +
                " P" + diagnostics.PresentationRevision.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.MiniWrap);
        }

        private void FocusNextDiagnosticIssue()
        {
            if (_plan?.Diagnostics == null)
                return;
            SmartBuildCellDiagnostic[] issues = _plan.Diagnostics
                .Where(diagnostic => diagnostic.State != SmartBuildCellDiagnosticState.Valid)
                .ToArray();
            if (issues.Length == 0)
                return;

            _diagnosticIssueIndex = (_diagnosticIssueIndex + 1) % issues.Length;
            SmartBuildCellDiagnostic issue = issues[_diagnosticIssueIndex];
            if (issue.NodeId >= 0 && _scene?.Select(issue.NodeId) == true)
            {
                _draft = _scene.SelectedPiece;
                _sceneSelectionAnchorId = issue.NodeId;
                RebuildPlan(SmartBuildPlanRevisionKind.Selection);
            }
            if (issue.HasCell)
            {
                _diagnosticPulseCell = issue.Cell;
                _hasDiagnosticPulseCell = true;
                _diagnosticPulseUntil = Time.unscaledTime + 2.5f;
            }
            InfoStore.Add(
                "Smart Builder " + issue.State + ": " +
                (string.IsNullOrWhiteSpace(issue.Message) ? "Preview issue." : issue.Message));
        }

        private static Color DiagnosticColor(SmartBuildCellDiagnosticState state)
        {
            switch (state)
            {
                case SmartBuildCellDiagnosticState.SkippedOccupied:
                    return new Color(1f, 0.86f, 0.08f, 1f);
                case SmartBuildCellDiagnosticState.PreviewOverlap:
                    return new Color(1f, 0.08f, 0.82f, 1f);
                case SmartBuildCellDiagnosticState.CraftCollision:
                    return new Color(1f, 0.08f, 0.08f, 1f);
                case SmartBuildCellDiagnosticState.Disconnected:
                    return new Color(0.15f, 0.52f, 1f, 1f);
                case SmartBuildCellDiagnosticState.Removal:
                    return new Color(1f, 0.42f, 0.08f, 1f);
                case SmartBuildCellDiagnosticState.Replacement:
                    return new Color(0.08f, 0.95f, 1f, 1f);
                default:
                    return new Color(0.72f, 0.72f, 0.72f, 1f);
            }
        }
    }
}
