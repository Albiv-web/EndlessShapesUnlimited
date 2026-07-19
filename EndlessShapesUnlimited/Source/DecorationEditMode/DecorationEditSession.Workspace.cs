using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.Presets;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed partial class DecorationEditSession
    {
        private readonly DecorationLayerWorkspace _workspaceLayers =
            DecorationLayerWorkspace.Default;
        private bool _workspaceToolsExpanded;
        private bool _workspacePresetListExpanded;
        private string _workspaceLayerName = "Detail";
        private string _workspaceFolderName = "Details";
        private string _workspaceSelectedLayer = string.Empty;
        private string _workspaceTagsText = string.Empty;
        private string _workspacePresetName = "Decoration group";
        private string _workspacePresetId = string.Empty;
        private Vector2 _workspacePresetScroll;
        private DecorationEditSnapshot _workspaceEyedropper;
        private string _workspaceArrayCountText = "3";
        private readonly string[] _workspaceArrayStepText = { "1", "0", "0" };
        private string _workspaceRadialDegreesText = "360";
        private DecorationLayoutAxis _workspaceLayoutAxis = DecorationLayoutAxis.X;
        private float _workspaceNextVisibilityRefresh;

        private void DrawDecorationWorkspaceTools()
        {
            GUILayout.Space(EsuHudLayout.Scale(4f));
            if (GUILayout.Button(
                    new GUIContent(
                        (_workspaceToolsExpanded ? "[-] " : "[+] ") + "Workspace tools",
                        "Presets, bulk selection, precision layout, arrays, layers, visibility, and persistent locks."),
                    DecorationEditorTheme.SubHeader,
                    GUILayout.Height(EsuHudLayout.Scale(26f))))
            {
                _workspaceToolsExpanded = !_workspaceToolsExpanded;
            }
            if (!_workspaceToolsExpanded)
                return;

            DrawWorkspacePresetTools();
            DecorationEditorTheme.Separator();
            DrawWorkspaceBulkTools();
            DecorationEditorTheme.Separator();
            DrawWorkspaceLayoutTools();
            DecorationEditorTheme.Separator();
            DrawWorkspaceLayerTools();
            DecorationEditorTheme.Separator();
            DrawWorkspaceMeasurements();
        }

        private void DrawWorkspacePresetTools()
        {
            GUILayout.Label("Reusable group preset", DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            _workspacePresetName = GUILayout.TextField(
                _workspacePresetName ?? string.Empty,
                DecorationEditorTheme.TextField);
            if (GUILayout.Button(
                    new GUIContent("Save", "Save the complete selection as a portable named group preset."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(48f))))
            {
                SaveWorkspaceDecorationPreset();
            }
            if (GUILayout.Button(
                    new GUIContent(
                        _workspacePresetListExpanded ? "Hide" : "Browse",
                        "Browse saved decoration group presets."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(60f))))
            {
                _workspacePresetListExpanded = !_workspacePresetListExpanded;
            }
            GUILayout.EndHorizontal();

            if (!_workspacePresetListExpanded)
                return;
            if (!EsuPresetLibrary.Default.TryList(out IReadOnlyList<EsuPresetEntry> entries, out string listMessage))
            {
                GUILayout.Label(listMessage, DecorationEditorTheme.MiniWrap);
                return;
            }

            EsuPresetEntry[] groupEntries = entries
                .Where(entry => entry.Kind == EsuPresetKind.DecorationSelection)
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            float height = Mathf.Min(
                EsuHudLayout.Scale(150f),
                Mathf.Max(EsuHudLayout.Scale(42f), groupEntries.Length * EsuHudLayout.Scale(25f)));
            _workspacePresetScroll = GUILayout.BeginScrollView(
                _workspacePresetScroll,
                alwaysShowHorizontal: false,
                alwaysShowVertical: true,
                GUILayout.Height(height));
            if (groupEntries.Length == 0)
                GUILayout.Label("No decoration group presets saved.", DecorationEditorTheme.MiniWrap);
            foreach (EsuPresetEntry entry in groupEntries)
            {
                GUILayout.BeginHorizontal();
                bool active = string.Equals(_workspacePresetId, entry.Id, StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Button(
                        new GUIContent(entry.Name, entry.Description),
                        active ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row,
                        GUILayout.Height(EsuHudLayout.Scale(23f))))
                {
                    _workspacePresetId = entry.Id;
                    _workspacePresetName = entry.Name;
                }
                if (GUILayout.Button(
                        new GUIContent("Place", "Place this group on the selected or pointed-at block."),
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(48f)),
                        GUILayout.Height(EsuHudLayout.Scale(23f))))
                {
                    PlaceWorkspaceDecorationPreset(entry.Id);
                }
                if (GUILayout.Button(
                        new GUIContent("X", "Delete this preset."),
                        DecorationEditorTheme.Button,
                        GUILayout.Width(EsuHudLayout.Scale(24f)),
                        GUILayout.Height(EsuHudLayout.Scale(23f))))
                {
                    if (EsuPresetLibrary.Default.TryDelete(entry.Id, out string deleteMessage))
                    {
                        if (string.Equals(_workspacePresetId, entry.Id, StringComparison.OrdinalIgnoreCase))
                            _workspacePresetId = string.Empty;
                    }
                    InfoStore.Add(deleteMessage);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void SaveWorkspaceDecorationPreset()
        {
            if (!TryGetSingleConstructWorkspaceSelection(
                    "decoration group preset capture",
                    out List<Decoration> presetSelection,
                    out AllConstruct _,
                    out string frameMessage))
            {
                InfoStore.Add(frameMessage);
                return;
            }
            if (!EsuDecorationGroupPresetPayload.TryCapture(
                    _selected,
                    presetSelection,
                    out EsuDecorationGroupPresetPayload payload,
                    out string captureMessage))
            {
                InfoStore.Add(captureMessage);
                return;
            }

            bool saved = EsuPresetLibrary.Default.TrySave(
                _workspacePresetName,
                EsuPresetKind.DecorationSelection,
                payload,
                overwrite: true,
                out EsuPresetEntry entry,
                out string saveMessage,
                captureMessage,
                ParseWorkspaceTags());
            if (saved)
                _workspacePresetId = entry.Id;
            InfoStore.Add(saveMessage);
        }

        private void PlaceWorkspaceDecorationPreset(string id)
        {
            if (!EsuPresetLibrary.Default.TryRead(
                    id,
                    out EsuDecorationGroupPresetPayload payload,
                    out EsuPresetEntry entry,
                    out string readMessage))
            {
                InfoStore.Add(readMessage);
                return;
            }

            AllConstruct construct;
            Vector3i anchor;
            if (_selected != null && !_selected.IsDeleted && _selectedConstruct != null)
            {
                construct = _selectedConstruct;
                anchor = _selected.TetherPoint.Us;
            }
            else if (_pointerProbe.TryProbe(out DecorationPointerHit hit) && hit != null)
            {
                construct = hit.Construct;
                anchor = hit.Anchor;
            }
            else
            {
                InfoStore.Add("Select a decoration or point at a craft block before placing a preset.");
                return;
            }

            if (!payload.TryCreateSnapshots(anchor, out DecorationEditSnapshot[] snapshots, out string payloadMessage))
            {
                InfoStore.Add(payloadMessage);
                return;
            }
            PasteDecorationSnapshotsInPlace(
                construct,
                snapshots,
                payload.PrimaryIndex,
                "Place preset " + entry.Name,
                "Placed preset '" + entry.Name + "' (" +
                snapshots.Length.ToString("N0", CultureInfo.InvariantCulture) + " decorations).");
        }

        private void DrawWorkspaceBulkTools()
        {
            GUILayout.Label("Bulk selection and eyedropper", DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            WorkspaceButton("Mesh", "Select all decorations with the primary mesh.", SelectWorkspaceMatchingMesh);
            WorkspaceButton("Color", "Select all decorations with the primary paint color.", SelectWorkspaceMatchingColor);
            WorkspaceButton("Material", "Select all decorations with the primary material override.", SelectWorkspaceMatchingMaterial);
            WorkspaceButton("Anchor", "Select all decorations on the primary tether.", SelectWorkspaceMatchingAnchor);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            WorkspaceButton("Filter", "Select every outliner result matching its current search.", SelectWorkspaceFilterResults);
            WorkspaceButton("Invert", "Invert selection on the current construct.", InvertWorkspaceSelection);
            WorkspaceButton("Grow", "Grow selection to decorations whose transformed mesh bounds touch or nearly touch the current selection.", GrowWorkspaceSelection);
            WorkspaceButton("Clear", "Clear the group selection but keep the primary decoration.", ClearWorkspaceGroupSelection);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            WorkspaceButton("Sample", "Sample exact mesh, rotation, scale, color, visibility, and material settings.", SampleWorkspaceEyedropper);
            bool previous = GUI.enabled;
            GUI.enabled = previous && _workspaceEyedropper != null;
            WorkspaceButton("Apply sample", "Atomically apply sampled settings to the complete selection without moving anchors.", ApplyWorkspaceEyedropper);
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
        }

        private void WorkspaceButton(string text, string tooltip, Action action)
        {
            if (GUILayout.Button(
                    new GUIContent(text, tooltip),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                action?.Invoke();
            }
        }

        private void SelectWorkspaceMatchingMesh() =>
            SelectWorkspaceMatches(
                decoration => DecorationWorkspaceBulkTools.MatchesMesh(
                    decoration.MeshGuid.Us,
                    _selected.MeshGuid.Us),
                "mesh");

        private void SelectWorkspaceMatchingColor() =>
            SelectWorkspaceMatches(
                decoration => DecorationWorkspaceBulkTools.MatchesColor(
                    decoration.Color.Us,
                    _selected.Color.Us),
                "color");

        private void SelectWorkspaceMatchingMaterial() =>
            SelectWorkspaceMatches(
                decoration => DecorationWorkspaceBulkTools.MatchesMaterial(
                    decoration.MaterialReplacement.Us,
                    _selected.MaterialReplacement.Us),
                "material");

        private void SelectWorkspaceMatchingAnchor() =>
            SelectWorkspaceMatches(
                decoration => DecorationWorkspaceBulkTools.MatchesAnchor(
                    decoration.TetherPoint.Us,
                    _selected.TetherPoint.Us),
                "anchor");

        private void SelectWorkspaceFilterResults()
        {
            string filter = (_outlinerFilter ?? string.Empty).Trim();
            if (filter.Length == 0)
            {
                InfoStore.Add("Type an Outliner filter before selecting filter results.");
                return;
            }

            // Use the actual visible Outliner rows rather than rebuilding a
            // current-construct-only approximation. Row.SearchText already contains
            // the exact construct label, tether, mesh, GUID, color, material, layer,
            // and tag semantics rendered by the Outliner.
            OutlinerRow[] matches = _outlinerRows
                .Where(row => row != null &&
                              row.Kind == DecorationOutlinerRowKind.Decoration &&
                              row.Decoration != null &&
                              !row.Decoration.IsDeleted &&
                              WorkspaceDecorationVisible(row.Decoration, row.Construct) &&
                              DecorationWorkspaceBulkTools.SearchMatches(row.SearchText, filter))
                .GroupBy(row => row.Decoration)
                .Select(group => group.First())
                .ToArray();

            _selection.Clear();
            foreach (OutlinerRow row in matches)
                _selection.Add(row.Decoration);

            OutlinerRow primary = matches.FirstOrDefault(row => ReferenceEquals(row.Decoration, _selected)) ??
                                   matches.FirstOrDefault();
            if (primary != null)
                SetPrimarySelection(primary.Decoration, primary.Construct);
            else
                PromoteOutlinerSelection(_selectedConstruct);

            ResetInspectorFields();
            bool crossesConstructs = matches.Length > 0 &&
                                     !DecorationWorkspaceBulkTools.TryResolveSingleFrame(
                                         matches.Select(row => row.Construct),
                                         out AllConstruct _);
            InfoStore.Add(
                "Selected " + matches.Length.ToString("N0", CultureInfo.InvariantCulture) +
                " visible Outliner decoration result(s) across the craft." +
                (crossesConstructs
                    ? " Field-only bulk tools remain available; spatial tools are paused until the filter is narrowed to one construct."
                    : string.Empty));
        }

        private void SelectWorkspaceMatches(Func<Decoration, bool> predicate, string label)
        {
            if (_selected == null || _selected.IsDeleted || _selectedConstruct == null)
            {
                InfoStore.Add("Select a primary decoration first.");
                return;
            }
            var manager = _selectedConstruct.Decorations as AllConstructDecorations;
            if (manager == null)
                return;

            Decoration[] matches = manager.DecorationList
                .Where(decoration => decoration != null &&
                                     !decoration.IsDeleted &&
                                     WorkspaceDecorationVisible(decoration, _selectedConstruct) &&
                                     predicate(decoration))
                .ToArray();
            _selection.Clear();
            foreach (Decoration decoration in matches)
                _selection.Add(decoration);
            if (!_selection.Contains(_selected))
            {
                Decoration first = matches.FirstOrDefault();
                if (first != null)
                    SetPrimarySelection(first, _selectedConstruct);
            }
            ResetInspectorFields();
            InfoStore.Add("Selected " + matches.Length.ToString("N0", CultureInfo.InvariantCulture) +
                          " decoration(s) matching " + label + ".");
        }

        private void InvertWorkspaceSelection()
        {
            if (_selectedConstruct?.Decorations is not AllConstructDecorations manager)
            {
                InfoStore.Add("Select a decoration first.");
                return;
            }
            var previous = new HashSet<Decoration>(_selection);
            Decoration[] inverted = manager.DecorationList
                .Where(decoration => decoration != null &&
                                     !decoration.IsDeleted &&
                                     !previous.Contains(decoration) &&
                                     WorkspaceDecorationVisible(decoration, _selectedConstruct))
                .ToArray();
            _selection.Clear();
            foreach (Decoration decoration in inverted)
                _selection.Add(decoration);
            if (inverted.Length > 0)
                SetPrimarySelection(inverted[0], _selectedConstruct);
            else
                PromoteOutlinerSelection(_selectedConstruct);
            ResetInspectorFields();
            InfoStore.Add("Inverted selection: " + inverted.Length.ToString("N0", CultureInfo.InvariantCulture) +
                          " decoration(s).");
        }

        private void GrowWorkspaceSelection()
        {
            if (!TryGetSingleConstructWorkspaceSelection(
                    "Grow selection",
                    out List<Decoration> selectedDecorations,
                    out AllConstruct selectionConstruct,
                    out string frameMessage))
            {
                InfoStore.Add(frameMessage);
                return;
            }
            if (selectionConstruct.Decorations is not AllConstructDecorations manager)
            {
                InfoStore.Add("Grow selection could not resolve the selected construct's decoration manager.");
                return;
            }

            DecorationWorkspaceBounds[] selectedBounds = selectedDecorations
                .Select(decoration => WorkspaceBoundsFor(decoration, out _))
                .Where(bounds => bounds.IsValid)
                .ToArray();
            if (selectedBounds.Length == 0)
            {
                InfoStore.Add("The selected decorations have no usable bounds on this construct.");
                return;
            }

            foreach (Decoration decoration in manager.DecorationList)
            {
                if (decoration == null || decoration.IsDeleted ||
                    !WorkspaceDecorationVisible(decoration, selectionConstruct))
                    continue;
                DecorationWorkspaceBounds candidateBounds = WorkspaceBoundsFor(
                    decoration,
                    out _);
                if (candidateBounds.IsValid &&
                    selectedBounds.Any(selected =>
                        DecorationWorkspaceBulkTools.BoundsTouchOrNear(
                            selected,
                            candidateBounds,
                            DecorationWorkspaceBulkTools.GrowAdjacencyTolerance)))
                {
                    _selection.Add(decoration);
                }
            }
            InfoStore.Add("Grew selection to " + _selection.Count.ToString("N0", CultureInfo.InvariantCulture) +
                          " decoration(s).");
        }

        private DecorationWorkspaceBounds WorkspaceBoundsFor(
            Decoration decoration,
            out bool usedMeshBounds)
        {
            usedMeshBounds = false;
            if (decoration == null || decoration.IsDeleted)
                return DecorationWorkspaceBounds.Invalid;

            if (_meshByGuid.TryGetValue(
                    decoration.MeshGuid.Us,
                    out DecorationMeshCatalogEntry entry))
            {
                Mesh mesh = _previewRenderer?.GetMesh(entry);
                if (mesh != null &&
                    DecorationWorkspaceBulkTools.TryTransformMeshBounds(
                        mesh.bounds,
                        GetDecorationLocalCenter(decoration),
                        decoration.Orientation.Us,
                        decoration.Scaling.Us,
                        out DecorationWorkspaceBounds transformed))
                {
                    usedMeshBounds = true;
                    return transformed;
                }
            }

            return DecorationWorkspaceBulkTools.FallbackBounds(
                GetDecorationLocalCenter(decoration),
                decoration.Scaling.Us);
        }

        private void ClearWorkspaceGroupSelection()
        {
            _selection.Clear();
            if (_selected != null && !_selected.IsDeleted)
                _selection.Add(_selected);
            InfoStore.Add("Selection reduced to the primary decoration.");
        }

        private void SampleWorkspaceEyedropper()
        {
            if (_selected == null || _selected.IsDeleted)
            {
                InfoStore.Add("Select a decoration to sample.");
                return;
            }
            _workspaceEyedropper = new DecorationEditSnapshot(_selected);
            InfoStore.Add("Sampled exact decoration settings.");
        }

        private void ApplyWorkspaceEyedropper()
        {
            if (_workspaceEyedropper == null)
                return;
            ApplyWorkspaceBatch(
                "Apply eyedropper",
                CurrentPrimarySelectionDecorations(),
                (decoration, before) =>
                {
                    if (!DecorationWorkspaceBulkTools.TryBuildEyedropperResult(
                            _workspaceEyedropper,
                            before,
                            out DecorationEditSnapshot result,
                            out string reason))
                    {
                        throw new InvalidOperationException(reason);
                    }

                    decoration.MeshGuid.Us = result.MeshGuid;
                    decoration.Orientation.Us = result.Orientation;
                    DecorationScaleBounds.AllowExtendedScale(decoration);
                    decoration.Scaling.Us = result.Scaling;
                    decoration.Color.Us = result.Color;
                    decoration.HideOriginalMesh.Us = result.HideOriginalMesh;
                    decoration.MaterialReplacement.Us = result.MaterialReplacement;
                });
        }
    }

    internal readonly struct DecorationWorkspaceBounds
    {
        internal DecorationWorkspaceBounds(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
            IsValid = DecorationWorkspaceBulkTools.IsFinite(min) &&
                      DecorationWorkspaceBulkTools.IsFinite(max) &&
                      min.x <= max.x &&
                      min.y <= max.y &&
                      min.z <= max.z;
        }

        internal Vector3 Min { get; }

        internal Vector3 Max { get; }

        internal bool IsValid { get; }

        internal Vector3 Center => IsValid ? (Min + Max) * 0.5f : Vector3.zero;

        internal Vector3 Extents => IsValid ? (Max - Min) * 0.5f : Vector3.zero;

        internal static DecorationWorkspaceBounds Invalid =>
            new DecorationWorkspaceBounds(
                new Vector3(float.NaN, float.NaN, float.NaN),
                new Vector3(float.NaN, float.NaN, float.NaN));
    }

    /// <summary>
    /// Pure predicates and transform math shared by the Workspace bulk-selection UI
    /// and behavioral verification.
    /// </summary>
    internal static class DecorationWorkspaceBulkTools
    {
        internal const float GrowAdjacencyTolerance = 0.075f;
        private const float MinimumFallbackExtent = 0.05f;
        private const float MaximumFallbackExtent = 2f;

        internal static bool MatchesMesh(Guid candidate, Guid reference) =>
            candidate == reference;

        internal static bool MatchesColor(int candidate, int reference) =>
            candidate == reference;

        internal static bool MatchesMaterial(Guid candidate, Guid reference) =>
            candidate == reference;

        internal static bool MatchesAnchor(Vector3i candidate, Vector3i reference) =>
            candidate.x == reference.x &&
            candidate.y == reference.y &&
            candidate.z == reference.z;

        internal static bool SearchMatches(string searchText, string filter)
        {
            string needle = (filter ?? string.Empty).Trim();
            return needle.Length > 0 &&
                   (searchText ?? string.Empty).IndexOf(
                       needle,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool TryResolveSingleFrame<TFrame>(
            IEnumerable<TFrame> frames,
            out TFrame frame)
            where TFrame : class
        {
            frame = null;
            foreach (TFrame candidate in frames ?? Enumerable.Empty<TFrame>())
            {
                if (candidate == null)
                    return false;
                if (frame == null)
                {
                    frame = candidate;
                    continue;
                }
                if (!ReferenceEquals(frame, candidate))
                    return false;
            }

            return frame != null;
        }

        internal static bool TryTransformMeshBounds(
            Bounds meshBounds,
            Vector3 decorationCenter,
            Vector3 orientation,
            Vector3 scaling,
            out DecorationWorkspaceBounds transformed)
        {
            transformed = DecorationWorkspaceBounds.Invalid;
            if (!IsFinite(meshBounds.center) ||
                !IsFinite(meshBounds.extents) ||
                !IsFinite(decorationCenter) ||
                !IsFinite(orientation) ||
                !IsFinite(scaling))
            {
                return false;
            }

            Vector3 absoluteScale = Abs(scaling);
            Vector3 localExtents = Vector3.Scale(meshBounds.extents, absoluteScale);
            if (!IsFinite(localExtents) ||
                localExtents.x < 0f ||
                localExtents.y < 0f ||
                localExtents.z < 0f)
            {
                return false;
            }

            Quaternion rotation = QuaternionFromUnityEuler(orientation);
            Vector3 transformedCenter = decorationCenter +
                                        Rotate(rotation, Vector3.Scale(meshBounds.center, scaling));
            Vector3 x = Rotate(rotation, new Vector3(localExtents.x, 0f, 0f));
            Vector3 y = Rotate(rotation, new Vector3(0f, localExtents.y, 0f));
            Vector3 z = Rotate(rotation, new Vector3(0f, 0f, localExtents.z));
            Vector3 transformedExtents = new Vector3(
                Math.Abs(x.x) + Math.Abs(y.x) + Math.Abs(z.x),
                Math.Abs(x.y) + Math.Abs(y.y) + Math.Abs(z.y),
                Math.Abs(x.z) + Math.Abs(y.z) + Math.Abs(z.z));
            transformed = new DecorationWorkspaceBounds(
                transformedCenter - transformedExtents,
                transformedCenter + transformedExtents);
            return transformed.IsValid;
        }

        internal static DecorationWorkspaceBounds FallbackBounds(
            Vector3 center,
            Vector3 scaling)
        {
            if (!IsFinite(center))
                return DecorationWorkspaceBounds.Invalid;

            Vector3 raw = IsFinite(scaling) ? Abs(scaling) * 0.5f : Vector3.one * 0.5f;
            Vector3 extents = new Vector3(
                Mathf.Clamp(raw.x, MinimumFallbackExtent, MaximumFallbackExtent),
                Mathf.Clamp(raw.y, MinimumFallbackExtent, MaximumFallbackExtent),
                Mathf.Clamp(raw.z, MinimumFallbackExtent, MaximumFallbackExtent));
            return new DecorationWorkspaceBounds(center - extents, center + extents);
        }

        internal static bool BoundsTouchOrNear(
            DecorationWorkspaceBounds first,
            DecorationWorkspaceBounds second,
            float tolerance)
        {
            if (!first.IsValid || !second.IsValid ||
                float.IsNaN(tolerance) || float.IsInfinity(tolerance) ||
                tolerance < 0f)
            {
                return false;
            }

            float gapX = AxisGap(first.Min.x, first.Max.x, second.Min.x, second.Max.x);
            float gapY = AxisGap(first.Min.y, first.Max.y, second.Min.y, second.Max.y);
            float gapZ = AxisGap(first.Min.z, first.Max.z, second.Min.z, second.Max.z);
            return gapX * gapX + gapY * gapY + gapZ * gapZ <= tolerance * tolerance;
        }

        internal static bool TryTranslateDecorationOriginForBoundsCenterMove(
            Vector3 decorationOrigin,
            Vector3 beforeBoundsCenter,
            Vector3 afterBoundsCenter,
            out Vector3 targetDecorationOrigin)
        {
            targetDecorationOrigin = Vector3.zero;
            if (!IsFinite(decorationOrigin) ||
                !IsFinite(beforeBoundsCenter) ||
                !IsFinite(afterBoundsCenter))
            {
                return false;
            }

            targetDecorationOrigin = decorationOrigin +
                                     (afterBoundsCenter - beforeBoundsCenter);
            return IsFinite(targetDecorationOrigin);
        }

        internal static bool TryBuildEyedropperResult(
            DecorationEditSnapshot sample,
            DecorationEditSnapshot target,
            out DecorationEditSnapshot result,
            out string reason)
        {
            result = null;
            if (sample == null || target == null)
            {
                reason = "The decoration sample or target is unavailable.";
                return false;
            }

            return DecorationEditSnapshot.TryCreatePortable(
                target.TetherPoint,
                target.Positioning,
                sample.Scaling,
                sample.Orientation,
                sample.MeshGuid,
                sample.Color,
                sample.HideOriginalMesh,
                sample.MaterialReplacement,
                out result,
                out reason);
        }

        internal static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static Vector3 Abs(Vector3 value) =>
            new Vector3(Math.Abs(value.x), Math.Abs(value.y), Math.Abs(value.z));

        // Unity applies Euler Z, then X, then Y. Keeping the conversion managed makes
        // the bounds planner usable in the standalone verifier as well as in-game.
        private static Quaternion QuaternionFromUnityEuler(Vector3 degrees)
        {
            const double degreesToHalfRadians = Math.PI / 360d;
            double halfX = degrees.x * degreesToHalfRadians;
            double halfY = degrees.y * degreesToHalfRadians;
            double halfZ = degrees.z * degreesToHalfRadians;
            var x = new Quaternion((float)Math.Sin(halfX), 0f, 0f, (float)Math.Cos(halfX));
            var y = new Quaternion(0f, (float)Math.Sin(halfY), 0f, (float)Math.Cos(halfY));
            var z = new Quaternion(0f, 0f, (float)Math.Sin(halfZ), (float)Math.Cos(halfZ));
            return Multiply(Multiply(y, x), z);
        }

        private static Quaternion Multiply(Quaternion left, Quaternion right) =>
            new Quaternion(
                left.w * right.x + left.x * right.w + left.y * right.z - left.z * right.y,
                left.w * right.y - left.x * right.z + left.y * right.w + left.z * right.x,
                left.w * right.z + left.x * right.y - left.y * right.x + left.z * right.w,
                left.w * right.w - left.x * right.x - left.y * right.y - left.z * right.z);

        private static Vector3 Rotate(Quaternion rotation, Vector3 value)
        {
            Vector3 q = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 doubledCross = Cross(q, value) * 2f;
            return value + doubledCross * rotation.w + Cross(q, doubledCross);
        }

        private static Vector3 Cross(Vector3 left, Vector3 right) =>
            new Vector3(
                left.y * right.z - left.z * right.y,
                left.z * right.x - left.x * right.z,
                left.x * right.y - left.y * right.x);

        private static float AxisGap(
            float firstMin,
            float firstMax,
            float secondMin,
            float secondMax)
        {
            if (firstMax < secondMin)
                return secondMin - firstMax;
            if (secondMax < firstMin)
                return firstMin - secondMax;
            return 0f;
        }
    }
}
