using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.Presets;
using EndlessShapes2;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed partial class DecorationEditSession
    {
        private enum WorkspacePrecisionSnapMode
        {
            None,
            Surface,
            Anchor,
            Axis
        }

        private WorkspacePrecisionSnapMode _workspacePrecisionSnapMode;

        private void DrawWorkspaceLayoutTools()
        {
            GUILayout.Label("Precision layout and arrays", DecorationEditorTheme.SubHeader);
            if (WorkspaceSelectionSpansConstructs())
            {
                GUILayout.Label(
                    "Cross-construct selection: spatial layout, snap, arrays, presets, Grow, and ruler are paused. Narrow the Outliner filter to one construct.",
                    DecorationEditorTheme.MiniWrap);
            }
            GUILayout.BeginHorizontal();
            foreach (DecorationLayoutAxis axis in new[]
                     {
                         DecorationLayoutAxis.X,
                         DecorationLayoutAxis.Y,
                         DecorationLayoutAxis.Z
                     })
            {
                if (GUILayout.Button(
                        new GUIContent(axis.ToString(), "Use the " + axis + " axis for layout operations."),
                        DecorationEditorTheme.ToolButton(_workspaceLayoutAxis == axis),
                        GUILayout.Height(EsuHudLayout.Scale(23f))))
                {
                    _workspaceLayoutAxis = axis;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            WorkspaceButton("Align min", "Align minimum bounds edges to the primary decoration.", () =>
                RunWorkspaceAlignment(DecorationAlignmentMode.MinimumEdge));
            WorkspaceButton("Centers", "Align centers to the primary decoration.", () =>
                RunWorkspaceAlignment(DecorationAlignmentMode.Center));
            WorkspaceButton("Align max", "Align maximum bounds edges to the primary decoration.", () =>
                RunWorkspaceAlignment(DecorationAlignmentMode.MaximumEdge));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            WorkspaceButton("Distribute", "Distribute selected centers evenly between the two outer objects.", () =>
                RunWorkspaceDistribution(useEdges: false));
            WorkspaceButton("Edge gaps", "Distribute selected objects with even edge clearance.", () =>
                RunWorkspaceDistribution(useEdges: true));
            WorkspaceButton("Match rot", "Match every editable selection member to the primary rotation.", () =>
                RunWorkspaceMatch(matchRotation: true, matchScale: false));
            WorkspaceButton("Match scale", "Match every editable selection member to the primary scale.", () =>
                RunWorkspaceMatch(matchRotation: false, matchScale: true));
            GUILayout.EndHorizontal();

            GUILayout.Label("Snap selection", DecorationEditorTheme.Mini);
            GUILayout.BeginHorizontal();
            DrawWorkspacePrecisionSnapButton(
                WorkspacePrecisionSnapMode.Surface,
                "Surface",
                "Pick a construct surface. The primary bounds support is placed against its outward plane and the editable group keeps its layout.");
            DrawWorkspacePrecisionSnapButton(
                WorkspacePrecisionSnapMode.Anchor,
                "Anchor",
                "Pick a construct block. The primary center snaps to its anchor cell and the editable group keeps its layout.");
            DrawWorkspacePrecisionSnapButton(
                WorkspacePrecisionSnapMode.Axis,
                "Axis",
                "Pick a construct point. The primary center projects onto the selected X/Y/Z axis through that point.");
            GUILayout.EndHorizontal();

            GUILayout.Label("Linear array", DecorationEditorTheme.Mini);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Count", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(38f)));
            _workspaceArrayCountText = GUILayout.TextField(
                _workspaceArrayCountText ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Width(EsuHudLayout.Scale(42f)));
            for (int axis = 0; axis < 3; axis++)
            {
                GUILayout.Label("XYZ"[axis].ToString(), DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(10f)));
                _workspaceArrayStepText[axis] = GUILayout.TextField(
                    _workspaceArrayStepText[axis] ?? string.Empty,
                    DecorationEditorTheme.TextField,
                    GUILayout.Width(EsuHudLayout.Scale(45f)));
            }
            if (GUILayout.Button(
                    new GUIContent("Make", "Create count-1 translated copies as one undoable transaction."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(44f))))
            {
                CreateWorkspaceArray(radial: false);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Radial degrees", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(84f)));
            _workspaceRadialDegreesText = GUILayout.TextField(
                _workspaceRadialDegreesText ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.Width(EsuHudLayout.Scale(58f)));
            if (GUILayout.Button(
                    new GUIContent("Make radial", "Create count-1 copies around the primary pivot and selected axis."),
                    DecorationEditorTheme.Button))
            {
                CreateWorkspaceArray(radial: true);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawWorkspacePrecisionSnapButton(
            WorkspacePrecisionSnapMode mode,
            string label,
            string tooltip)
        {
            if (!GUILayout.Button(
                    new GUIContent(label, tooltip + " Right-click the viewport to cancel."),
                    DecorationEditorTheme.ToolButton(_workspacePrecisionSnapMode == mode),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                return;
            }

            _workspacePrecisionSnapMode = _workspacePrecisionSnapMode == mode
                ? WorkspacePrecisionSnapMode.None
                : mode;
            InfoStore.Add(
                _workspacePrecisionSnapMode == WorkspacePrecisionSnapMode.None
                    ? "Precision snap cancelled."
                    : "Precision " + label.ToLowerInvariant() +
                      " snap armed. Left-click a point on the selected construct; right-click cancels.");
        }

        private bool TryHandleWorkspacePrecisionSnapInput()
        {
            if (_workspacePrecisionSnapMode == WorkspacePrecisionSnapMode.None)
                return false;

            DecorationEditorInputScope.ClaimBuildInputForFrames();
            DecorationEditorInputScope.ClaimCameraInputForFrames();
            if (Input.GetMouseButtonDown(1))
            {
                _workspacePrecisionSnapMode = WorkspacePrecisionSnapMode.None;
                InfoStore.Add("Precision snap cancelled.");
                return true;
            }

            if (!Input.GetMouseButtonDown(0))
                return true;

            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit) || hit == null)
            {
                InfoStore.Add("Precision snap did not find a construct surface under the cursor.");
                return true;
            }
            if (_selectedConstruct == null || !ReferenceEquals(hit.Construct, _selectedConstruct))
            {
                InfoStore.Add("Precision snap must target the same construct as the selected decorations.");
                return true;
            }
            if (!TryBuildWorkspaceLayoutItems(
                    out List<Decoration> decorations,
                    out DecorationLayoutItem[] items,
                    out string referenceKey,
                    out string message))
            {
                InfoStore.Add(message);
                return true;
            }

            DecorationLayoutPlan plan;
            bool planned;
            switch (_workspacePrecisionSnapMode)
            {
                case WorkspacePrecisionSnapMode.Surface:
                    planned = DecorationPrecisionSnapTools.TrySnapToSurface(
                        items,
                        referenceKey,
                        hit.LocalHit,
                        LocalNormalFromHit(hit),
                        out plan,
                        out message);
                    break;
                case WorkspacePrecisionSnapMode.Anchor:
                    planned = DecorationPrecisionSnapTools.TrySnapToAnchor(
                        items,
                        referenceKey,
                        ToVector3(hit.Anchor),
                        out plan,
                        out message);
                    break;
                case WorkspacePrecisionSnapMode.Axis:
                    planned = DecorationPrecisionSnapTools.TrySnapToAxis(
                        items,
                        referenceKey,
                        hit.LocalHit,
                        WorkspaceAxisVector(_workspaceLayoutAxis),
                        out plan,
                        out message);
                    break;
                default:
                    return true;
            }

            if (!planned)
            {
                InfoStore.Add(message);
                return true;
            }

            ApplyWorkspaceLayoutPlan(decorations, plan, message);
            _workspacePrecisionSnapMode = WorkspacePrecisionSnapMode.None;
            return true;
        }

        private void RunWorkspaceAlignment(DecorationAlignmentMode mode)
        {
            if (!TryBuildWorkspaceLayoutItems(
                    out List<Decoration> decorations,
                    out DecorationLayoutItem[] items,
                    out string referenceKey,
                    out string message) ||
                !DecorationLayoutTools.TryAlign(
                    items,
                    referenceKey,
                    _workspaceLayoutAxis,
                    mode,
                    out DecorationLayoutPlan plan,
                    out message))
            {
                InfoStore.Add(message);
                return;
            }
            ApplyWorkspaceLayoutPlan(decorations, plan, message);
        }

        private void RunWorkspaceDistribution(bool useEdges)
        {
            if (!TryBuildWorkspaceLayoutItems(
                    out List<Decoration> decorations,
                    out DecorationLayoutItem[] items,
                    out _,
                    out string message) ||
                !DecorationLayoutTools.TryDistribute(
                    items,
                    _workspaceLayoutAxis,
                    useEdges,
                    out DecorationLayoutPlan plan,
                    out message))
            {
                InfoStore.Add(message);
                return;
            }
            ApplyWorkspaceLayoutPlan(decorations, plan, message);
        }

        private void RunWorkspaceMatch(bool matchRotation, bool matchScale)
        {
            if (!TryBuildWorkspaceLayoutItems(
                    out List<Decoration> decorations,
                    out DecorationLayoutItem[] items,
                    out string referenceKey,
                    out string message) ||
                !DecorationLayoutTools.TryMatchTransform(
                    items,
                    referenceKey,
                    matchRotation,
                    matchScale,
                    out DecorationLayoutPlan plan,
                    out message))
            {
                InfoStore.Add(message);
                return;
            }
            ApplyWorkspaceLayoutPlan(decorations, plan, message);
        }

        private bool TryBuildWorkspaceLayoutItems(
            out List<Decoration> decorations,
            out DecorationLayoutItem[] items,
            out string referenceKey,
            out string message)
        {
            items = Array.Empty<DecorationLayoutItem>();
            referenceKey = string.Empty;
            if (!TryGetSingleConstructWorkspaceSelection(
                    "precision layout",
                    out decorations,
                    out AllConstruct _,
                    out message))
                return false;

            items = new DecorationLayoutItem[decorations.Count];
            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                string key = index.ToString(CultureInfo.InvariantCulture) + ":" + WorkspaceObjectKey(decoration);
                if (!TryCreateWorkspaceLayoutItem(
                        decoration,
                        key,
                        out items[index],
                        out string boundsMessage))
                {
                    items = Array.Empty<DecorationLayoutItem>();
                    message = "Precision layout object #" +
                              (index + 1).ToString(CultureInfo.InvariantCulture) +
                              " is invalid: " + boundsMessage;
                    return false;
                }
                if (ReferenceEquals(decoration, _selected))
                    referenceKey = key;
            }
            if (referenceKey.Length == 0)
                referenceKey = items[0].Key;
            message = "Layout selection prepared.";
            return true;
        }

        private bool TryCreateWorkspaceLayoutItem(
            Decoration decoration,
            string key,
            out DecorationLayoutItem item,
            out string message)
        {
            item = null;
            if (decoration == null || decoration.IsDeleted)
            {
                message = "the decoration is no longer available.";
                return false;
            }

            DecorationWorkspaceBounds bounds = WorkspaceBoundsFor(decoration, out _);
            if (!bounds.IsValid)
            {
                message = "finite transformed mesh bounds could not be resolved.";
                return false;
            }

            item = new DecorationLayoutItem(
                key,
                bounds.Center,
                bounds.Extents,
                decoration.Orientation.Us,
                decoration.Scaling.Us,
                editable: !WorkspaceDecorationLocked(decoration));
            if (!DecorationLayoutTools.IsValidItem(item))
            {
                item = null;
                message = "the transform or transformed mesh bounds are invalid.";
                return false;
            }

            message = "transformed mesh bounds are valid.";
            return true;
        }

        private void ApplyWorkspaceLayoutPlan(
            List<Decoration> decorations,
            DecorationLayoutPlan plan,
            string successMessage)
        {
            if (decorations == null || plan == null || !plan.IsValid ||
                decorations.Count != plan.After.Length || _selectedConstruct == null)
            {
                InfoStore.Add("Layout plan became stale before it could be applied.");
                return;
            }
            if (!DecorationWorkspaceBulkTools.TryResolveSingleFrame(
                    decorations.Select(WorkspaceConstructFor),
                    out AllConstruct planConstruct) ||
                !ReferenceEquals(planConstruct, _selectedConstruct))
            {
                InfoStore.Add(
                    "Layout plan was rejected because its selection no longer belongs to one construct-local coordinate frame.");
                return;
            }

            var before = decorations.Select(decoration => new DecorationEditSnapshot(decoration)).ToArray();
            var placements = new MultiTransformPlacement[decorations.Count];
            var changed = new bool[decorations.Count];
            for (int index = 0; index < decorations.Count; index++)
            {
                changed[index] = WorkspaceLayoutTransformChanged(
                    plan.Before[index],
                    plan.After[index]);
                if (!changed[index])
                    continue;
                // LayoutItem.Center is the transformed mesh AABB center, not the
                // decoration origin. Apply its planned translation to the live origin
                // so offset mesh bounds align exactly without moving the origin onto
                // the bounds center.
                if (!DecorationWorkspaceBulkTools.TryTranslateDecorationOriginForBoundsCenterMove(
                        GetDecorationLocalCenter(decorations[index]),
                        plan.Before[index].Center,
                        plan.After[index].Center,
                        out Vector3 targetDecorationOrigin))
                {
                    InfoStore.Add("Layout rejected because object #" + (index + 1) +
                                  " produced a non-finite bounds-center translation.");
                    return;
                }
                if (!TryResolveMultiTransformPlacement(
                        before[index],
                        targetDecorationOrigin,
                        plan.After[index].Rotation,
                        plan.After[index].Scale,
                        out placements[index]))
                {
                    InfoStore.Add("Layout rejected because object #" + (index + 1) +
                                  " cannot keep or resolve a valid tether within +/-10m.");
                    return;
                }
            }

            try
            {
                for (int index = 0; index < decorations.Count; index++)
                {
                    if (!changed[index])
                        continue;
                    Decoration decoration = decorations[index];
                    MultiTransformPlacement placement = placements[index];
                    Vector3i current = decoration.TetherPoint.Us;
                    if (!SameTether(current, placement.TetherPoint))
                    {
                        var shift = new Vector3i(
                            placement.TetherPoint.x - current.x,
                            placement.TetherPoint.y - current.y,
                            placement.TetherPoint.z - current.z);
                        if (decoration.OurManager == null || !decoration.OurManager.ShiftDecoration(decoration, shift))
                            throw new InvalidOperationException("FTD rejected a layout tether shift.");
                    }
                    decoration.Positioning.Us = placement.Positioning;
                    decoration.Orientation.Us = placement.Orientation;
                    DecorationScaleBounds.AllowExtendedScale(decoration);
                    decoration.Scaling.Us = placement.Scaling;
                    decoration.Changed();
                }
            }
            catch (Exception exception)
            {
                for (int index = 0; index < decorations.Count; index++)
                    before[index].TryRestore(decorations[index]);
                InfoStore.Add("Layout failed and was rolled back: " + exception.Message);
                return;
            }

            RecordWorkspaceSnapshotBatch(plan.Label, decorations, before);
            InfoStore.Add(successMessage);
        }

        private static bool WorkspaceLayoutTransformChanged(
            DecorationLayoutItem before,
            DecorationLayoutItem after) =>
            before == null || after == null ||
            before.Center != after.Center ||
            before.Rotation != after.Rotation ||
            before.Scale != after.Scale;

        private void CreateWorkspaceArray(bool radial)
        {
            if (!TryGetSingleConstructWorkspaceSelection(
                    radial ? "radial array" : "linear array",
                    out List<Decoration> sources,
                    out AllConstruct arrayConstruct,
                    out string frameMessage))
            {
                InfoStore.Add(frameMessage);
                return;
            }
            if (WorkspaceHasLockedSelection())
            {
                InfoStore.Add("Unlock the selection before duplicating it as an array.");
                return;
            }
            if (!int.TryParse(
                    (_workspaceArrayCountText ?? string.Empty).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int count) ||
                count < 2 ||
                (long)sources.Count * (count - 1) > DecorationLayoutTools.MaximumArrayOutput)
            {
                InfoStore.Add("Array count must create between 1 and 100,000 new decorations.");
                return;
            }

            if (!TryParseWorkspaceVector(_workspaceArrayStepText, out Vector3 step))
            {
                InfoStore.Add("Array step must contain three finite numbers.");
                return;
            }
            float totalDegrees = 0f;
            if (radial &&
                (!FlexibleFloatParser.TryParse(_workspaceRadialDegreesText, out totalDegrees) ||
                 float.IsNaN(totalDegrees) || float.IsInfinity(totalDegrees)))
            {
                InfoStore.Add("Radial array degrees must be finite.");
                return;
            }

            DecorationEditSnapshot[] originals = sources
                .Select(decoration => new DecorationEditSnapshot(decoration))
                .ToArray();
            Vector3 pivot = GetDecorationLocalCenter(_selected);
            Vector3 axis = WorkspaceAxisVector(_workspaceLayoutAxis);
            float angleStep = radial ? totalDegrees / count : 0f;
            var createdSnapshots = new List<DecorationEditSnapshot>(sources.Count * (count - 1));
            for (int copyIndex = 1; copyIndex < count; copyIndex++)
            {
                Quaternion rotation = radial
                    ? Quaternion.AngleAxis(angleStep * copyIndex, axis)
                    : Quaternion.identity;
                for (int sourceIndex = 0; sourceIndex < originals.Length; sourceIndex++)
                {
                    DecorationEditSnapshot source = originals[sourceIndex];
                    Vector3 center = ToVector3(source.TetherPoint) + source.Positioning;
                    Vector3 targetCenter = radial
                        ? pivot + rotation * (center - pivot)
                        : center + step * copyIndex;
                    Vector3 orientation = radial
                        ? (rotation * Quaternion.Euler(source.Orientation)).eulerAngles
                        : source.Orientation;
                    if (!TryCreateWorkspaceSnapshotAtCenter(
                            source,
                            targetCenter,
                            orientation,
                            out DecorationEditSnapshot snapshot,
                            out string failure))
                    {
                        InfoStore.Add("Array preflight rejected copy " + copyIndex + ": " + failure);
                        return;
                    }
                    createdSnapshots.Add(snapshot);
                }
            }

            PasteDecorationSnapshotsInPlace(
                arrayConstruct,
                createdSnapshots.ToArray(),
                primaryIndex: 0,
                radial ? "Create radial array" : "Create linear array",
                "Created " + createdSnapshots.Count.ToString("N0", CultureInfo.InvariantCulture) +
                (radial ? " radial" : " linear") + " array decorations.");
        }

        private bool TryCreateWorkspaceSnapshotAtCenter(
            DecorationEditSnapshot source,
            Vector3 center,
            Vector3 orientation,
            out DecorationEditSnapshot snapshot,
            out string message)
        {
            snapshot = null;
            Vector3i tether = source.TetherPoint;
            Vector3 positioning = center - ToVector3(tether);
            if (!HasBlock(_selectedConstruct, tether) ||
                !DecorationEditMath.IsWithinPositionLimit(positioning))
            {
                Vector3i rounded = RoundToCell(center);
                if (HasBlock(_selectedConstruct, rounded))
                    tether = rounded;
                else if (!TryFindAnchorFollowTarget(_selectedConstruct, center, source.TetherPoint, out tether))
                {
                    message = "no valid tether block was found near the copy.";
                    return false;
                }
                positioning = DecorationEditMath.Snap(center - ToVector3(tether));
            }

            return DecorationEditSnapshot.TryCreatePortable(
                tether,
                positioning,
                source.Scaling,
                orientation,
                source.MeshGuid,
                source.Color,
                source.HideOriginalMesh,
                source.MaterialReplacement,
                out snapshot,
                out message);
        }

        private static Vector3 WorkspaceAxisVector(DecorationLayoutAxis axis)
        {
            switch (axis)
            {
                case DecorationLayoutAxis.X:
                    return Vector3.right;
                case DecorationLayoutAxis.Y:
                    return Vector3.up;
                default:
                    return Vector3.forward;
            }
        }

        private static bool TryParseWorkspaceVector(string[] text, out Vector3 value)
        {
            value = Vector3.zero;
            return text != null && text.Length >= 3 &&
                   FlexibleFloatParser.TryParse(text[0], out value.x) &&
                   FlexibleFloatParser.TryParse(text[1], out value.y) &&
                   FlexibleFloatParser.TryParse(text[2], out value.z) &&
                   DecorationEditMath.IsFinite(value);
        }

        private void DrawWorkspaceLayerTools()
        {
            GUILayout.Label("Layers, folders, tags, visibility, and edit locks", DecorationEditorTheme.SubHeader);
            GUILayout.BeginHorizontal();
            _workspaceLayerName = GUILayout.TextField(
                _workspaceLayerName ?? string.Empty,
                DecorationEditorTheme.TextField);
            WorkspaceButton("New", "Create a named persistent layer.", CreateWorkspaceLayer);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Folder", DecorationEditorTheme.Mini, GUILayout.Width(EsuHudLayout.Scale(42f)));
            _workspaceFolderName = GUILayout.TextField(
                _workspaceFolderName ?? string.Empty,
                DecorationEditorTheme.TextField);
            if (GUILayout.Button(
                    new GUIContent(
                        "Set folder",
                        "Assign the selected layer to this persistent named folder. Leave empty to remove it from a folder."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(66f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                SetWorkspaceLayerFolder();
            }
            GUILayout.EndHorizontal();

            IReadOnlyList<DecorationLayerDefinition> layers = _workspaceLayers.Layers;
            foreach (DecorationLayerDefinition layer in layers)
            {
                bool selected = string.Equals(
                    _workspaceSelectedLayer,
                    layer.Name,
                    StringComparison.OrdinalIgnoreCase);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(
                        new GUIContent(
                            (layer.Visible ? "[V] " : "[-] ") +
                            (layer.Locked ? "[L] " : string.Empty) +
                            (string.IsNullOrEmpty(layer.Folder)
                                ? string.Empty
                                : "[" + layer.Folder + "] ") +
                            layer.Name,
                            "Select this layer."),
                        selected ? DecorationEditorTheme.RowSelected : DecorationEditorTheme.Row,
                        GUILayout.Height(EsuHudLayout.Scale(22f))))
                {
                    _workspaceSelectedLayer = layer.Name;
                    _workspaceLayerName = layer.Name;
                    _workspaceFolderName = layer.Folder ?? string.Empty;
                }
                GUILayout.EndHorizontal();
            }
            if (layers.Count == 0)
                GUILayout.Label("Create a layer, then assign the current selection.", DecorationEditorTheme.MiniWrap);

            GUILayout.BeginHorizontal();
            WorkspaceButton("Assign", "Assign the complete selection to the selected layer.", AssignWorkspaceLayer);
            WorkspaceButton("Select", "Select every visible member of the selected layer.", SelectWorkspaceLayerMembers);
            WorkspaceButton("Hide/show", "Toggle selected layer viewport visibility.", ToggleWorkspaceLayerVisibility);
            WorkspaceButton("Isolate", "Show only the selected layer; click again to show all.", ToggleWorkspaceLayerIsolation);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            WorkspaceButton("Layer lock", "Toggle the persistent edit lock for the selected layer.", ToggleWorkspaceLayerLock);
            WorkspaceButton("Lock objects", "Lock each selected decoration independently of its layer.", () => SetWorkspaceObjectLock(true));
            WorkspaceButton("Unlock", "Remove independent locks from selected decorations.", () => SetWorkspaceObjectLock(false));
            WorkspaceButton("Delete layer", "Delete the selected layer metadata; decorations are not deleted.", DeleteWorkspaceLayer);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _workspaceTagsText = GUILayout.TextField(
                _workspaceTagsText ?? string.Empty,
                DecorationEditorTheme.TextField);
            if (GUILayout.Button(
                    new GUIContent("Set tags", "Set comma-separated persistent tags on the selected decorations."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f))))
            {
                if (_workspaceLayers.TrySetObjectTags(
                        WorkspaceSelectedObjectKeys(),
                        ParseWorkspaceTags(),
                        out string tagMessage))
                    RefreshDecorationCache(force: true);
                InfoStore.Add(tagMessage);
            }
            GUILayout.EndHorizontal();

            if (WorkspaceHasLockedSelection())
                GUILayout.Label("LOCKED: transform, paint, paste, duplicate, and delete are blocked until unlocked.", DecorationEditorTheme.MiniWrap);
        }

        private void CreateWorkspaceLayer()
        {
            if (_workspaceLayers.TryCreateLayer(
                    _workspaceLayerName,
                    _workspaceFolderName,
                    out string message))
                _workspaceSelectedLayer = (_workspaceLayerName ?? string.Empty).Trim();
            InfoStore.Add(message);
        }

        private void SetWorkspaceLayerFolder()
        {
            if (_workspaceLayers.TrySetLayerFolder(
                    _workspaceSelectedLayer,
                    _workspaceFolderName,
                    out string message))
            {
                RefreshDecorationCache(force: true);
            }
            InfoStore.Add(message);
        }

        private void AssignWorkspaceLayer()
        {
            if (_workspaceLayers.TryAssign(
                    WorkspaceSelectedObjectKeys(),
                    _workspaceSelectedLayer,
                    out string message))
            {
                RefreshWorkspaceLayerVisibility(force: true);
                RefreshDecorationCache(force: true);
            }
            InfoStore.Add(message);
        }

        private void SelectWorkspaceLayerMembers()
        {
            if (string.IsNullOrWhiteSpace(_workspaceSelectedLayer))
            {
                InfoStore.Add("Select a layer first.");
                return;
            }
            SelectWorkspaceMatches(
                decoration => string.Equals(
                    _workspaceLayers.LayerFor(WorkspaceObjectKey(decoration)),
                    _workspaceSelectedLayer,
                    StringComparison.OrdinalIgnoreCase),
                "layer");
        }

        private void ToggleWorkspaceLayerVisibility()
        {
            DecorationLayerDefinition layer = SelectedWorkspaceLayer();
            if (layer == null)
            {
                InfoStore.Add("Select a layer first.");
                return;
            }
            _workspaceLayers.TrySetLayerState(layer.Name, !layer.Visible, null, out string message);
            RefreshWorkspaceLayerVisibility(force: true);
            RefreshDecorationCache(force: true);
            InfoStore.Add(message);
        }

        private void ToggleWorkspaceLayerLock()
        {
            DecorationLayerDefinition layer = SelectedWorkspaceLayer();
            if (layer == null)
            {
                InfoStore.Add("Select a layer first.");
                return;
            }
            _workspaceLayers.TrySetLayerState(layer.Name, null, !layer.Locked, out string message);
            InfoStore.Add(message);
        }

        private void ToggleWorkspaceLayerIsolation()
        {
            if (string.IsNullOrWhiteSpace(_workspaceSelectedLayer))
            {
                InfoStore.Add("Select a layer first.");
                return;
            }
            string isolate = string.Equals(
                _workspaceLayers.IsolatedLayer,
                _workspaceSelectedLayer,
                StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : _workspaceSelectedLayer;
            _workspaceLayers.TrySetIsolatedLayer(isolate, out string message);
            RefreshWorkspaceLayerVisibility(force: true);
            RefreshDecorationCache(force: true);
            InfoStore.Add(message);
        }

        private void SetWorkspaceObjectLock(bool locked)
        {
            _workspaceLayers.TrySetObjectLock(
                WorkspaceSelectedObjectKeys(),
                locked,
                out string message);
            InfoStore.Add(message);
        }

        private void DeleteWorkspaceLayer()
        {
            if (_workspaceLayers.TryDeleteLayer(_workspaceSelectedLayer, out string message))
            {
                _workspaceSelectedLayer = string.Empty;
                RefreshWorkspaceLayerVisibility(force: true);
                RefreshDecorationCache(force: true);
            }
            InfoStore.Add(message);
        }

        private DecorationLayerDefinition SelectedWorkspaceLayer() =>
            _workspaceLayers.Layers.FirstOrDefault(layer =>
                string.Equals(layer.Name, _workspaceSelectedLayer, StringComparison.OrdinalIgnoreCase));

        private string[] ParseWorkspaceTags() =>
            (_workspaceTagsText ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0)
                .ToArray();

        private IEnumerable<string> WorkspaceSelectedObjectKeys() =>
            CurrentPrimarySelectionDecorations().Select(WorkspaceObjectKey);

        private string WorkspaceObjectKey(Decoration decoration) =>
            DecorationWorkspaceObjectIdentity.Key(
                WorkspaceConstructFor(decoration),
                decoration);

        private AllConstruct WorkspaceConstructFor(Decoration decoration)
        {
            if (decoration != null)
            {
                AllConstruct managerOwner = _selectionOccluderConstructs.FirstOrDefault(construct =>
                    construct != null &&
                    ReferenceEquals(construct.Decorations, decoration.OurManager));
                if (managerOwner != null)
                    return managerOwner;

                OutlinerRow row = _outlinerRows.FirstOrDefault(candidate =>
                    candidate != null &&
                    candidate.Kind == DecorationOutlinerRowKind.Decoration &&
                    ReferenceEquals(candidate.Decoration, decoration));
                if (row?.Construct != null)
                    return row.Construct;
            }

            return _selectedConstruct;
        }

        private bool TryGetSingleConstructWorkspaceSelection(
            string operation,
            out List<Decoration> decorations,
            out AllConstruct construct,
            out string message)
        {
            decorations = CurrentPrimarySelectionDecorations();
            construct = null;
            if (_selected == null ||
                _selected.IsDeleted ||
                _selectedConstruct == null ||
                decorations.Count == 0)
            {
                message = "Select decorations before using " + operation + ".";
                return false;
            }

            if (!DecorationWorkspaceBulkTools.TryResolveSingleFrame(
                    decorations.Select(WorkspaceConstructFor),
                    out construct) ||
                construct == null ||
                !ReferenceEquals(construct, _selectedConstruct))
            {
                message = operation +
                          " requires every selected decoration to be on one construct. " +
                          "Narrow the Outliner filter to Main construct or one Subconstruct, then select again.";
                construct = null;
                return false;
            }

            message = operation + " selection uses one construct-local coordinate frame.";
            return true;
        }

        private bool WorkspaceSelectionSpansConstructs()
        {
            List<Decoration> decorations = CurrentPrimarySelectionDecorations();
            return decorations.Count > 1 &&
                   !DecorationWorkspaceBulkTools.TryResolveSingleFrame(
                       decorations.Select(decoration => decoration.OurManager),
                       out AllConstructDecorations _);
        }

        private bool WorkspaceDecorationLocked(Decoration decoration) =>
            decoration != null && _workspaceLayers.IsLocked(WorkspaceObjectKey(decoration));

        private bool WorkspaceHasLockedSelection() =>
            CurrentPrimarySelectionDecorations().Any(WorkspaceDecorationLocked);

        private bool WorkspaceDecorationVisible(Decoration decoration, AllConstruct construct) =>
            decoration != null && _workspaceLayers.IsVisible(
                DecorationWorkspaceObjectIdentity.Key(construct, decoration));

        private void RefreshWorkspaceLayerVisibility(bool force)
        {
            float now = Time.unscaledTime;
            if (!force && now < _workspaceNextVisibilityRefresh)
                return;
            _workspaceNextVisibilityRefresh = now + 0.5f;

            MainConstruct main;
            try { main = _build.GetCC(); }
            catch { main = null; }
            if (main == null)
                return;
            var constructs = new List<AllConstruct>();
            try { main.AllBasicsRestricted.GetAllConstructsBelowUsAndIncludingUs(constructs); }
            catch
            {
                AllConstruct focused = FocusedConstruct();
                if (focused != null)
                    constructs.Add(focused);
            }
            foreach (AllConstruct construct in constructs)
            {
                if (construct?.Decorations is not AllConstructDecorations manager)
                    continue;
                foreach (Decoration decoration in manager.DecorationList)
                {
                    if (decoration == null || decoration.IsDeleted)
                        continue;
                    bool hidden = !WorkspaceDecorationVisible(decoration, construct);
                    DecorationLayerVisibilityBridge.SetHidden(decoration, hidden);
                }
            }
        }

        private void DrawWorkspaceMeasurements()
        {
            GUILayout.Label("Ruler / angle / clearance", DecorationEditorTheme.SubHeader);
            if (!TryGetSingleConstructWorkspaceSelection(
                    "live ruler",
                    out List<Decoration> selection,
                    out AllConstruct _,
                    out string frameMessage))
            {
                GUILayout.Label(frameMessage, DecorationEditorTheme.MiniWrap);
                return;
            }
            if (selection.Count < 2)
            {
                GUILayout.Label("Select at least two decorations for live measurements.", DecorationEditorTheme.MiniWrap);
                return;
            }

            Decoration other = selection.First(decoration => !ReferenceEquals(decoration, _selected));
            if (!TryCreateWorkspaceLayoutItem(
                    _selected,
                    "ruler-primary",
                    out DecorationLayoutItem first,
                    out string firstMessage))
            {
                GUILayout.Label(firstMessage, DecorationEditorTheme.MiniWrap);
                return;
            }
            if (!TryCreateWorkspaceLayoutItem(
                    other,
                    "ruler-secondary",
                    out DecorationLayoutItem second,
                    out string secondMessage))
            {
                GUILayout.Label(secondMessage, DecorationEditorTheme.MiniWrap);
                return;
            }
            if (!DecorationPrecisionSnapTools.TryMeasure(
                    first,
                    second,
                    out DecorationPrecisionMeasurement measurement,
                    out string measureMessage))
            {
                GUILayout.Label(measureMessage, DecorationEditorTheme.MiniWrap);
                return;
            }

            float angle = Quaternion.Angle(
                Quaternion.Euler(_selected.Orientation.Us),
                Quaternion.Euler(other.Orientation.Us));
            GUILayout.Label(
                "Bounds-center distance " + measurement.CenterDistance.ToString("0.#####", CultureInfo.InvariantCulture) +
                "m | Angle " + angle.ToString("0.###", CultureInfo.InvariantCulture) +
                " deg | Clearance " + measurement.BoundsClearance.ToString("0.#####", CultureInfo.InvariantCulture) + "m",
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Delta X " + measurement.Delta.x.ToString("0.#####", CultureInfo.InvariantCulture) +
                " | Y " + measurement.Delta.y.ToString("0.#####", CultureInfo.InvariantCulture) +
                " | Z " + measurement.Delta.z.ToString("0.#####", CultureInfo.InvariantCulture) +
                " | axis clearance " +
                measurement.ClearanceByAxis.x.ToString("0.#####", CultureInfo.InvariantCulture) + ", " +
                measurement.ClearanceByAxis.y.ToString("0.#####", CultureInfo.InvariantCulture) + ", " +
                measurement.ClearanceByAxis.z.ToString("0.#####", CultureInfo.InvariantCulture),
                DecorationEditorTheme.MiniWrap);
        }

        private bool ApplyWorkspaceBatch(
            string label,
            IReadOnlyList<Decoration> decorations,
            Action<Decoration, DecorationEditSnapshot> apply)
        {
            Decoration[] targets = (decorations ?? Array.Empty<Decoration>())
                .Where(decoration => decoration != null && !decoration.IsDeleted)
                .Distinct()
                .ToArray();
            if (targets.Length == 0 || _selectedConstruct == null)
            {
                InfoStore.Add("Select at least one decoration first.");
                return false;
            }
            if (targets.Any(WorkspaceDecorationLocked))
            {
                InfoStore.Add("The operation was not started because the selection contains locked decorations.");
                return false;
            }

            var before = targets.Select(decoration => new DecorationEditSnapshot(decoration)).ToArray();
            try
            {
                for (int index = 0; index < targets.Length; index++)
                {
                    apply(targets[index], before[index]);
                    DecorationEditSnapshot after = new DecorationEditSnapshot(targets[index]);
                    if (!after.HasFiniteTransform || !IsValidScale(after.Scaling) ||
                        after.Color < 0 || after.Color > 31)
                        throw new InvalidOperationException("The operation produced invalid decoration data.");
                    targets[index].Changed();
                }
            }
            catch (Exception exception)
            {
                for (int index = 0; index < targets.Length; index++)
                    before[index].TryRestore(targets[index]);
                InfoStore.Add(label + " failed and was rolled back: " + exception.Message);
                return false;
            }

            RecordWorkspaceSnapshotBatch(label, targets, before);
            InfoStore.Add(label + " applied to " + targets.Length.ToString("N0", CultureInfo.InvariantCulture) +
                          " decoration(s).");
            return true;
        }

        private void RecordWorkspaceSnapshotBatch(
            string label,
            IReadOnlyList<Decoration> decorations,
            IReadOnlyList<DecorationEditSnapshot> before)
        {
            var changedDecorations = new List<Decoration>();
            var changedConstructs = new List<AllConstruct>();
            var changedBefore = new List<DecorationEditSnapshot>();
            var changedAfter = new List<DecorationEditSnapshot>();
            int primaryIndex = -1;
            for (int index = 0; index < decorations.Count; index++)
            {
                Decoration decoration = decorations[index];
                DecorationEditSnapshot original = before[index];
                if (original.Matches(decoration))
                    continue;
                if (ReferenceEquals(decoration, _selected))
                    primaryIndex = changedDecorations.Count;
                _transactions.TrackEdit(decoration, original);
                changedDecorations.Add(decoration);
                changedConstructs.Add(WorkspaceConstructFor(decoration));
                changedBefore.Add(original);
                changedAfter.Add(new DecorationEditSnapshot(decoration));
            }
            if (changedDecorations.Count == 0)
            {
                InfoStore.Add("The selected decorations already have the requested layout/settings.");
                return;
            }
            if (primaryIndex < 0)
                primaryIndex = 0;
            _history.Record(new DecorationSnapshotBatchCommand(
                label,
                changedConstructs.ToArray(),
                changedDecorations.ToArray(),
                changedBefore.ToArray(),
                changedAfter.ToArray(),
                primaryIndex));
            _dirty = true;
            _snapshot = _selected != null && !_selected.IsDeleted
                ? _transactions.GetOriginal(_selected) ?? new DecorationEditSnapshot(_selected)
                : null;
            ResetInspectorFields();
            RefreshDecorationCache(force: true);
            RefreshForecast(force: true);
        }
    }
}
