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
    internal enum SmartBuildConditionalReplacementMatchMode
    {
        Material,
        Shape,
        MaterialAndShape,
        ExactDefinition
    }

    /// <summary>
    /// Immutable guard captured with a conditional replacement plan.  A plan is
    /// destructive, so Apply must prove that its source filter, target controls,
    /// construct, and selected scan footprint still match the preview the user saw.
    /// </summary>
    internal sealed class SmartBuildConditionalReplacementInputSnapshot
    {
        private readonly SmartBuildConditionalReplacementMatchMode _matchMode;
        private readonly Guid _definitionGuid;
        private readonly SmartBuildMaterial _sourceMaterial;
        private readonly string _sourceShape;
        private readonly int _sourceLength;
        private readonly SmartBuildMaterial _targetMaterial;
        private readonly string _targetShape;
        private readonly int _targetLength;
        private readonly AllConstruct _construct;
        private readonly string _scopeSignature;

        private SmartBuildConditionalReplacementInputSnapshot(
            SmartBuildConditionalReplacementMatchMode matchMode,
            SmartBuildCraftBlockSampleDescriptor sample,
            SmartBuildMaterial targetMaterial,
            string targetShape,
            int targetLength,
            AllConstruct construct,
            IEnumerable<Vector3i> scopeCells)
        {
            _matchMode = matchMode;
            _definitionGuid = sample?.DefinitionGuid ?? Guid.Empty;
            _sourceMaterial = sample?.Material ?? default;
            _sourceShape = Normalize(sample?.ShapeDescriptorKey);
            _sourceLength = sample?.Length ?? 0;
            _targetMaterial = targetMaterial;
            _targetShape = Normalize(targetShape);
            _targetLength = targetLength;
            _construct = construct;
            _scopeSignature = ScopeSignature(scopeCells);
        }

        internal static SmartBuildConditionalReplacementInputSnapshot Capture(
            SmartBuildConditionalReplacementMatchMode matchMode,
            SmartBuildCraftBlockSampleDescriptor sample,
            SmartBuildMaterial targetMaterial,
            string targetShape,
            int targetLength,
            AllConstruct construct,
            IEnumerable<Vector3i> scopeCells) =>
            new SmartBuildConditionalReplacementInputSnapshot(
                matchMode,
                sample,
                targetMaterial,
                targetShape,
                targetLength,
                construct,
                scopeCells);

        internal bool Matches(
            SmartBuildConditionalReplacementMatchMode matchMode,
            SmartBuildCraftBlockSampleDescriptor sample,
            SmartBuildMaterial targetMaterial,
            string targetShape,
            int targetLength,
            AllConstruct construct,
            IEnumerable<Vector3i> scopeCells)
        {
            return sample != null &&
                   _matchMode == matchMode &&
                   _definitionGuid == sample.DefinitionGuid &&
                   _sourceMaterial == sample.Material &&
                   string.Equals(_sourceShape, Normalize(sample.ShapeDescriptorKey), StringComparison.Ordinal) &&
                   _sourceLength == sample.Length &&
                   _targetMaterial == targetMaterial &&
                   string.Equals(_targetShape, Normalize(targetShape), StringComparison.Ordinal) &&
                   _targetLength == targetLength &&
                   ReferenceEquals(_construct, construct) &&
                   string.Equals(_scopeSignature, ScopeSignature(scopeCells), StringComparison.Ordinal);
        }

        private static string Normalize(string value) =>
            (value ?? string.Empty).Trim().ToUpperInvariant();

        private static string ScopeSignature(IEnumerable<Vector3i> cells) =>
            string.Join(
                "|",
                (cells ?? Array.Empty<Vector3i>())
                .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                .Select(group => group.Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray());
    }

    internal sealed partial class SmartBuildSession
    {
        private const int MaximumRemovalWirePreviewCells =
            SmartBuildLimits.MaximumFloodFillCells;
        private SmartBuildConditionalReplacementMatchMode _conditionalReplacementMatchMode =
            SmartBuildConditionalReplacementMatchMode.MaterialAndShape;
        private SmartBuildPlan _conditionalReplacementPlan;
        private SmartBuildConditionalReplacementPlan _conditionalReplacementDetails;
        private SmartBuildConditionalReplacementInputSnapshot _conditionalReplacementInputs;
        private string _conditionalReplacementStatus;

        private bool ConditionalReplacementPreviewActive =>
            _conditionalReplacementPlan != null &&
            ReferenceEquals(_plan, _conditionalReplacementPlan);

        private void DrawConditionalReplacementControls()
        {
            if (_scene?.SelectionCount <= 0)
                return;

            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Replace matching craft blocks", DecorationEditorTheme.SubHeader);
            SmartBuildCraftBlockSampleDescriptor sample = LastCraftBlockSample;
            if (sample == null)
            {
                GUILayout.Label(
                    "Use Pick craft block first. The accepted exact sample becomes the source filter; the current material and Library shape become the target.",
                    DecorationEditorTheme.MiniWrap);
            }
            else
            {
                SmartBuildShapeDescriptor sampledShape =
                    SmartBuildShapeDescriptors.ByKey(sample.ShapeDescriptorKey);
                GUILayout.Label(
                    "Source: " + SmartBlockFamilyCatalog.MaterialDisplayName(sample.Material) +
                    " " + (sampledShape?.Label ?? sample.ShapeDescriptorKey) +
                    " " + sample.Length.ToString(CultureInfo.InvariantCulture) + "m",
                    DecorationEditorTheme.MiniWrap);
                GUILayout.Label(
                    "Target: " + SmartBlockFamilyCatalog.MaterialDisplayName(_selectedMaterial) +
                    " " + (SelectedShapeDescriptor?.Label ?? "shape"),
                    DecorationEditorTheme.MiniWrap);
            }

            GUILayout.BeginHorizontal();
            DrawConditionalMatchButton(SmartBuildConditionalReplacementMatchMode.Material, "Material");
            DrawConditionalMatchButton(SmartBuildConditionalReplacementMatchMode.Shape, "Shape");
            DrawConditionalMatchButton(SmartBuildConditionalReplacementMatchMode.MaterialAndShape, "Both");
            bool previous = GUI.enabled;
            GUI.enabled = previous && sample?.DefinitionGuid != Guid.Empty;
            DrawConditionalMatchButton(SmartBuildConditionalReplacementMatchMode.ExactDefinition, "Exact item");
            GUI.enabled = previous;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = previous && sample != null && _scene?.Count > 0;
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Preview matching replacement",
                        "Within all selected Smart preview volumes, find complete existing craft items matching the sampled source material/shape and preview exact native replacements using the current material and Library shape."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                PreviewConditionalReplacement();
            }
            GUI.enabled = previous && ConditionalReplacementPreviewActive;
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Clear replace preview",
                        "Return to the normal Smart scene placement plan without changing the craft or preview scene."),
                    GUI.enabled ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton,
                    GUILayout.Width(EsuHudLayout.Scale(124f)),
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                ClearConditionalReplacementPreview(rebuildNormalPlan: true);
            }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_conditionalReplacementStatus))
                GUILayout.Label(_conditionalReplacementStatus, DecorationEditorTheme.MiniWrap);
        }

        private void DrawConditionalMatchButton(
            SmartBuildConditionalReplacementMatchMode mode,
            string label)
        {
            if (SmartGUILayoutButton(
                    new GUIContent(
                        label,
                        "Choose which exact fields from the sampled craft block must match before replacement."),
                    DecorationEditorTheme.ToolButton(_conditionalReplacementMatchMode == mode),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                if (_conditionalReplacementMatchMode == mode)
                    return;
                _conditionalReplacementMatchMode = mode;
                InvalidateConditionalReplacementPreview();
            }
        }

        private void PreviewConditionalReplacement()
        {
            SmartBuildCraftBlockSampleDescriptor sample = LastCraftBlockSample;
            if (sample == null || _scene?.Construct == null)
            {
                RejectConditionalReplacement(
                    "Sample an exact craft block and create a Smart preview region first.");
                return;
            }

            if (_planDirty || _plan == null)
                RebuildPlan();
            // Conditional replacement is bounded by the selected preview pieces,
            // not unrelated pieces elsewhere in the Smart scene. A temporary scene
            // reuses the authoritative preview/symmetry path without mutating IDs,
            // selection, history, or the live scene.
            Vector3i[] scopeCells = ConditionalReplacementScopeCells();
            if (scopeCells.Length == 0)
            {
                RejectConditionalReplacement("The Smart preview region contains no scan cells.");
                return;
            }

            RefreshSelection();
            SmartBuildSource targetSource = _source;
            SmartBuildShapeDescriptor targetShape = SelectedShapeDescriptor;
            SmartBuildVolume referenceVolume = VolumeFromCells(_scene.Construct, scopeCells);
            SmartBuildExistingItemMatch sourceMatch = ConditionalSourceMatch(sample);
            if (!SmartBuildConditionalReplacementPlanner.TryBuildCraftPlan(
                    _scene.Construct,
                    referenceVolume,
                    scopeCells,
                    sourceMatch,
                    new SmartBuildReplacementTarget(targetSource, targetShape),
                    new SmartBuildConditionalReplacementLimits
                    {
                        HardScopeCellCap = SmartBuildLimits.MaximumConditionalScopeCells,
                        HardItemCap = SmartBuildLimits.MaximumConditionalItems,
                        HardFootprintCellCap = SmartBuildLimits.MaximumConditionalFootprintCells
                    },
                    out SmartBuildPlan plan,
                    out SmartBuildConditionalReplacementPlan details,
                    out string reason))
            {
                RejectConditionalReplacement(reason);
                return;
            }

            _conditionalReplacementPlan = plan;
            _conditionalReplacementDetails = details;
            _conditionalReplacementInputs = SmartBuildConditionalReplacementInputSnapshot.Capture(
                _conditionalReplacementMatchMode,
                sample,
                _selectedMaterial,
                targetShape?.Key,
                _selectedSlopeLength,
                _scene.Construct,
                scopeCells);
            _plan = plan;
            _planDirty = false;
            _conditionalReplacementStatus =
                "Previewing " + details.MatchedItemCount.ToString("N0", CultureInfo.InvariantCulture) +
                " complete matched item(s): " +
                details.RemovedFootprintCells.Count.ToString("N0", CultureInfo.InvariantCulture) +
                " removed cells -> " +
                details.ReplacementFootprintCells.Count.ToString("N0", CultureInfo.InvariantCulture) +
                " replacement cells. Apply commits one undoable transaction.";
            NotifyPlanIssueIfNeeded();
            RefreshApplyCancelAttention();
            InfoStore.Add("Smart Builder: " + _conditionalReplacementStatus);
        }

        private SmartBuildExistingItemMatch ConditionalSourceMatch(
            SmartBuildCraftBlockSampleDescriptor sample)
        {
            switch (_conditionalReplacementMatchMode)
            {
                case SmartBuildConditionalReplacementMatchMode.Material:
                    return SmartBuildExistingItemMatch.ForMaterial(sample.Material);
                case SmartBuildConditionalReplacementMatchMode.Shape:
                    return SmartBuildExistingItemMatch.ForShape(sample.ShapeDescriptorKey);
                case SmartBuildConditionalReplacementMatchMode.ExactDefinition:
                    return SmartBuildExistingItemMatch.ForExactDefinition(sample.DefinitionGuid);
                default:
                    return SmartBuildExistingItemMatch.ForMaterialAndShape(
                        sample.Material,
                        sample.ShapeDescriptorKey);
            }
        }

        private void ClearConditionalReplacementPreview(bool rebuildNormalPlan)
        {
            bool active = ConditionalReplacementPreviewActive;
            _conditionalReplacementPlan = null;
            _conditionalReplacementDetails = null;
            _conditionalReplacementInputs = null;
            _conditionalReplacementStatus = null;
            if (active && rebuildNormalPlan && _scene?.Count > 0)
                RebuildPlan(SmartBuildPlanRevisionKind.Presentation);
        }

        private void InvalidateConditionalReplacementPreview()
        {
            bool hadPreview = _conditionalReplacementPlan != null ||
                              _conditionalReplacementInputs != null;
            ClearConditionalReplacementPreview(rebuildNormalPlan: true);
            if (hadPreview)
            {
                InfoStore.Add(
                    "Smart Builder conditional replacement preview cleared because its inputs changed. Preview it again before Apply.");
            }
        }

        private Vector3i[] ConditionalReplacementScopeCells()
        {
            if (_scene == null || _scene.SelectionCount <= 0)
                return Array.Empty<Vector3i>();

            var scopeScene = new SmartBuildPieceScene(_scene.Construct);
            scopeScene.ReplaceWith(
                _scene.SelectedPieces.Select(piece => piece.Clone()),
                selectedId: -1);
            SmartBuildPreviewSnapshot scopePreview =
                scopeScene.BuildPreviewWithSources(SourceForPiece);
            return (scopePreview.Cells ?? Array.Empty<Vector3i>())
                .GroupBy(DecoLimitLifter.EsuSymmetry.CellKey)
                .Select(group => group.First())
                .ToArray();
        }

        private bool ConditionalReplacementPreviewMatchesCurrentInputs()
        {
            SmartBuildShapeDescriptor targetShape = SelectedShapeDescriptor;
            return _conditionalReplacementInputs?.Matches(
                       _conditionalReplacementMatchMode,
                       LastCraftBlockSample,
                       _selectedMaterial,
                       targetShape?.Key,
                       _selectedSlopeLength,
                       _scene?.Construct,
                       ConditionalReplacementScopeCells()) == true;
        }

        /// <summary>
        /// Draws exact complete removal footprints for generic Replace/Erase and
        /// conditional replacement. Touched cells are bright red; the rest of a
        /// touched multi-cell item remains orange-red so the user can see collateral
        /// footprint removal before Apply. Conditional target footprints are cyan.
        /// </summary>
        private void DrawDestructivePlanFootprintOverlay()
        {
            if (_planDirty || _plan?.RemovalFootprintCells == null ||
                _plan.RemovalFootprintCells.Count == 0 || _plan.Construct == null)
            {
                return;
            }

            var touched = new HashSet<string>(
                (_plan.RemovalTouchedCells ?? Array.Empty<Vector3i>())
                .Select(DecoLimitLifter.EsuSymmetry.CellKey),
                StringComparer.Ordinal);
            int removalCount = Math.Min(
                MaximumRemovalWirePreviewCells,
                _plan.RemovalFootprintCells.Count);
            for (int index = 0; index < removalCount; index++)
            {
                Vector3i cell = _plan.RemovalFootprintCells[index];
                bool directlyTouched = touched.Contains(DecoLimitLifter.EsuSymmetry.CellKey(cell));
                DrawCellWire(
                    _plan.Construct,
                    cell,
                    directlyTouched
                        ? new Color(1f, 0.12f, 0.08f, 1f)
                        : new Color(1f, 0.42f, 0.08f, 0.95f),
                    directlyTouched ? 4.2f : 3.2f);
            }

            if (!ConditionalReplacementPreviewActive ||
                _conditionalReplacementDetails?.ReplacementFootprintCells == null)
            {
                return;
            }

            int replacementCount = Math.Min(
                MaximumRemovalWirePreviewCells,
                _conditionalReplacementDetails.ReplacementFootprintCells.Count);
            for (int index = 0; index < replacementCount; index++)
            {
                DrawCellWire(
                    _plan.Construct,
                    _conditionalReplacementDetails.ReplacementFootprintCells[index],
                    new Color(0.08f, 0.95f, 1f, 1f),
                    2.4f);
            }
        }

        private void RejectConditionalReplacement(string reason)
        {
            ClearConditionalReplacementPreview(rebuildNormalPlan: false);
            _conditionalReplacementStatus = string.IsNullOrWhiteSpace(reason)
                ? "Conditional replacement was rejected before changing the scene or craft."
                : reason;
            InfoStore.Add("Smart Builder conditional replace rejected: " + _conditionalReplacementStatus);
        }

    }
}
