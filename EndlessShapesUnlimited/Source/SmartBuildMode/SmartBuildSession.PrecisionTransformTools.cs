using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed partial class SmartBuildSession
    {
        private SmartBuildPrecisionPivotMode _precisionPivotMode =
            SmartBuildPrecisionPivotMode.Primary;
        private string _precisionPivotXText = "0";
        private string _precisionPivotYText = "0";
        private string _precisionPivotZText = "0";
        private bool _precisionCustomPivotInitialized;
        private string _precisionDimensionXText = "1";
        private string _precisionDimensionYText = "1";
        private string _precisionDimensionZText = "1";
        private bool _precisionDimensionTextInitialized;
        private int _precisionDimensionSyncedPieceId = -1;
        private Vector3i _precisionDimensionSyncedSize;
        private bool _precisionSnapFaceArmed;

        private void DrawPrecisionSelectionMeasurements()
        {
            if (!SmartBuildPrecisionTools.TryMeasureSelection(
                    PrecisionSelectionItems(),
                    _draft?.Id ?? -1,
                    out SmartBuildPrecisionSelectionMetrics metrics,
                    out _))
            {
                return;
            }

            GUILayout.Label(
                "Bounds " + FormatCell(metrics.Bounds.Min) + " -> " +
                FormatCell(metrics.Bounds.Max),
                DecorationEditorTheme.MiniWrap);
            GUILayout.Label(
                "Span " + FormatCell(metrics.Span) + " occupied cell(s)",
                DecorationEditorTheme.MiniWrap);
            if (metrics.HasMeasuredGap)
            {
                GUILayout.Label(
                    "Clear gap #" + metrics.PrimaryId.ToString(CultureInfo.InvariantCulture) +
                    " to nearest #" + metrics.NearestPieceId.ToString(CultureInfo.InvariantCulture) +
                    ": X " + metrics.MeasuredClearGapCells.x.ToString(CultureInfo.InvariantCulture) +
                    " | Y " + metrics.MeasuredClearGapCells.y.ToString(CultureInfo.InvariantCulture) +
                    " | Z " + metrics.MeasuredClearGapCells.z.ToString(CultureInfo.InvariantCulture),
                    DecorationEditorTheme.MiniWrap);
            }
        }

        private void DrawPrecisionPrimitiveDimensionControls()
        {
            if (!TryGetSingleSelectedPrimitive(out SmartBuildPiece piece, out _))
                return;

            EnsurePrecisionDimensionTextSynced(piece);
            GUILayout.Space(EsuHudLayout.Scale(2f));
            GUILayout.Label(
                SmartBuildPrecisionTools.PrimitiveDimensionLabel(piece) + " dimensions",
                DecorationEditorTheme.MiniWrap);
            GUILayout.BeginHorizontal();
            DrawPrecisionCoordinateField("X", ref _precisionDimensionXText);
            DrawPrecisionCoordinateField("Y", ref _precisionDimensionYText);
            DrawPrecisionCoordinateField("Z", ref _precisionDimensionZText);
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Resize",
                        "Set exact shape-specific X/Y/Z bounds while anchoring the primitive's minimum craft-local faces. Unsupported shape dimensions fail without mutation."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(58f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                ApplyPrecisionPrimitiveDimensions(piece);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawPrecisionPivotRotationControls()
        {
            GUILayout.Space(EsuHudLayout.Scale(2f));
            GUILayout.Label("Pivot / quarter turn", DecorationEditorTheme.MiniWrap);
            GUILayout.BeginHorizontal();
            DrawPrecisionPivotButton("Primary", SmartBuildPrecisionPivotMode.Primary);
            DrawPrecisionPivotButton("Bounds", SmartBuildPrecisionPivotMode.SelectionBoundsCenter);
            DrawPrecisionPivotButton("Custom", SmartBuildPrecisionPivotMode.CustomCraftLocal);
            GUILayout.EndHorizontal();

            if (_precisionPivotMode == SmartBuildPrecisionPivotMode.CustomCraftLocal)
            {
                EnsurePrecisionCustomPivotInitialized();
                GUILayout.BeginHorizontal();
                DrawPrecisionCoordinateField("X", ref _precisionPivotXText);
                DrawPrecisionCoordinateField("Y", ref _precisionPivotYText);
                DrawPrecisionCoordinateField("Z", ref _precisionPivotZText);
                GUILayout.EndHorizontal();
            }

            if (TryResolveCurrentPrecisionPivot(out Vector3i pivot, out _))
            {
                GUILayout.Label(
                    "Pivot cell " + FormatCell(pivot),
                    DecorationEditorTheme.MiniWrap);
            }
            else
            {
                GUILayout.Label("Pivot cell: invalid input", DecorationEditorTheme.MiniWrap);
            }

            bool previous = GUI.enabled;
            bool primitiveGroup = PrecisionSelectionContainsOnlyPrimitiveNodes();
            GUI.enabled = previous && primitiveGroup;
            GUILayout.BeginHorizontal();
            DrawPrecisionRotateButton("X -90", DecorationEditAxis.X, -1);
            DrawPrecisionRotateButton("X +90", DecorationEditAxis.X, 1);
            DrawPrecisionRotateButton("Y -90", DecorationEditAxis.Y, -1);
            DrawPrecisionRotateButton("Y +90", DecorationEditAxis.Y, 1);
            DrawPrecisionRotateButton("Z -90", DecorationEditAxis.Z, -1);
            DrawPrecisionRotateButton("Z +90", DecorationEditAxis.Z, 1);
            GUILayout.EndHorizontal();
            GUI.enabled = previous;

            if (SmartGUILayoutButton(
                    new GUIContent(
                        _precisionSnapFaceArmed ? "Picking craft face..." : "Snap group to craft face",
                        "Arm one world click. The selected occupied bound is moved flush into the outside cell layer of the pointed craft face."),
                    DecorationEditorTheme.ToolButton(_precisionSnapFaceArmed),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                TogglePrecisionSnapFaceMode();
            }
        }

        private void DrawPrecisionPivotButton(
            string label,
            SmartBuildPrecisionPivotMode mode)
        {
            if (SmartGUILayoutButton(
                    new GUIContent(
                        label,
                        mode == SmartBuildPrecisionPivotMode.Primary
                            ? "Rotate around the primary piece's center pivot cell."
                            : mode == SmartBuildPrecisionPivotMode.SelectionBoundsCenter
                                ? "Rotate around the whole selection bounds center, rounded to the nearest exact craft grid cell."
                                : "Rotate around a typed craft-local pivot cell."),
                    DecorationEditorTheme.ToolButton(_precisionPivotMode == mode),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                SetPrecisionPivotMode(mode);
            }
        }

        private void DrawPrecisionRotateButton(
            string label,
            DecorationEditAxis axis,
            int quarterTurns)
        {
            if (SmartGUILayoutButton(
                    new GUIContent(
                        label,
                        "Atomically rotate every selected primitive " +
                        (quarterTurns > 0 ? "+90" : "-90") +
                        " degrees around craft-local " + axis + " and the chosen pivot."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                RotatePrecisionSelection(axis, quarterTurns);
            }
        }

        private void SetPrecisionPivotMode(SmartBuildPrecisionPivotMode mode)
        {
            if (_precisionPivotMode == mode)
                return;

            _precisionPivotMode = mode;
            if (mode == SmartBuildPrecisionPivotMode.CustomCraftLocal)
            {
                _precisionCustomPivotInitialized = false;
                EnsurePrecisionCustomPivotInitialized();
            }
            try { GUIUtility.keyboardControl = 0; }
            catch { }
        }

        private void EnsurePrecisionCustomPivotInitialized()
        {
            if (_precisionCustomPivotInitialized)
                return;

            if (!SmartBuildPrecisionTools.TryResolvePivot(
                    PrecisionSelectionItems(),
                    _draft?.Id ?? -1,
                    SmartBuildPrecisionPivotMode.SelectionBoundsCenter,
                    default,
                    out Vector3i pivot,
                    out _))
            {
                pivot = _draft?.Origin ?? new Vector3i(0, 0, 0);
            }

            _precisionPivotXText = pivot.x.ToString(CultureInfo.InvariantCulture);
            _precisionPivotYText = pivot.y.ToString(CultureInfo.InvariantCulture);
            _precisionPivotZText = pivot.z.ToString(CultureInfo.InvariantCulture);
            _precisionCustomPivotInitialized = true;
        }

        private bool TryResolveCurrentPrecisionPivot(
            out Vector3i pivot,
            out string message)
        {
            Vector3i custom = default;
            if (_precisionPivotMode == SmartBuildPrecisionPivotMode.CustomCraftLocal &&
                !SmartBuildPrecisionTools.TryParseCellVector(
                    _precisionPivotXText,
                    _precisionPivotYText,
                    _precisionPivotZText,
                    out custom,
                    out message))
            {
                pivot = default;
                return false;
            }

            return SmartBuildPrecisionTools.TryResolvePivot(
                PrecisionSelectionItems(),
                _draft?.Id ?? -1,
                _precisionPivotMode,
                custom,
                out pivot,
                out message);
        }

        private void RotatePrecisionSelection(
            DecorationEditAxis axis,
            int quarterTurns)
        {
            if (!PrecisionSelectionContainsOnlyPrimitiveNodes())
            {
                InfoStore.Add("Bake editable pattern or region nodes before precision group rotation.");
                return;
            }
            if (!TryResolveCurrentPrecisionPivot(out Vector3i pivot, out string message))
            {
                InfoStore.Add(message);
                return;
            }

            SmartBuildPiece[] selected = _scene.SelectedPieces.ToArray();
            if (!SmartBuildPrecisionTools.TryRotatePieces(
                    selected,
                    axis,
                    quarterTurns,
                    pivot,
                    out IReadOnlyList<SmartBuildPiece> rotated,
                    out message))
            {
                InfoStore.Add(message);
                return;
            }

            ApplyPreparedPrecisionPieces(
                rotated,
                selected.Length,
                SmartBuildTool.Rotate,
                message);
        }

        private void EnsurePrecisionDimensionTextSynced(SmartBuildPiece piece)
        {
            if (piece == null)
                return;

            Vector3i size = piece.GroupScaleBounds.Size;
            if (_precisionDimensionTextInitialized &&
                _precisionDimensionSyncedPieceId == piece.Id &&
                _precisionDimensionSyncedSize.Equals(size))
            {
                return;
            }

            _precisionDimensionXText = size.x.ToString(CultureInfo.InvariantCulture);
            _precisionDimensionYText = size.y.ToString(CultureInfo.InvariantCulture);
            _precisionDimensionZText = size.z.ToString(CultureInfo.InvariantCulture);
            _precisionDimensionSyncedPieceId = piece.Id;
            _precisionDimensionSyncedSize = size;
            _precisionDimensionTextInitialized = true;
        }

        private void ApplyPrecisionPrimitiveDimensions(SmartBuildPiece piece)
        {
            if (!TryGetSingleSelectedPrimitive(out SmartBuildPiece selected, out string reason) ||
                selected.Id != piece?.Id)
            {
                InfoStore.Add(reason ?? "The primitive selection changed; enter dimensions again.");
                return;
            }
            if (!SmartBuildPrecisionTools.TryParseCellVector(
                    _precisionDimensionXText,
                    _precisionDimensionYText,
                    _precisionDimensionZText,
                    out Vector3i dimensions,
                    out string message))
            {
                InfoStore.Add(message);
                return;
            }
            if (!SmartBuildPrecisionTools.TryResizePrimitive(
                    selected,
                    dimensions,
                    out SmartBuildPiece resized,
                    out message))
            {
                InfoStore.Add(message);
                return;
            }

            ApplyPreparedPrecisionPieces(
                new[] { resized },
                expectedSelectionCount: 1,
                SmartBuildTool.Scale,
                message);
        }

        private bool TryGetSingleSelectedPrimitive(
            out SmartBuildPiece piece,
            out string reason)
        {
            piece = null;
            if (_scene?.SelectionCount != 1 || _scene.SelectedPiece == null)
            {
                reason = "Select exactly one primitive Smart Builder piece to edit dimensions.";
                return false;
            }
            if (_scene.SelectedNode?.Kind != SmartBuildSceneNodeKind.Primitive)
            {
                reason = "Bake the selected editable node before editing primitive dimensions.";
                return false;
            }

            piece = _scene.SelectedPiece;
            reason = null;
            return true;
        }

        private bool PrecisionSelectionContainsOnlyPrimitiveNodes()
        {
            if (_scene == null || _scene.SelectionCount <= 0)
                return false;

            var selectedIds = new HashSet<int>(_scene.SelectedPieceIds);
            ISmartBuildSceneNode[] selectedNodes = _scene.Nodes
                .Where(node => node != null && selectedIds.Contains(node.Id))
                .ToArray();
            return selectedNodes.Length == selectedIds.Count &&
                   selectedNodes.All(node => node.Kind == SmartBuildSceneNodeKind.Primitive);
        }

        private void ApplyPreparedPrecisionPieces(
            IReadOnlyList<SmartBuildPiece> preparedPieces,
            int expectedSelectionCount,
            SmartBuildTool resultingTool,
            string successMessage)
        {
            SmartBuildPiece[] selected = (_scene?.SelectedPieces ?? Array.Empty<SmartBuildPiece>()).ToArray();
            SmartBuildPiece[] prepared = (preparedPieces ?? Array.Empty<SmartBuildPiece>())
                .Where(piece => piece != null)
                .ToArray();
            if (selected.Length == 0 || selected.Length != expectedSelectionCount)
            {
                InfoStore.Add("The Smart Builder precision selection changed; prepare the operation again.");
                return;
            }

            var liveById = selected.ToDictionary(piece => piece.Id);
            if (prepared.Length == 0 ||
                prepared.Select(piece => piece.Id).Distinct().Count() != prepared.Length ||
                prepared.Any(piece =>
                    !liveById.ContainsKey(piece.Id) ||
                    !SmartBuildPrecisionTools.IsSafeBounds(piece.Bounds)))
            {
                InfoStore.Add("The prepared precision transform does not match the live selection.");
                return;
            }

            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            foreach (SmartBuildPiece piece in prepared)
                liveById[piece.Id].CopyFrom(piece);
            _scene.MarkGeometryChanged();
            PushSceneSnapshot(before);
            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
            _tool = resultingTool;
            _precisionPositionTextInitialized = false;
            _precisionDimensionTextInitialized = false;
            RebuildPlan();
            RefreshApplyCancelAttention();
            InfoStore.Add(successMessage ?? "Smart Builder precision transform applied.");
        }

        private void TogglePrecisionSnapFaceMode()
        {
            if (_precisionSnapFaceArmed)
            {
                CancelPrecisionSnapFaceMode(notify: true);
                return;
            }
            if (_scene?.SelectionCount <= 0 || _scene.Construct == null)
            {
                InfoStore.Add("Select a Smart Builder group before snapping to a craft face.");
                return;
            }

            _precisionSnapFaceArmed = true;
            _smartScenePresetTargetArmed = false;
            _blockEyedropperArmed = false;
            _contextMenuOpen = false;
            try { GUIUtility.keyboardControl = 0; }
            catch { }
            ClaimPrecisionShortcutInput();
            InfoStore.Add("Click a face on the same craft to snap the selected group; right-click or Escape cancels.");
        }

        private bool TryHandlePrecisionPointerInput()
        {
            if (!_precisionSnapFaceArmed)
                return false;

            if (Input.GetMouseButtonDown(1))
                CancelPrecisionSnapFaceMode(notify: true);
            else if (Input.GetMouseButtonDown(0))
                TrySnapPrecisionSelectionToPointedFace();

            ClaimPrecisionShortcutInput();
            return true;
        }

        private bool TrySnapPrecisionSelectionToPointedFace()
        {
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit) || hit?.Construct == null)
            {
                InfoStore.Add("Point at an existing craft block face on this construct.");
                return false;
            }
            if (_scene?.Construct == null || !ReferenceEquals(hit.Construct, _scene.Construct))
            {
                InfoStore.Add("The pointed face belongs to a different construct.");
                return false;
            }

            Vector3 localNormal = WorldNormalToLocal(hit);
            Vector3i adjacent = AdjacentCellFromSurfaceHit(
                hit.Anchor,
                hit.LocalHit,
                localNormal,
                out SmartBuildAxis axis,
                out int sign);
            if (!SmartBuildPrecisionTools.TrySnapSelectionToFace(
                    PrecisionSelectionItems(),
                    axis,
                    sign,
                    adjacent,
                    out SmartBuildPrecisionLayoutPlan plan,
                    out string message))
            {
                InfoStore.Add(message);
                return false;
            }

            ApplyPrecisionLayoutPlan(plan, message);
            _precisionSnapFaceArmed = false;
            return true;
        }

        private bool CancelPrecisionSnapFaceMode(bool notify)
        {
            if (!_precisionSnapFaceArmed)
                return false;

            _precisionSnapFaceArmed = false;
            if (notify)
                InfoStore.Add("Smart Builder craft-face snap cancelled.");
            return true;
        }

        private void ResetPrecisionTransformState()
        {
            _precisionSnapFaceArmed = false;
            _precisionCustomPivotInitialized = false;
            _precisionDimensionTextInitialized = false;
            _precisionDimensionSyncedPieceId = -1;
        }
    }
}
