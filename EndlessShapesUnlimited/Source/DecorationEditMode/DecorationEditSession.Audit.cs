using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.FilesAndFolders;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using BrilliantSkies.Ui.Special.InfoStore;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed partial class DecorationEditSession
    {
        private const string DecorationAuditSourceId = "decoration-edit-current-craft";
        private const int DecorationAuditMaximumDrawnFindings = 400;
        private const int DecorationAuditMaximumPreviewOperations = 8;

        private bool _decorationAuditOpen;
        private DecorationAuditReport _decorationAuditReport;
        private DecorationAuditRepairPlan _decorationAuditPreviewPlan;
        private Vector2 _decorationAuditScroll;
        private int _decorationAuditSelectedFinding = -1;
        private string _decorationAuditMessage =
            "Scan is read-only. Repairs are previewed and never applied without confirmation.";
        private bool _decorationAuditApplyArmed;

        private void DrawDecorationAuditToolbarButton()
        {
            if (GUILayout.Button(
                    new GUIContent(
                        RightToolbarLabel("Audit", "A"),
                        DecorationEditorIconCatalog.Get("settings"),
                        "Scan this craft for corrupt transforms, suspicious tethers, duplicates, capacity hotspots, and serialization risk."),
                    DecorationEditorTheme.ToolButton(_decorationAuditOpen),
                    GUILayout.Width(_toolbarRightControlWidth),
                    GUILayout.Height(EsuHudLayout.Scale(40f))))
            {
                _decorationAuditOpen = !_decorationAuditOpen;
                _decorationAuditApplyArmed = false;
                if (_decorationAuditOpen && _decorationAuditReport == null)
                    RunDecorationAudit();
            }
        }

        private bool DecorationAuditContainsMouse(Vector2 mouse) =>
            _decorationAuditOpen && DecorationAuditWindowRect().Contains(mouse);

        private Rect DecorationAuditWindowRect()
        {
            float margin = EsuHudLayout.Scale(18f);
            float width = Mathf.Min(EsuHudLayout.Scale(820f), Mathf.Max(1f, Screen.width - margin * 2f));
            float height = Mathf.Min(EsuHudLayout.Scale(680f), Mathf.Max(1f, Screen.height - margin * 2f));
            return new Rect(
                Mathf.Max(margin, (Screen.width - width) * 0.5f),
                Mathf.Max(margin, (Screen.height - height) * 0.5f),
                width,
                height);
        }

        private void DrawDecorationAuditWindow(bool interactive)
        {
            if (!_decorationAuditOpen)
                return;

            Rect rect = DecorationAuditWindowRect();
            EsuHudChrome.DrawPanel(rect);
            Rect inner = EsuHudLayout.PanelInnerRect(rect);
            bool previousEnabled = GUI.enabled;
            if (!interactive)
                GUI.enabled = false;

            GUILayout.BeginArea(inner);
            try
            {
                DrawDecorationAuditHeader();
                DecorationEditorTheme.Separator();
                DrawDecorationAuditSummary();
                DecorationEditorTheme.Separator();
                DrawDecorationAuditSelectedFinding();
                DecorationEditorTheme.Separator();
                DrawDecorationAuditRepairPreview();
                DecorationEditorTheme.Separator();
                DrawDecorationAuditFindings();
            }
            finally
            {
                GUILayout.EndArea();
                GUI.enabled = previousEnabled;
            }
        }

        private void DrawDecorationAuditHeader()
        {
            bool previousEnabled = GUI.enabled;
            GUILayout.BeginHorizontal(GUILayout.Height(EsuHudLayout.Scale(28f)));
            try
            {
                GUILayout.Label(
                    new GUIContent(
                        "Craft & Decoration Audit",
                        DecorationEditorIconCatalog.Get("settings")),
                    DecorationEditorTheme.SubHeader,
                    GUILayout.Width(EsuHudLayout.Scale(230f)));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        new GUIContent("Scan", "Capture a fresh read-only snapshot."),
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(62f))))
                {
                    RunDecorationAudit();
                }

                GUI.enabled = previousEnabled && _decorationAuditReport != null;
                if (GUILayout.Button(
                        new GUIContent("Copy", "Copy the complete deterministic report."),
                        _decorationAuditReport == null
                            ? DecorationEditorTheme.DisabledButton
                            : DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(62f))))
                {
                    CopyDecorationAuditReport();
                }
                if (GUILayout.Button(
                        new GUIContent("Save", "Save the complete report under the FtD profile."),
                        _decorationAuditReport == null
                            ? DecorationEditorTheme.DisabledButton
                            : DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(62f))))
                {
                    SaveDecorationAuditReport();
                }

                GUI.enabled = previousEnabled;
                if (GUILayout.Button(
                        new GUIContent("Close", "Close the audit panel without changing the craft."),
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(62f))))
                {
                    _decorationAuditOpen = false;
                    _decorationAuditApplyArmed = false;
                }
            }
            finally
            {
                GUI.enabled = previousEnabled;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawDecorationAuditSummary()
        {
            if (_decorationAuditReport == null)
            {
                GUILayout.Label(
                    "No snapshot captured. Scan does not save, delete, retether, or edit anything.",
                    DecorationEditorTheme.BodyWrap,
                    GUILayout.Height(EsuHudLayout.Scale(42f)));
                return;
            }

            DecorationAuditSummary summary = _decorationAuditReport.Summary;
            string counts = string.Format(
                CultureInfo.InvariantCulture,
                "{0:N0} decorations / {1:N0} managers | Critical {2:N0} | Errors {3:N0} | Warnings {4:N0} | Info {5:N0}",
                summary.DecorationCount,
                summary.ManagerCount,
                summary.CriticalCount,
                summary.ErrorCount,
                summary.WarningCount,
                summary.InformationCount);
            string categories = string.Format(
                CultureInfo.InvariantCulture,
                "Tethers {0:N0} | Transforms {1:N0} | References {2:N0} | Meshes {3:N0}",
                summary.SuspiciousTetherCount,
                summary.TransformFindingCount,
                summary.ReferenceFindingCount,
                summary.MeshFindingCount);
            string remainingCategories = string.Format(
                CultureInfo.InvariantCulture,
                "Duplicate groups {0:N0} | Layers {1:N0} | Capacity {2:N0} | Serialization {3:N0}",
                summary.DuplicateGroupCount,
                summary.LayerFindingCount,
                summary.CapacityHotspotCount,
                summary.SerializationFindingCount);
            GUILayout.Label(counts, summary.HasErrors
                ? DecorationEditorTheme.Warning
                : DecorationEditorTheme.Body);
            GUILayout.Label(categories, DecorationEditorTheme.Mini);
            GUILayout.Label(remainingCategories, DecorationEditorTheme.Mini);
            GUILayout.Label(_decorationAuditMessage ?? string.Empty, DecorationEditorTheme.MiniWrap);
        }

        private void DrawDecorationAuditSelectedFinding()
        {
            if (_decorationAuditReport == null ||
                _decorationAuditSelectedFinding < 0 ||
                _decorationAuditSelectedFinding >= _decorationAuditReport.Findings.Count)
            {
                GUILayout.Label(
                    "Select a finding below for details and its conservative repair guidance.",
                    DecorationEditorTheme.MiniWrap,
                    GUILayout.Height(EsuHudLayout.Scale(62f)));
                return;
            }

            DecorationAuditFinding finding =
                _decorationAuditReport.Findings[_decorationAuditSelectedFinding];
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                SeverityLabel(finding.Severity) + " / " + finding.Category + " / " + finding.Title,
                FindingStyle(finding.Severity),
                GUILayout.ExpandWidth(true));
            bool canSelect = !string.IsNullOrEmpty(finding.ManagerId) &&
                             !string.IsNullOrEmpty(finding.DecorationId);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && canSelect;
            if (GUILayout.Button(
                    new GUIContent("Select target", "Select the live decoration if the snapshot is still current."),
                    canSelect ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(96f))))
            {
                SelectDecorationAuditTarget(finding);
            }
            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();
            GUILayout.Label(finding.Detail, DecorationEditorTheme.MiniWrap);
            if (finding.SuggestedRepair != null)
            {
                GUILayout.Label(
                    "Suggested " + finding.SuggestedRepair.Safety + ": " +
                    finding.SuggestedRepair.Description,
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private void DrawDecorationAuditRepairPreview()
        {
            bool previousEnabled = GUI.enabled;
            GUILayout.BeginVertical(GUILayout.MinHeight(EsuHudLayout.Scale(76f)));
            try
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    "Safe repair preview",
                    DecorationEditorTheme.SectionHeader,
                    GUILayout.Width(EsuHudLayout.Scale(160f)));
                GUILayout.FlexibleSpace();
                GUI.enabled = previousEnabled && _decorationAuditReport != null;
                if (GUILayout.Button(
                        new GUIContent(
                            "Preview safe only",
                            "Build an immutable dry-run plan. Review-required and destructive suggestions stay excluded."),
                        _decorationAuditReport == null
                            ? DecorationEditorTheme.DisabledButton
                            : DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(132f))))
                {
                    PreviewDecorationAuditSafeRepairs();
                }
                GUI.enabled = previousEnabled;
                GUILayout.EndHorizontal();

                if (_decorationAuditPreviewPlan == null)
                {
                    GUILayout.Label(
                        "No plan yet. The safe plan currently permits only finite angle normalization; it never retethers, deletes, clamps placement, or resets corrupt values.",
                        DecorationEditorTheme.MiniWrap);
                    return;
                }

                GUILayout.Label(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Dry run: {0:N0} safe operation(s), {1:N0} operation(s) excluded by safety or snapshot guards.",
                        _decorationAuditPreviewPlan.Operations.Count,
                        _decorationAuditPreviewPlan.ExcludedSuggestionCount),
                    DecorationEditorTheme.Mini);
                int shown = Math.Min(
                    DecorationAuditMaximumPreviewOperations,
                    _decorationAuditPreviewPlan.Operations.Count);
                for (int index = 0; index < shown; index++)
                {
                    DecorationAuditRepairOperation operation =
                        _decorationAuditPreviewPlan.Operations[index];
                    GUILayout.Label(
                        "• " + operation.ManagerId + "/" + operation.DecorationId +
                        ": " + operation.Kind + " " + operation.ExpectedValue +
                        " -> " + operation.ReplacementValue,
                        DecorationEditorTheme.Mini);
                }

                if (_decorationAuditPreviewPlan.Operations.Count > shown)
                {
                    GUILayout.Label(
                        "+ " + (_decorationAuditPreviewPlan.Operations.Count - shown)
                            .ToString("N0", CultureInfo.InvariantCulture) + " more",
                        DecorationEditorTheme.Mini);
                }

                GUI.enabled = previousEnabled && _decorationAuditPreviewPlan.Operations.Count > 0;
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (!_decorationAuditApplyArmed)
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                "Apply preview…",
                                "Arm a second explicit confirmation. The snapshot fingerprint is checked again before mutation."),
                            _decorationAuditPreviewPlan.Operations.Count > 0
                                ? DecorationEditorTheme.Button
                                : DecorationEditorTheme.DisabledButton,
                            GUILayout.Width(EsuHudLayout.Scale(116f))))
                    {
                        _decorationAuditApplyArmed = true;
                    }
                }
                else
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                "Confirm apply",
                                "Atomically apply exactly the displayed safe plan and record one undoable history command."),
                            DecorationEditorTheme.ActiveButton,
                            GUILayout.Width(EsuHudLayout.Scale(116f))))
                    {
                        ApplyDecorationAuditPreview();
                    }
                    if (GUILayout.Button(
                            "Keep dry run",
                            DecorationEditorTheme.Button,
                            GUILayout.Width(EsuHudLayout.Scale(104f))))
                    {
                        _decorationAuditApplyArmed = false;
                    }
                }
                GUILayout.EndHorizontal();
            }
            finally
            {
                GUI.enabled = previousEnabled;
                GUILayout.EndVertical();
            }
        }

        private void DrawDecorationAuditFindings()
        {
            if (_decorationAuditReport == null)
            {
                GUILayout.FlexibleSpace();
                return;
            }

            IReadOnlyList<DecorationAuditFinding> findings = _decorationAuditReport.Findings;
            _decorationAuditScroll = GUILayout.BeginScrollView(
                _decorationAuditScroll,
                GUILayout.ExpandHeight(true));
            try
            {
                int count = Math.Min(findings.Count, DecorationAuditMaximumDrawnFindings);
                for (int index = 0; index < count; index++)
                {
                    DecorationAuditFinding finding = findings[index];
                    string target = string.IsNullOrEmpty(finding.DecorationId)
                        ? finding.ManagerId
                        : finding.ManagerId + "/" + finding.DecorationId;
                    string label = SeverityLabel(finding.Severity) + " | " +
                                   finding.Category + " | " + finding.Title +
                                   (string.IsNullOrEmpty(target) ? string.Empty : " | " + target);
                    if (GUILayout.Button(
                            new GUIContent(label, finding.Detail),
                            index == _decorationAuditSelectedFinding
                                ? DecorationEditorTheme.RowSelected
                                : DecorationEditorTheme.Row,
                            GUILayout.Height(EsuHudLayout.Scale(24f))))
                    {
                        _decorationAuditSelectedFinding = index;
                    }
                }

                if (findings.Count > count)
                {
                    GUILayout.Label(
                        "Display capped at " + count.ToString("N0", CultureInfo.InvariantCulture) +
                        " findings. Copy or save the report for all " +
                        findings.Count.ToString("N0", CultureInfo.InvariantCulture) + ".",
                        DecorationEditorTheme.Warning);
                }
                else if (findings.Count == 0)
                {
                    GUILayout.Label(
                        "No suspicious conditions were found in the supplied snapshot.",
                        DecorationEditorTheme.BodyWrap);
                }
            }
            finally
            {
                GUILayout.EndScrollView();
            }
        }

        private void RunDecorationAudit()
        {
            _decorationAuditApplyArmed = false;
            _decorationAuditPreviewPlan = null;
            _decorationAuditSelectedFinding = -1;
            try
            {
                MainConstruct main = _build.GetCC();
                if (main == null)
                {
                    _decorationAuditReport = null;
                    _decorationAuditMessage = "Audit needs an active craft in build mode.";
                    InfoStore.Add(_decorationAuditMessage);
                    return;
                }

                RefreshForecast(force: true);
                DecorationAuditCraftSnapshot snapshot =
                    DecorationAuditFtdSnapshotAdapter.Capture(
                        main,
                        _forecast,
                        DecorationAuditSourceId,
                        CreateDecorationAuditMetadataContext());
                _decorationAuditReport = DecorationAuditEngine.Scan(snapshot);
                _decorationAuditMessage = _decorationAuditReport.Findings.Count == 0
                    ? "Read-only scan complete: no suspicious conditions found."
                    : "Read-only scan complete: " +
                      _decorationAuditReport.Findings.Count.ToString("N0", CultureInfo.InvariantCulture) +
                      " finding(s). Select a row for details.";
                EsuRuntimeLog.Info("Craft audit", _decorationAuditMessage);
            }
            catch (Exception exception)
            {
                _decorationAuditReport = null;
                _decorationAuditMessage = "Audit failed without changing the craft: " + exception.Message;
                EsuRuntimeLog.Exception("Craft audit", exception, "Craft audit scan failed");
                AdvLogger.LogException(
                    "[Endless Shapes Unlimited] Craft audit scan failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(_decorationAuditMessage);
            }
        }

        private void PreviewDecorationAuditSafeRepairs()
        {
            _decorationAuditApplyArmed = false;
            if (_decorationAuditReport == null)
            {
                _decorationAuditMessage = "Scan the craft before previewing repairs.";
                return;
            }

            _decorationAuditPreviewPlan = _decorationAuditReport.CreateRepairPlan(
                DecorationAuditRepairInclusion.SafeOnly);
            _decorationAuditMessage = _decorationAuditPreviewPlan.Operations.Count == 0
                ? "Dry run complete: no automatically safe repair is available. Review guidance remains non-mutating."
                : "Dry run complete. Review every operation, then use the two-step apply confirmation if desired.";
        }

        private DecorationAuditFtdMetadataContext CreateDecorationAuditMetadataContext()
        {
            var meshCache = new Dictionary<Guid, DecorationAuditMeshMetadata>();
            bool meshCatalogAvailable = _meshByGuid.Count > 0;
            DecorationAuditMeshMetadata ResolveMesh(Guid guid)
            {
                if (meshCache.TryGetValue(guid, out DecorationAuditMeshMetadata cached))
                    return cached;

                DecorationAuditMeshMetadata result;
                if (!meshCatalogAvailable)
                {
                    result = DecorationAuditMeshMetadata.Unknown;
                }
                else if (!_meshByGuid.TryGetValue(
                             guid,
                             out DecorationMeshCatalogEntry entry))
                {
                    result = new DecorationAuditMeshMetadata(
                        DecorationAuditReferenceState.Missing);
                }
                else
                {
                    try
                    {
                        Mesh mesh = _previewRenderer?.GetMesh(entry);
                        if (mesh == null)
                        {
                            result = new DecorationAuditMeshMetadata(
                                DecorationAuditReferenceState.Unreadable,
                                diagnostic: "The mesh reference exists, but its geometry could not be decoded.");
                        }
                        else
                        {
                            Bounds bounds = mesh.bounds;
                            Vector3 size = bounds.size;
                            long triangleCount = 0L;
                            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                            {
                                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                                    continue;
                                ulong subMeshTriangles = mesh.GetIndexCount(subMesh) / 3UL;
                                triangleCount = subMeshTriangles > (ulong)(long.MaxValue - triangleCount)
                                    ? long.MaxValue
                                    : triangleCount + (long)subMeshTriangles;
                            }
                            result = new DecorationAuditMeshMetadata(
                                DecorationAuditReferenceState.Valid,
                                metricsAvailable: true,
                                localSize: ToDecorationAuditVector(size),
                                vertexCount: mesh.vertexCount,
                                triangleCount: triangleCount,
                                localCenter: ToDecorationAuditVector(bounds.center));
                        }
                    }
                    catch (Exception exception)
                    {
                        result = new DecorationAuditMeshMetadata(
                            DecorationAuditReferenceState.Unreadable,
                            diagnostic: exception.GetType().Name + ": " + exception.Message);
                    }
                }

                meshCache[guid] = result;
                return result;
            }

            bool materialCatalogAvailable = _materialByGuid.Count > 0;
            DecorationAuditReferenceState ResolveMaterial(Guid guid) =>
                !materialCatalogAvailable
                    ? DecorationAuditReferenceState.Unknown
                    : _materialByGuid.ContainsKey(guid)
                        ? DecorationAuditReferenceState.Valid
                        : DecorationAuditReferenceState.Missing;

            DecorationAuditLayerSnapshot[] layers = _workspaceLayers.Layers
                .Select(layer => new DecorationAuditLayerSnapshot(
                    layer.Name,
                    layer.Visible,
                    layer.Locked))
                .ToArray();
            return new DecorationAuditFtdMetadataContext(
                ResolveMesh,
                ResolveMaterial,
                (construct, decoration) => _workspaceLayers.LayerFor(
                    DecorationWorkspaceObjectIdentity.Key(construct, decoration)),
                (construct, decoration) => _workspaceLayers.IsLocked(
                    DecorationWorkspaceObjectIdentity.Key(construct, decoration)),
                layers);
        }

        private void ApplyDecorationAuditPreview()
        {
            _decorationAuditApplyArmed = false;
            DecorationAuditRepairPlan plan = _decorationAuditPreviewPlan;
            if (plan == null || plan.Operations.Count == 0)
            {
                _decorationAuditMessage = "There is no safe preview to apply.";
                return;
            }

            DecorationAuditRepairApplyResult result;
            try
            {
                result = plan.ApplyExplicitly(new DecorationAuditEditorRepairAdapter(this));
            }
            catch (Exception exception)
            {
                result = new DecorationAuditRepairApplyResult(
                    DecorationAuditRepairApplyStatus.Failed,
                    0,
                    "Apply failed before mutation: " + exception.Message);
                AdvLogger.LogException(
                    "[Endless Shapes Unlimited] Craft audit repair apply failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
            }

            if (!result.Applied)
            {
                _decorationAuditMessage = result.Message;
                InfoStore.Add(result.Message);
                return;
            }

            int appliedCount = result.AppliedOperationCount;
            RunDecorationAudit();
            _decorationAuditMessage = appliedCount.ToString("N0", CultureInfo.InvariantCulture) +
                                      " audited safe repair(s) applied atomically. Undo is available.";
            InfoStore.Add(_decorationAuditMessage);
            EsuRuntimeLog.Info("Craft audit", _decorationAuditMessage);
        }

        private DecorationAuditRepairApplyResult ApplyDecorationAuditPlanAtomically(
            DecorationAuditRepairPlan plan)
        {
            if (plan == null || plan.Operations.Count == 0)
            {
                return new DecorationAuditRepairApplyResult(
                    DecorationAuditRepairApplyStatus.NothingToApply,
                    0,
                    "The audit plan is empty.");
            }
            if (!string.Equals(plan.SourceId, DecorationAuditSourceId, StringComparison.Ordinal))
            {
                return new DecorationAuditRepairApplyResult(
                    DecorationAuditRepairApplyStatus.Rejected,
                    0,
                    "The audit plan belongs to another craft source.");
            }

            MainConstruct main = _build.GetCC();
            if (main == null)
            {
                return new DecorationAuditRepairApplyResult(
                    DecorationAuditRepairApplyStatus.Rejected,
                    0,
                    "The active craft is no longer available.");
            }

            var resolved = new List<DecorationAuditResolvedRepair>(plan.Operations.Count);
            var targetKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (DecorationAuditRepairOperation operation in plan.Operations)
            {
                if (operation.Safety != DecorationAuditRepairSafety.Safe ||
                    operation.Kind != DecorationAuditRepairKind.NormalizeOrientation)
                {
                    return new DecorationAuditRepairApplyResult(
                        DecorationAuditRepairApplyStatus.Rejected,
                        0,
                        "The in-game adapter accepts safe orientation normalization only.");
                }
                if (!targetKeys.Add(operation.ManagerId + "/" + operation.DecorationId))
                {
                    return new DecorationAuditRepairApplyResult(
                        DecorationAuditRepairApplyStatus.Rejected,
                        0,
                        "The plan contains duplicate repair targets.");
                }
                if (!TryResolveDecorationAuditTarget(
                        main,
                        operation.ManagerId,
                        operation.DecorationId,
                        out AllConstruct construct,
                        out Decoration decoration,
                        out string rejection))
                {
                    return new DecorationAuditRepairApplyResult(
                        DecorationAuditRepairApplyStatus.Rejected,
                        0,
                        rejection);
                }

                if (_workspaceLayers.IsLocked(
                        DecorationWorkspaceObjectIdentity.Key(construct, decoration)))
                {
                    return new DecorationAuditRepairApplyResult(
                        DecorationAuditRepairApplyStatus.RejectedStaleSnapshot,
                        0,
                        "An audit repair target is locked. Unlock it and scan again.");
                }

                DecorationAuditVector3 current = ToDecorationAuditVector(decoration.Orientation.Us);
                if (!current.Equals(operation.ExpectedValue))
                {
                    return new DecorationAuditRepairApplyResult(
                        DecorationAuditRepairApplyStatus.RejectedStaleSnapshot,
                        0,
                        "A target orientation changed after preview; scan again.");
                }
                if (!TryToFiniteVector3(operation.ReplacementValue, out Vector3 replacement))
                {
                    return new DecorationAuditRepairApplyResult(
                        DecorationAuditRepairApplyStatus.Rejected,
                        0,
                        "A proposed orientation is not a finite float vector.");
                }

                DecorationAuditVector3 storedReplacement =
                    ToDecorationAuditVector(replacement);

                resolved.Add(new DecorationAuditResolvedRepair(
                    operation,
                    construct,
                    decoration,
                    replacement,
                    storedReplacement,
                    new DecorationEditSnapshot(decoration)));
            }

            Exception failure = null;
            var completed = new List<DecorationAuditResolvedRepair>(resolved.Count);
            try
            {
                foreach (DecorationAuditResolvedRepair repair in resolved)
                {
                    repair.Decoration.Orientation.Us = repair.Replacement;
                    repair.Decoration.Changed();
                    if (!ToDecorationAuditVector(repair.Decoration.Orientation.Us)
                        .Equals(repair.StoredReplacement))
                    {
                        throw new InvalidOperationException(
                            "FtD did not preserve an audited orientation normalization.");
                    }
                    repair.After = new DecorationEditSnapshot(repair.Decoration);
                    completed.Add(repair);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (failure != null)
            {
                bool rollbackOk = RollbackDecorationAuditRepairs(resolved);
                AdvLogger.LogException(
                    "[Endless Shapes Unlimited] Craft audit repair transaction failed",
                    failure,
                    LogOptions._AlertDevAndCustomerInGame);
                return new DecorationAuditRepairApplyResult(
                    DecorationAuditRepairApplyStatus.Failed,
                    0,
                    rollbackOk
                        ? "Audit repair failed; every target was rolled back."
                        : "Audit repair failed and rollback was incomplete; see the log.");
            }

            foreach (DecorationAuditResolvedRepair repair in completed)
                _transactions.TrackEdit(repair.Decoration, repair.Before);
            _history.Record(new DecorationAuditHistoryCommand(
                "Normalize audited orientations",
                completed.Select(repair => new DecorationAuditHistoryEntry(
                    repair.Construct,
                    repair.Decoration,
                    repair.Before,
                    repair.After))));
            UpdateDirtyFromSelection();
            ResetInspectorFields();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
            return new DecorationAuditRepairApplyResult(
                DecorationAuditRepairApplyStatus.Applied,
                completed.Count,
                completed.Count.ToString("N0", CultureInfo.InvariantCulture) +
                " safe orientation normalization(s) applied.");
        }

        private static bool RollbackDecorationAuditRepairs(
            IReadOnlyList<DecorationAuditResolvedRepair> repairs)
        {
            bool rollbackOk = true;
            for (int index = repairs.Count - 1; index >= 0; index--)
            {
                DecorationAuditResolvedRepair repair = repairs[index];
                try
                {
                    if (!repair.Before.TryRestore(repair.Decoration) ||
                        !repair.Before.Matches(repair.Decoration))
                    {
                        rollbackOk = false;
                    }
                }
                catch
                {
                    rollbackOk = false;
                }
            }
            return rollbackOk;
        }

        internal bool TryRestoreDecorationAuditHistory(
            IReadOnlyList<DecorationAuditHistoryEntry> entries,
            bool restoreBefore,
            string context)
        {
            if (entries == null || entries.Count == 0)
            {
                InfoStore.Add(context + ": audit history is empty.");
                return false;
            }

            var rollback = new DecorationEditSnapshot[entries.Count];
            try
            {
                for (int index = 0; index < entries.Count; index++)
                {
                    Decoration decoration = entries[index]?.Decoration;
                    DecorationEditSnapshot target = restoreBefore
                        ? entries[index]?.Before
                        : entries[index]?.After;
                    if (decoration == null || decoration.IsDeleted || target == null)
                        throw new InvalidOperationException("An audited decoration no longer exists.");
                    rollback[index] = new DecorationEditSnapshot(decoration);
                }

                for (int index = 0; index < entries.Count; index++)
                {
                    DecorationEditSnapshot target = restoreBefore
                        ? entries[index].Before
                        : entries[index].After;
                    if (!target.TryRestore(entries[index].Decoration) ||
                        !target.Matches(entries[index].Decoration))
                    {
                        throw new InvalidOperationException("FtD rejected an audit history snapshot.");
                    }
                }
                return true;
            }
            catch (Exception exception)
            {
                bool rollbackOk = true;
                for (int index = entries.Count - 1; index >= 0; index--)
                {
                    if (rollback[index] == null)
                        continue;
                    try
                    {
                        if (!rollback[index].TryRestore(entries[index].Decoration) ||
                            !rollback[index].Matches(entries[index].Decoration))
                        {
                            rollbackOk = false;
                        }
                    }
                    catch
                    {
                        rollbackOk = false;
                    }
                }

                AdvLogger.LogException(
                    "[Endless Shapes Unlimited] Craft audit history restore failed",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
                InfoStore.Add(rollbackOk
                    ? context + ": restore failed; every target was rolled back."
                    : context + ": restore failed and rollback was incomplete; see the log.");
                return false;
            }
        }

        private void SelectDecorationAuditTarget(DecorationAuditFinding finding)
        {
            if (_decorationAuditReport == null ||
                !string.Equals(
                    new DecorationAuditEditorRepairAdapter(this)
                        .GetCurrentSnapshotFingerprint(DecorationAuditSourceId),
                    _decorationAuditReport.SnapshotFingerprint,
                    StringComparison.Ordinal))
            {
                _decorationAuditMessage =
                    "The craft changed after this audit. Scan again before selecting a target.";
                return;
            }

            MainConstruct main = _build.GetCC();
            string rejection = null;
            if (main == null ||
                !TryResolveDecorationAuditTarget(
                    main,
                    finding.ManagerId,
                    finding.DecorationId,
                    out AllConstruct construct,
                    out Decoration decoration,
                    out rejection))
            {
                _decorationAuditMessage = string.IsNullOrEmpty(rejection)
                    ? "The live audit target is unavailable; scan again."
                    : rejection;
                return;
            }

            SetPrimarySelection(decoration, construct);
            ResetInspectorFields();
            _decorationAuditMessage = "Selected " + finding.ManagerId + "/" + finding.DecorationId + ".";
        }

        private static bool TryResolveDecorationAuditTarget(
            MainConstruct main,
            string managerId,
            string decorationId,
            out AllConstruct construct,
            out Decoration decoration,
            out string rejection)
        {
            construct = null;
            decoration = null;
            rejection = null;
            if (main == null ||
                !TryParseAuditIndex(managerId, "construct-", out int constructIndex) ||
                !TryParseAuditIndex(decorationId, "decoration-", out int decorationIndex))
            {
                rejection = "The audit target id is not resolvable.";
                return false;
            }

            var constructs = new List<AllConstruct>();
            try
            {
                main.AllBasicsRestricted?.GetAllConstructsBelowUsAndIncludingUs(constructs);
                if (constructIndex < 0 || constructIndex >= constructs.Count)
                {
                    rejection = "The audited construct no longer exists.";
                    return false;
                }

                construct = constructs[constructIndex];
                var manager = construct?.Decorations as AllConstructDecorations;
                Decoration[] live = manager?.DecorationList?.ToArray();
                if (manager == null ||
                    live == null ||
                    decorationIndex < 0 ||
                    decorationIndex >= live.Length)
                {
                    rejection = "The audited decoration index no longer exists.";
                    return false;
                }

                decoration = live[decorationIndex];
                if (decoration == null ||
                    decoration.IsDeleted ||
                    !ReferenceEquals(decoration.OurManager, manager))
                {
                    rejection = "The audited decoration is no longer a live member of its manager.";
                    decoration = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                construct = null;
                decoration = null;
                rejection = "Audit target resolution failed: " + exception.Message;
                return false;
            }
        }

        private static bool TryParseAuditIndex(string value, string prefix, out int index)
        {
            index = -1;
            return !string.IsNullOrEmpty(value) &&
                   value.StartsWith(prefix, StringComparison.Ordinal) &&
                   int.TryParse(
                       value.Substring(prefix.Length),
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out index) &&
                   index >= 0;
        }

        private void CopyDecorationAuditReport()
        {
            if (_decorationAuditReport == null)
                return;
            GUIUtility.systemCopyBuffer = DecorationAuditReportFormatter.Format(
                _decorationAuditReport,
                DateTime.UtcNow);
            _decorationAuditMessage = "Complete audit report copied to the clipboard.";
            InfoStore.Add(_decorationAuditMessage);
        }

        private void SaveDecorationAuditReport()
        {
            if (_decorationAuditReport == null)
                return;

            try
            {
                string root = null;
                try { root = Get.ProfilePaths?.ProfileRootDir()?.ToString(); }
                catch { root = null; }
                if (string.IsNullOrWhiteSpace(root))
                    root = Path.GetTempPath();
                string directory = Path.Combine(root, "EndlessShapesUnlimited", "Reports");
                Directory.CreateDirectory(directory);
                string prefix = _decorationAuditReport.SnapshotFingerprint.Length >= 8
                    ? _decorationAuditReport.SnapshotFingerprint.Substring(0, 8)
                    : "snapshot";
                string baseName = "DecorationAudit-" +
                                  DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
                                  "-" + prefix;
                string path = Path.Combine(directory, baseName + ".txt");
                for (int suffix = 1; File.Exists(path); suffix++)
                {
                    path = Path.Combine(
                        directory,
                        baseName + "-" + suffix.ToString(CultureInfo.InvariantCulture) + ".txt");
                }

                File.WriteAllText(
                    path,
                    DecorationAuditReportFormatter.Format(
                        _decorationAuditReport,
                        DateTime.UtcNow),
                    new UTF8Encoding(false));
                _decorationAuditMessage = "Audit report saved: " + path;
                EsuRuntimeLog.Info("Craft audit", "Audit report saved", path);
                InfoStore.Add(_decorationAuditMessage);
            }
            catch (Exception exception)
            {
                _decorationAuditMessage = "Audit report save failed: " + exception.Message;
                EsuRuntimeLog.Exception("Craft audit", exception, "Audit report save failed");
                InfoStore.Add(_decorationAuditMessage);
            }
        }

        private static string SeverityLabel(DecorationAuditSeverity severity)
        {
            switch (severity)
            {
                case DecorationAuditSeverity.Critical:
                    return "CRITICAL";
                case DecorationAuditSeverity.Error:
                    return "ERROR";
                case DecorationAuditSeverity.Warning:
                    return "WARN";
                default:
                    return "INFO";
            }
        }

        private static GUIStyle FindingStyle(DecorationAuditSeverity severity)
        {
            switch (severity)
            {
                case DecorationAuditSeverity.Critical:
                case DecorationAuditSeverity.Error:
                    return DecorationEditorTheme.Error;
                case DecorationAuditSeverity.Warning:
                    return DecorationEditorTheme.Warning;
                default:
                    return DecorationEditorTheme.Body;
            }
        }

        private static DecorationAuditVector3 ToDecorationAuditVector(Vector3 value) =>
            new DecorationAuditVector3(value.x, value.y, value.z);

        private static bool TryToFiniteVector3(
            DecorationAuditVector3 value,
            out Vector3 vector)
        {
            vector = Vector3.zero;
            if (!value.IsFinite ||
                Math.Abs(value.X) > float.MaxValue ||
                Math.Abs(value.Y) > float.MaxValue ||
                Math.Abs(value.Z) > float.MaxValue)
            {
                return false;
            }

            vector = new Vector3((float)value.X, (float)value.Y, (float)value.Z);
            return DecorationEditMath.IsFinite(vector);
        }

        private void ResetDecorationAuditState()
        {
            _decorationAuditOpen = false;
            _decorationAuditReport = null;
            _decorationAuditPreviewPlan = null;
            _decorationAuditScroll = Vector2.zero;
            _decorationAuditSelectedFinding = -1;
            _decorationAuditApplyArmed = false;
            _decorationAuditMessage =
                "Scan is read-only. Repairs are previewed and never applied without confirmation.";
        }

        private sealed class DecorationAuditEditorRepairAdapter : IDecorationAuditRepairAdapter
        {
            private readonly DecorationEditSession _session;

            internal DecorationAuditEditorRepairAdapter(DecorationEditSession session)
            {
                _session = session ?? throw new ArgumentNullException(nameof(session));
            }

            public string GetCurrentSnapshotFingerprint(string sourceId)
            {
                try
                {
                    MainConstruct main = _session._build.GetCC();
                    if (main == null)
                        return string.Empty;
                    DecorationAuditCraftSnapshot snapshot =
                        DecorationAuditFtdSnapshotAdapter.Capture(
                            main,
                            _session._forecast,
                            sourceId,
                            _session.CreateDecorationAuditMetadataContext());
                    return DecorationAuditEngine.ComputeSnapshotFingerprint(snapshot);
                }
                catch
                {
                    return string.Empty;
                }
            }

            public DecorationAuditRepairApplyResult ApplyAtomically(
                DecorationAuditRepairPlan plan) =>
                _session.ApplyDecorationAuditPlanAtomically(plan);
        }

        private sealed class DecorationAuditResolvedRepair
        {
            internal DecorationAuditResolvedRepair(
                DecorationAuditRepairOperation operation,
                AllConstruct construct,
                Decoration decoration,
                Vector3 replacement,
                DecorationAuditVector3 storedReplacement,
                DecorationEditSnapshot before)
            {
                Operation = operation;
                Construct = construct;
                Decoration = decoration;
                Replacement = replacement;
                StoredReplacement = storedReplacement;
                Before = before;
            }

            internal DecorationAuditRepairOperation Operation { get; }

            internal AllConstruct Construct { get; }

            internal Decoration Decoration { get; }

            internal Vector3 Replacement { get; }

            internal DecorationAuditVector3 StoredReplacement { get; }

            internal DecorationEditSnapshot Before { get; }

            internal DecorationEditSnapshot After { get; set; }
        }
    }

    internal sealed class DecorationAuditHistoryEntry
    {
        internal DecorationAuditHistoryEntry(
            AllConstruct construct,
            Decoration decoration,
            DecorationEditSnapshot before,
            DecorationEditSnapshot after)
        {
            Construct = construct;
            Decoration = decoration;
            Before = before;
            After = after;
        }

        internal AllConstruct Construct { get; }

        internal Decoration Decoration { get; }

        internal DecorationEditSnapshot Before { get; }

        internal DecorationEditSnapshot After { get; }
    }

    internal sealed class DecorationAuditHistoryCommand : IDecorationEditCommand
    {
        private readonly DecorationAuditHistoryEntry[] _entries;

        internal DecorationAuditHistoryCommand(
            string label,
            IEnumerable<DecorationAuditHistoryEntry> entries)
        {
            Label = string.IsNullOrWhiteSpace(label)
                ? "Apply audited safe repairs"
                : label;
            _entries = (entries ?? Enumerable.Empty<DecorationAuditHistoryEntry>())
                .Where(entry => entry != null)
                .ToArray();
        }

        public string Label { get; }

        public bool Undo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreDecorationAuditHistory(
                _entries,
                restoreBefore: true,
                Label + " undo");

        public bool Redo(DecorationEditSession session) =>
            session != null &&
            session.TryRestoreDecorationAuditHistory(
                _entries,
                restoreBefore: false,
                Label + " redo");
    }
}
