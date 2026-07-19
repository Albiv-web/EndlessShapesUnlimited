using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ui.Special.InfoStore;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.SmartBuildMode
{
    internal sealed partial class SmartBuildSession
    {
        private SmartBuildPrecisionPositionMode _precisionPositionMode =
            SmartBuildPrecisionPositionMode.AbsoluteCraftLocal;
        private SmartBuildAxis _precisionLayoutAxis = SmartBuildAxis.X;
        private string _precisionXText = "0";
        private string _precisionYText = "0";
        private string _precisionZText = "0";
        private bool _precisionPositionTextInitialized;
        private int _precisionSyncedPieceId = -1;
        private Vector3i _precisionSyncedOrigin;
        private SmartBuildPrecisionPositionMode _precisionSyncedMode =
            SmartBuildPrecisionPositionMode.AbsoluteCraftLocal;
        private SmartBuildSessionClipboard _precisionClipboard;
        private int _precisionClipboardPasteCount;

        private sealed class SmartBuildSessionClipboard
        {
            internal SmartBuildSessionClipboard(
                AllConstruct construct,
                IEnumerable<SmartBuildPiece> pieces,
                int primarySourceId,
                int widthX)
            {
                Construct = construct;
                Pieces = (pieces ?? Array.Empty<SmartBuildPiece>()).ToArray();
                PrimarySourceId = primarySourceId;
                WidthX = Math.Max(1, widthX);
            }

            internal AllConstruct Construct { get; }

            internal IReadOnlyList<SmartBuildPiece> Pieces { get; }

            internal int PrimarySourceId { get; }

            internal int WidthX { get; }
        }

        private void DrawPrecisionTransformLayoutControls()
        {
            if (_scene?.SelectionCount <= 0 || _draft == null)
                return;

            EnsurePrecisionPositionTextSynced();
            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Transform / layout", DecorationEditorTheme.SubHeader);
            GUILayout.Label(
                "Craft-local grid cells | primary anchor #" +
                _draft.Id.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.MiniWrap);
            DrawPrecisionSelectionMeasurements();

            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Absolute",
                        "Set the primary piece's absolute construct-local origin and preserve every selected group offset."),
                    DecorationEditorTheme.ToolButton(
                        _precisionPositionMode == SmartBuildPrecisionPositionMode.AbsoluteCraftLocal),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                SetPrecisionPositionMode(SmartBuildPrecisionPositionMode.AbsoluteCraftLocal);
            }
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Relative",
                        "Move every selected piece by an exact construct-local X/Y/Z cell delta."),
                    DecorationEditorTheme.ToolButton(
                        _precisionPositionMode == SmartBuildPrecisionPositionMode.RelativeCraftLocal),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                SetPrecisionPositionMode(SmartBuildPrecisionPositionMode.RelativeCraftLocal);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawPrecisionCoordinateField("X", ref _precisionXText);
            DrawPrecisionCoordinateField("Y", ref _precisionYText);
            DrawPrecisionCoordinateField("Z", ref _precisionZText);
            if (SmartGUILayoutButton(
                    new GUIContent(
                        _precisionPositionMode == SmartBuildPrecisionPositionMode.RelativeCraftLocal
                            ? "Move"
                            : "Set",
                        _precisionPositionMode == SmartBuildPrecisionPositionMode.RelativeCraftLocal
                            ? "Apply this exact craft-local delta to the selected group as one undoable edit."
                            : "Move the selected group so the primary anchor has this exact craft-local origin."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(54f)),
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                ApplyPrecisionPositionText();
            }
            GUILayout.EndHorizontal();

            DrawPrecisionPrimitiveDimensionControls();
            DrawPrecisionPivotRotationControls();

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Axis",
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(30f)));
            DrawPrecisionAxisButton(SmartBuildAxis.X);
            DrawPrecisionAxisButton(SmartBuildAxis.Y);
            DrawPrecisionAxisButton(SmartBuildAxis.Z);
            GUILayout.EndHorizontal();

            bool previous = GUI.enabled;
            GUI.enabled = previous && _scene.SelectionCount >= 2;
            GUILayout.BeginHorizontal();
            DrawPrecisionAlignButton("Min", SmartBuildPrecisionAlignment.Minimum);
            DrawPrecisionAlignButton("Center", SmartBuildPrecisionAlignment.Center);
            DrawPrecisionAlignButton("Max", SmartBuildPrecisionAlignment.Maximum);
            GUILayout.EndHorizontal();

            GUI.enabled = previous && _scene.SelectionCount >= 3;
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Distribute equal gaps",
                        "Keep the outer pair fixed and distribute the selected occupied bounds with equal whole-cell gaps on craft-local " +
                        _precisionLayoutAxis + "."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                ApplyPrecisionDistribution();
            }
            GUI.enabled = previous;

            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Copy",
                        "Ctrl+C: copy the exact selected preview group into the Smart Builder session clipboard."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                CopyPrecisionSelection();
            }

            bool canPaste = CanPastePrecisionClipboard();
            GUI.enabled = previous && canPaste;
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Paste +X",
                        "Ctrl+V: paste the session clipboard one copied-group width farther along craft-local +X each time."),
                    canPaste ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                PastePrecisionClipboard();
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
        }

        private static void DrawPrecisionCoordinateField(string label, ref string text)
        {
            GUILayout.Label(
                label,
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(12f)));
            text = GUILayout.TextField(
                text ?? string.Empty,
                DecorationEditorTheme.TextField,
                GUILayout.MinWidth(EsuHudLayout.Scale(38f)),
                GUILayout.Height(EsuHudLayout.Scale(23f)));
            Rect field = GUILayoutUtility.GetLastRect();
            EsuCursorTooltip.Register(field, label + " in whole construct-local grid cells.");
        }

        private void DrawPrecisionAxisButton(SmartBuildAxis axis)
        {
            if (SmartGUILayoutButton(
                    new GUIContent(
                        axis.ToString(),
                        "Use craft-local " + axis + " for alignment and equal-gap distribution."),
                    DecorationEditorTheme.ToolButton(_precisionLayoutAxis == axis),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                _precisionLayoutAxis = axis;
            }
        }

        private void DrawPrecisionAlignButton(
            string label,
            SmartBuildPrecisionAlignment alignment)
        {
            if (SmartGUILayoutButton(
                    new GUIContent(
                        label,
                        "Align each selected occupied bound's " + label.ToLowerInvariant() +
                        " to the primary piece on craft-local " + _precisionLayoutAxis + "."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                ApplyPrecisionAlignment(alignment);
            }
        }

        private void SetPrecisionPositionMode(SmartBuildPrecisionPositionMode mode)
        {
            if (_precisionPositionMode == mode)
                return;

            _precisionPositionMode = mode;
            _precisionPositionTextInitialized = false;
            try { GUIUtility.keyboardControl = 0; }
            catch { }
            EnsurePrecisionPositionTextSynced();
        }

        private void EnsurePrecisionPositionTextSynced()
        {
            if (_draft == null)
                return;

            Vector3i origin = _draft.Origin;
            if (_precisionPositionTextInitialized &&
                _precisionSyncedPieceId == _draft.Id &&
                _precisionSyncedOrigin.Equals(origin) &&
                _precisionSyncedMode == _precisionPositionMode)
            {
                return;
            }

            Vector3i display = _precisionPositionMode == SmartBuildPrecisionPositionMode.RelativeCraftLocal
                ? new Vector3i(0, 0, 0)
                : origin;
            _precisionXText = display.x.ToString(CultureInfo.InvariantCulture);
            _precisionYText = display.y.ToString(CultureInfo.InvariantCulture);
            _precisionZText = display.z.ToString(CultureInfo.InvariantCulture);
            _precisionSyncedPieceId = _draft.Id;
            _precisionSyncedOrigin = origin;
            _precisionSyncedMode = _precisionPositionMode;
            _precisionPositionTextInitialized = true;
        }

        private void ApplyPrecisionPositionText()
        {
            if (!SmartBuildPrecisionTools.TryParseCellVector(
                    _precisionXText,
                    _precisionYText,
                    _precisionZText,
                    out Vector3i value,
                    out string message))
            {
                InfoStore.Add(message);
                return;
            }

            if (!SmartBuildPrecisionTools.TryTranslate(
                    PrecisionSelectionItems(),
                    _draft?.Id ?? -1,
                    value,
                    _precisionPositionMode,
                    out SmartBuildPrecisionLayoutPlan plan,
                    out message))
            {
                InfoStore.Add(message);
                return;
            }

            ApplyPrecisionLayoutPlan(plan, message);
        }

        private void ApplyPrecisionAlignment(SmartBuildPrecisionAlignment alignment)
        {
            if (!SmartBuildPrecisionTools.TryAlign(
                    PrecisionSelectionItems(),
                    _draft?.Id ?? -1,
                    _precisionLayoutAxis,
                    alignment,
                    out SmartBuildPrecisionLayoutPlan plan,
                    out string message))
            {
                InfoStore.Add(message);
                return;
            }

            ApplyPrecisionLayoutPlan(plan, message);
        }

        private void ApplyPrecisionDistribution()
        {
            if (!SmartBuildPrecisionTools.TryDistributeEqualGaps(
                    PrecisionSelectionItems(),
                    _precisionLayoutAxis,
                    out SmartBuildPrecisionLayoutPlan plan,
                    out string message))
            {
                InfoStore.Add(message);
                return;
            }

            ApplyPrecisionLayoutPlan(plan, message);
        }

        private SmartBuildPrecisionItem[] PrecisionSelectionItems() =>
            (_scene?.SelectedPieces ?? Array.Empty<SmartBuildPiece>())
            .Select(piece => new SmartBuildPrecisionItem(piece.Id, piece.Origin, piece.Bounds))
            .ToArray();

        private void ApplyPrecisionLayoutPlan(
            SmartBuildPrecisionLayoutPlan plan,
            string successMessage)
        {
            SmartBuildPiece[] selected = (_scene?.SelectedPieces ?? Array.Empty<SmartBuildPiece>()).ToArray();
            if (plan == null || selected.Length == 0 || plan.SelectionCount != selected.Length)
            {
                InfoStore.Add("The Smart Builder precision selection changed; prepare the operation again.");
                return;
            }

            var liveById = selected.ToDictionary(piece => piece.Id);
            if (plan.Moves.Select(move => move.PieceId).Distinct().Count() != plan.Moves.Count ||
                plan.Moves.Any(move => !liveById.ContainsKey(move.PieceId)))
            {
                InfoStore.Add("The Smart Builder precision plan does not match the live selection.");
                return;
            }

            var staged = new Dictionary<int, SmartBuildPiece>();
            try
            {
                foreach (SmartBuildPrecisionMove move in plan.Moves)
                {
                    SmartBuildPiece clone = liveById[move.PieceId].Clone();
                    clone.MoveBy(move.Delta);
                    if (!SmartBuildPrecisionTools.IsSafeBounds(clone.Bounds))
                    {
                        InfoStore.Add(
                            "The Smart Builder precision transform exceeds the safe craft-local range.");
                        return;
                    }
                    staged.Add(move.PieceId, clone);
                }
            }
            catch (Exception exception)
            {
                InfoStore.Add("The Smart Builder precision transform could not be prepared: " + exception.Message);
                return;
            }

            if (staged.Count == 0)
            {
                InfoStore.Add("The Smart Builder precision operation does not change the selection.");
                return;
            }

            ApplyPreparedPrecisionPieces(
                staged.Values.ToArray(),
                plan.SelectionCount,
                SmartBuildTool.Move,
                successMessage ?? plan.Label + ".");
        }

        private bool TryHandlePrecisionKeyboardShortcuts()
        {
            if (GUIUtility.keyboardControl != 0 ||
                DecoLimitLifter.EsuInputState.IsTextInputActive())
            {
                return false;
            }

            bool control = IsControlHeld();
            bool shift = IsShiftHeld();
            if (control && !shift && Input.GetKeyDown(KeyCode.A))
            {
                ClaimPrecisionShortcutInput();
                BulkSelectAllPieces();
                return true;
            }

            if (control && !shift && Input.GetKeyDown(KeyCode.C))
            {
                ClaimPrecisionShortcutInput();
                CopyPrecisionSelection();
                return true;
            }

            if (control && !shift && Input.GetKeyDown(KeyCode.D))
            {
                ClaimPrecisionShortcutInput();
                if (_scene?.SelectionCount > 0)
                    DuplicateSelectedPiece();
                else
                    InfoStore.Add("Select a Smart Builder preview piece before duplicating it.");
                return true;
            }

            if (control && !shift && Input.GetKeyDown(KeyCode.V))
            {
                ClaimPrecisionShortcutInput();
                PastePrecisionClipboard();
                return true;
            }

            if (!control && Input.GetKeyDown(KeyCode.Delete))
            {
                ClaimPrecisionShortcutInput();
                if (_scene?.SelectionCount > 0)
                    DeleteSelectedPiece();
                else
                    InfoStore.Add("Select a Smart Builder preview piece before deleting it.");
                return true;
            }

            return false;
        }

        internal bool TryHandlePrecisionEscapeShortcut()
        {
            if (!Active)
                return false;

            if (CancelPrecisionSnapFaceMode(notify: true))
            {
                ClaimPrecisionShortcutInput();
                DecoLimitLifter.EsuEscapeCloseGuard.Arm();
                return true;
            }

            if (GUIUtility.keyboardControl != 0 ||
                DecoLimitLifter.EsuInputState.IsTextInputActive())
            {
                try { GUIUtility.keyboardControl = 0; }
                catch { }
                ClaimPrecisionShortcutInput();
                DecoLimitLifter.EsuEscapeCloseGuard.Arm();
                return true;
            }

            if ((_scene?.SelectionCount ?? 0) <= 0)
                return false;

            ClaimPrecisionShortcutInput();
            DecoLimitLifter.EsuEscapeCloseGuard.Arm();
            BulkClearPieceSelection();
            return true;
        }

        private static void ClaimPrecisionShortcutInput()
        {
            SmartBuildInputScope.ClaimBuildInputForFrames();
            SmartBuildInputScope.ClaimCameraInputForFrames();
        }

        private void CopyPrecisionSelection()
        {
            SmartBuildPiece[] selected = (_scene?.SelectedPieces ?? Array.Empty<SmartBuildPiece>()).ToArray();
            if (selected.Length == 0 || _scene?.Construct == null)
            {
                InfoStore.Add("Select at least one Smart Builder preview piece to copy.");
                return;
            }

            SmartBuildPiece[] copies;
            int minimumX;
            int maximumX;
            try
            {
                copies = selected.Select(piece => piece.Clone()).ToArray();
                minimumX = selected.Min(piece => piece.Bounds.Min.x);
                maximumX = selected.Max(piece => piece.Bounds.Max.x);
            }
            catch (Exception exception)
            {
                InfoStore.Add("The Smart Builder selection could not be copied: " + exception.Message);
                return;
            }

            long width = (long)maximumX - minimumX + 1L;
            if (width < 1L || width > int.MaxValue)
            {
                InfoStore.Add("The Smart Builder selection is too wide for the session clipboard.");
                return;
            }

            _precisionClipboard = new SmartBuildSessionClipboard(
                _scene.Construct,
                copies,
                _scene.SelectedPiece?.Id ?? copies[copies.Length - 1].Id,
                (int)width);
            _precisionClipboardPasteCount = 0;
            InfoStore.Add(
                "Copied " + selected.Length.ToString("N0", CultureInfo.InvariantCulture) +
                " Smart Builder piece(s) to the session clipboard.");
        }

        private bool CanPastePrecisionClipboard() =>
            _precisionClipboard != null &&
            _precisionClipboard.Construct != null &&
            _precisionClipboard.Pieces.Count > 0 &&
            (_scene == null || ReferenceEquals(_scene.Construct, _precisionClipboard.Construct));

        private void PastePrecisionClipboard()
        {
            if (_precisionClipboard == null ||
                _precisionClipboard.Construct == null ||
                _precisionClipboard.Pieces.Count == 0)
            {
                InfoStore.Add("Copy a Smart Builder preview group before pasting it.");
                return;
            }

            if (_scene != null &&
                !ReferenceEquals(_scene.Construct, _precisionClipboard.Construct))
            {
                InfoStore.Add("The Smart Builder session clipboard belongs to a different construct.");
                return;
            }

            int existingCount = _scene?.Count ?? 0;
            if (existingCount + _precisionClipboard.Pieces.Count > SmartBuildPieceScene.MaximumScenePieces)
            {
                InfoStore.Add(
                    "Paste would exceed the Smart Builder scene cap of " +
                    SmartBuildPieceScene.MaximumScenePieces.ToString(CultureInfo.InvariantCulture) +
                    " pieces.");
                return;
            }

            long offsetX = (long)_precisionClipboard.WidthX * (_precisionClipboardPasteCount + 1L);
            if (offsetX > int.MaxValue || offsetX > SmartBuildPrecisionTools.MaximumCoordinateMagnitude * 2L)
            {
                InfoStore.Add("The next Smart Builder clipboard offset is outside the safe craft-local range.");
                return;
            }

            Vector3i offset = new Vector3i((int)offsetX, 0, 0);
            var staged = new List<SmartBuildPiece>(_precisionClipboard.Pieces.Count);
            SmartBuildPiece primary = null;
            try
            {
                foreach (SmartBuildPiece source in _precisionClipboard.Pieces)
                {
                    SmartBuildPiece duplicate = source.Duplicate(offset);
                    if (!SmartBuildPrecisionTools.IsSafeBounds(duplicate.Bounds))
                    {
                        InfoStore.Add("Pasted pieces would exceed the safe craft-local grid range.");
                        return;
                    }
                    staged.Add(duplicate);
                    if (source.Id == _precisionClipboard.PrimarySourceId)
                        primary = duplicate;
                }
            }
            catch (Exception exception)
            {
                InfoStore.Add("The Smart Builder session clipboard could not be pasted: " + exception.Message);
                return;
            }

            if (staged.Count == 0)
                return;

            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            SmartBuildPiece[] existing = _scene?.Pieces.ToArray() ?? Array.Empty<SmartBuildPiece>();
            if (_scene == null)
                _scene = new SmartBuildPieceScene(_precisionClipboard.Construct);
            _scene.ReplaceWith(
                existing.Concat(staged),
                staged.Select(piece => piece.Id),
                primary?.Id ?? staged[staged.Count - 1].Id);

            PushSceneSnapshot(before);
            _precisionClipboardPasteCount++;
            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
            _tool = SmartBuildTool.Move;
            _precisionPositionTextInitialized = false;
            RebuildPlan();
            RefreshApplyCancelAttention();
            InfoStore.Add(
                "Pasted " + staged.Count.ToString("N0", CultureInfo.InvariantCulture) +
                " Smart Builder piece(s) " + offsetX.ToString(CultureInfo.InvariantCulture) +
                " craft-local cell(s) along +X from the copied position.");
        }

        private void ResetPrecisionSessionState()
        {
            _precisionClipboard = null;
            _precisionClipboardPasteCount = 0;
            _precisionPositionTextInitialized = false;
            _precisionSyncedPieceId = -1;
            ResetPrecisionTransformState();
        }
    }
}
