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
        private int _advancedGridColumns = 3;
        private int _advancedGridRows = 3;
        private int _advancedPathTurnAfter = 3;
        private int _advancedPathLift;
        private int _advancedFloodRadius = 16;
        private bool _advancedRadialRotateCopies = true;
        private string _advancedPatternStatus;
        private int _editablePatternFieldsNodeId = -1;
        private string _editablePrimaryStepText;
        private string _editableSecondaryStepText;
        private string _editableRadialPivotText;
        private string _editableRadialAngleText;
        private string _editablePathPointsText;
        private int _editablePatternSourceIndex;

        /// <summary>
        /// Reachable controls for every bounded advanced Smart Builder pattern.
        /// The basic +/- axis buttons above remain the direct linear-array path.
        /// </summary>
        private void DrawAdvancedArrayBrushControls()
        {
            if (_scene?.SelectionCount <= 0)
                return;

            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label("Advanced patterns", DecorationEditorTheme.SubHeader);
            DrawAdvancedPatternStepper(
                "Columns",
                ref _advancedGridColumns,
                1,
                32,
                "Grid and rectangle width, including the original cell or group.");
            DrawAdvancedPatternStepper(
                "Rows",
                ref _advancedGridRows,
                1,
                32,
                "Grid and rectangle height, including the original cell or group.");

            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent("Linear", "Create one editable linear pattern node from the selected source group."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CreateEditableLinearPattern();
            }
            if (SmartGUILayoutButton(
                    new GUIContent("Grid", "Create one editable grid pattern node across the active draw plane."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CreateAdvancedGridArray();
            }
            if (SmartGUILayoutButton(
                    new GUIContent("Path verts", "Duplicate at the vertices of a configurable elbow path."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CreateAdvancedPathArray(SmartBuildPathArrayMode.PolylineVertices);
            }
            if (SmartGUILayoutButton(
                    new GUIContent("Path cells", "Duplicate at every stepped grid cell along a configurable elbow path."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CreateAdvancedPathArray(SmartBuildPathArrayMode.SteppedCells);
            }
            GUILayout.EndHorizontal();

            DrawSelectedEditableNodeControls();

            DrawAdvancedPatternStepper(
                "Turn after",
                ref _advancedPathTurnAfter,
                1,
                64,
                "Number of forward spacing steps before the path turns on the active plane.");
            DrawAdvancedPatternStepper(
                "Path lift",
                ref _advancedPathLift,
                -16,
                16,
                "Grid-cell lift along the active plane normal at the path endpoint.");

            GUILayout.BeginHorizontal();
            DrawAdvancedRadialButton(DecorationEditAxis.X, "Radial X");
            DrawAdvancedRadialButton(DecorationEditAxis.Y, "Radial Y");
            DrawAdvancedRadialButton(DecorationEditAxis.Z, "Radial Z");
            GUILayout.EndHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent(
                        _advancedRadialRotateCopies ? "Radial orientation: Rotate" : "Radial orientation: Keep",
                        "Choose whether cardinal radial copies turn with their position or retain the original orientation."),
                    DecorationEditorTheme.ToolButton(_advancedRadialRotateCopies),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                _advancedRadialRotateCopies = !_advancedRadialRotateCopies;
            }

            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent("Wall fill", "Fill a vertical rectangular wall using the selected 1m cuboid."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CreateAdvancedRectanglePattern(AdvancedRectangleAction.WallFill);
            }
            if (SmartGUILayoutButton(
                    new GUIContent("Wall brush", "Brush only the perimeter of a vertical rectangular wall using the selected 1m cuboid."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CreateAdvancedRectanglePattern(AdvancedRectangleAction.WallPerimeter);
            }
            if (SmartGUILayoutButton(
                    new GUIContent("Plane brush", "Fill a rectangle on the active draw plane using the selected 1m cuboid."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CreateAdvancedRectanglePattern(AdvancedRectangleAction.ActivePlaneFill);
            }
            GUILayout.EndHorizontal();

            DrawAdvancedPatternStepper(
                "Flood radius",
                ref _advancedFloodRadius,
                2,
                64,
                "Maximum planar search radius. Open regions and scene-cap overflow reject without partial copies.");
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Flood fill enclosed plane",
                        "Four-neighbour fill from the selected 1m cuboid. Existing craft blocks and other preview pieces form the boundary; open regions reject atomically."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CreateAdvancedFloodFill();
            }

            if (!string.IsNullOrWhiteSpace(_advancedPatternStatus))
                GUILayout.Label(_advancedPatternStatus, DecorationEditorTheme.MiniWrap);
            DrawSmartBuildDiagnosticLegend();
        }

        private void DrawAdvancedPatternStepper(
            string label,
            ref int value,
            int minimum,
            int maximum,
            string tooltip)
        {
            value = Mathf.Clamp(value, minimum, maximum);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(label, tooltip),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(74f)));
            if (SmartGUILayoutButton(
                    new GUIContent("-", "Decrease " + label.ToLowerInvariant() + "."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                value = Mathf.Clamp(value - 1, minimum, maximum);
            }
            GUILayout.Label(
                value.ToString(CultureInfo.InvariantCulture),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(36f)));
            if (SmartGUILayoutButton(
                    new GUIContent("+", "Increase " + label.ToLowerInvariant() + "."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(28f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                value = Mathf.Clamp(value + 1, minimum, maximum);
            }
            GUILayout.EndHorizontal();
        }

        private void CreateAdvancedGridArray()
        {
            SmartBuildAxis normal = ActivePatternPlaneNormal();
            SmartBuildAxis[] axes = SmartBuildAxisHelper.PlaneAxes(normal);
            int spacing = Math.Max(1, _arraySpacing);
            var definition = new SmartBuildPatternDefinition
            {
                Kind = SmartBuildEditablePatternKind.Grid,
                PrimaryStep = SmartBuildAxisHelper.ToVector3i(axes[0], spacing),
                PrimaryAfter = Math.Max(0, _advancedGridColumns - 1),
                SecondaryStep = SmartBuildAxisHelper.ToVector3i(axes[1], spacing),
                SecondaryAfter = Math.Max(0, _advancedGridRows - 1)
            };
            ApplyEditablePattern(definition, "grid pattern");
        }

        private void CreateEditableLinearPattern()
        {
            SmartBuildAxis[] axes = SmartBuildAxisHelper.PlaneAxes(ActivePatternPlaneNormal());
            var definition = new SmartBuildPatternDefinition
            {
                Kind = SmartBuildEditablePatternKind.Linear,
                PrimaryStep = SmartBuildAxisHelper.ToVector3i(
                    axes[0],
                    Math.Max(1, _arraySpacing)),
                PrimaryAfter = Math.Max(1, _arrayCopyCount)
            };
            ApplyEditablePattern(definition, "linear pattern");
        }

        private void DrawAdvancedRadialButton(DecorationEditAxis axis, string label)
        {
            if (SmartGUILayoutButton(
                    new GUIContent(
                        label,
                        "Create up to three 90-degree radial copies around " + axis +
                        ". Gap is the radius; Copies is clamped to three."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(24f))))
            {
                CreateAdvancedRadialArray(axis);
            }
        }

        private void CreateAdvancedRadialArray(DecorationEditAxis axis)
        {
            int copies = Mathf.Clamp(_arrayCopyCount, 1, 3);
            if (!TrySelectedGroupPivot(out Vector3i groupPivot, out string reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }
            SmartBuildAxis armAxis = FirstAxisPerpendicularTo(axis);
            Vector3i arm = SmartBuildAxisHelper.ToVector3i(
                armAxis,
                Math.Max(1, _arraySpacing));
            var definition = new SmartBuildPatternDefinition
            {
                Kind = SmartBuildEditablePatternKind.Radial,
                RadialAxis = axis,
                RadialPivot = groupPivot - arm,
                RadialAngleStepDegrees = 90f,
                RadialOrientation = _advancedRadialRotateCopies
                    ? SmartBuildRadialOrientationMode.RotateCardinal
                    : SmartBuildRadialOrientationMode.Keep,
                PrimaryAfter = copies
            };
            ApplyEditablePattern(definition, "radial pattern");
        }

        private void CreateAdvancedPathArray(SmartBuildPathArrayMode mode)
        {
            SmartBuildAxis normal = ActivePatternPlaneNormal();
            SmartBuildAxis[] axes = SmartBuildAxisHelper.PlaneAxes(normal);
            int spacing = Math.Max(1, _arraySpacing);
            int requestedCopies = Mathf.Clamp(
                _arrayCopyCount,
                1,
                SmartBuildAdvancedToolPlanner.MaximumPathControlPoints - 1);
            int firstLegSteps = Mathf.Clamp(_advancedPathTurnAfter, 1, requestedCopies);
            int secondLegSteps = requestedCopies - firstLegSteps;

            Vector3i corner = SmartBuildAxisHelper.ToVector3i(
                axes[0],
                firstLegSteps * spacing);
            var points = new List<Vector3i>
            {
                new Vector3i(0, 0, 0),
                corner
            };
            if (secondLegSteps > 0 || _advancedPathLift != 0)
            {
                points.Add(
                    corner +
                    SmartBuildAxisHelper.ToVector3i(axes[1], secondLegSteps * spacing) +
                    SmartBuildAxisHelper.ToVector3i(normal, _advancedPathLift));
            }

            var definition = new SmartBuildPatternDefinition
            {
                Kind = SmartBuildEditablePatternKind.Polyline,
                PathPoints = points,
                PathMode = mode,
                PathSpacingCells = 1
            };
            ApplyEditablePattern(
                definition,
                mode == SmartBuildPathArrayMode.SteppedCells
                    ? "stepped path pattern"
                    : "polyline pattern");
        }

        private void CreateAdvancedRectanglePattern(AdvancedRectangleAction action)
        {
            if (!TryGetAdvancedCellBrushSeed(out Vector3i seed, out string reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }

            // A fill is always continuous. Brushes intentionally retain the
            // configurable Gap so they can stamp a spaced wall/plane pattern.
            int spacing = action == AdvancedRectangleAction.WallFill
                ? 1
                : Math.Max(1, _arraySpacing);
            Vector3i columnStep;
            Vector3i rowStep;
            bool planned;
            IReadOnlyList<Vector3i> cells;
            switch (action)
            {
                case AdvancedRectangleAction.WallFill:
                case AdvancedRectangleAction.WallPerimeter:
                    SmartBuildAxis horizontal = _drawPlane == SmartBuildDrawPlane.YZ
                        ? SmartBuildAxis.Z
                        : SmartBuildAxis.X;
                    columnStep = SmartBuildAxisHelper.ToVector3i(horizontal, spacing);
                    rowStep = SmartBuildAxisHelper.ToVector3i(SmartBuildAxis.Y, spacing);
                    planned = action == AdvancedRectangleAction.WallFill
                        ? SmartBuildAdvancedToolPlanner.TryPlanRectanglePlane(
                            seed,
                            columnStep,
                            _advancedGridColumns,
                            rowStep,
                            _advancedGridRows,
                            out cells,
                            out reason)
                        : SmartBuildAdvancedToolPlanner.TryPlanRectangleWall(
                            seed,
                            columnStep,
                            _advancedGridColumns,
                            rowStep,
                            _advancedGridRows,
                            out cells,
                            out reason);
                    break;
                default:
                    SmartBuildAxis[] axes = SmartBuildAxisHelper.PlaneAxes(ActivePatternPlaneNormal());
                    columnStep = SmartBuildAxisHelper.ToVector3i(axes[0], spacing);
                    rowStep = SmartBuildAxisHelper.ToVector3i(axes[1], spacing);
                    planned = SmartBuildAdvancedToolPlanner.TryPlanRectanglePlane(
                        seed,
                        columnStep,
                        _advancedGridColumns,
                        rowStep,
                        _advancedGridRows,
                        out cells,
                        out reason);
                    break;
            }

            if (!planned)
            {
                RejectAdvancedPattern(reason);
                return;
            }

            ApplyAdvancedCellPattern(
                seed,
                cells,
                action == AdvancedRectangleAction.WallFill
                    ? SmartBuildRegionKind.Wall
                    : action == AdvancedRectangleAction.WallPerimeter
                        ? SmartBuildRegionKind.Brush
                        : SmartBuildRegionKind.Plane,
                action == AdvancedRectangleAction.WallFill
                    ? "wall fill"
                    : action == AdvancedRectangleAction.WallPerimeter
                        ? "wall brush"
                        : "plane brush");
        }

        private void CreateAdvancedFloodFill()
        {
            if (!TryGetAdvancedCellBrushSeed(out Vector3i seed, out string reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }

            SmartBuildAxis normal = ActivePatternPlaneNormal();
            SmartBuildAxis[] axes = SmartBuildAxisHelper.PlaneAxes(normal);
            int radius = Mathf.Clamp(_advancedFloodRadius, 2, 64);
            Vector3i min = seed;
            Vector3i max = seed;
            foreach (SmartBuildAxis axis in axes)
            {
                min = SmartBuildAxisHelper.Set(
                    min,
                    axis,
                    SmartBuildAxisHelper.Get(seed, axis) - radius);
                max = SmartBuildAxisHelper.Set(
                    max,
                    axis,
                    SmartBuildAxisHelper.Get(seed, axis) + radius);
            }

            var previewBoundary = new HashSet<string>(StringComparer.Ordinal);
            foreach (SmartBuildPiece piece in _scene.Pieces)
            {
                if (piece == null || _scene.IsSelected(piece.Id))
                    continue;

                foreach (Vector3i cell in piece.EnumeratePreviewCells(SourceForPiece(piece)))
                    previewBoundary.Add(DecoLimitLifter.EsuSymmetry.CellKey(cell));
            }

            int maximumCells = SmartBuildAdvancedToolPlanner.MaximumFloodFillCells;
            if (!SmartBuildAdvancedToolPlanner.TryPlanEnclosedPlanarFloodFill(
                    seed,
                    normal,
                    new SmartBuildBounds(min, max),
                    cell =>
                        IsCellOccupied(_scene.Construct, cell) ||
                        previewBoundary.Contains(DecoLimitLifter.EsuSymmetry.CellKey(cell)),
                    maximumCells,
                    out IReadOnlyList<Vector3i> cells,
                    out reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }

            ApplyAdvancedCellPattern(
                seed,
                cells,
                SmartBuildRegionKind.Flood,
                "enclosed planar flood fill");
        }

        private bool TryGetAdvancedCellBrushSeed(
            out Vector3i seed,
            out string reason)
        {
            seed = new Vector3i(0, 0, 0);
            IReadOnlyList<SmartBuildPiece> selected = _scene?.SelectedPieces;
            if (selected == null || selected.Count != 1)
            {
                reason = "Wall, plane, and flood tools require exactly one selected 1m cuboid.";
                return false;
            }

            SmartBuildPiece piece = selected[0];
            SmartBuildBounds bounds = piece.Bounds;
            if (piece.ShapeKind != SmartBuildShapeKind.Cuboid ||
                bounds.Size.x != 1 || bounds.Size.y != 1 || bounds.Size.z != 1)
            {
                reason = "Wall, plane, and flood tools require exactly one selected 1m cuboid.";
                return false;
            }

            seed = piece.Origin;
            reason = null;
            return true;
        }

        private void ApplyAdvancedCellPattern(
            Vector3i seed,
            IEnumerable<Vector3i> absoluteCells,
            SmartBuildRegionKind kind,
            string label)
        {
            Vector3i[] cells = (absoluteCells ?? Array.Empty<Vector3i>())
                .Distinct()
                .ToArray();
            if (cells.Length <= 1)
            {
                RejectAdvancedPattern("The " + label + " contains only its existing seed cell.");
                return;
            }
            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            if (!_scene.TryCreateRegionFromSelection(kind, cells, out SmartBuildRegionNode region, out string reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }
            PushSceneSnapshot(before);
            _draft = region.HostPiece;
            _sceneSelectionAnchorId = _draft.Id;
            _tool = SmartBuildTool.Move;
            RebuildPlan();
            RefreshApplyCancelAttention();
            _advancedPatternStatus =
                "Created editable " + label + " with " +
                cells.Length.ToString(CultureInfo.InvariantCulture) + " cells in one scene node.";
            InfoStore.Add("Smart Builder: " + _advancedPatternStatus);
        }

        private void ApplyEditablePattern(
            SmartBuildPatternDefinition definition,
            string label)
        {
            if (_scene?.SelectionCount <= 0)
                return;

            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            if (!_scene.TryCreatePatternFromSelection(
                    definition,
                    out SmartBuildPatternNode pattern,
                    out string reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }

            PushSceneSnapshot(before);
            _draft = pattern.HostPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
            _tool = SmartBuildTool.Move;
            RebuildPlan();
            RefreshApplyCancelAttention();
            _advancedPatternStatus =
                "Created one editable " + label +
                "; Apply expands it directly, while Bake makes independent pieces.";
            InfoStore.Add("Smart Builder: " + _advancedPatternStatus);
        }

        private void DrawSelectedEditableNodeControls()
        {
            ISmartBuildSceneNode node = _scene?.SelectedNode;
            if (node == null || node.Kind == SmartBuildSceneNodeKind.Primitive)
                return;

            GUILayout.Space(EsuHudLayout.Scale(4f));
            GUILayout.Label(
                node.Kind == SmartBuildSceneNodeKind.Pattern
                    ? "Selected editable pattern"
                    : "Selected editable region",
                DecorationEditorTheme.SubHeader);

            bool guiEnabledBeforeViewportGesture = GUI.enabled;
            bool viewportGesturePending = _patternViewportGesture != null;
            if (viewportGesturePending)
            {
                GUILayout.Label(
                    "Viewport edit staged - Enter commits, Escape restores",
                    DecorationEditorTheme.MiniWrap);
                GUI.enabled = false;
            }

            if (node is SmartBuildPatternNode pattern)
            {
                if (_editablePatternFieldsNodeId != pattern.Id)
                    SyncEditablePatternFields(pattern);
                SmartBuildPatternDefinition definition = pattern.Definition;
                GUILayout.Label(
                    definition.Kind + " · source pieces " +
                    pattern.SourcePieces.Count.ToString(CultureInfo.InvariantCulture),
                    DecorationEditorTheme.MiniWrap);
                DrawEditablePatternSourceControls(pattern);

                int primaryBefore = definition.PrimaryBefore;
                DrawAdvancedPatternStepper(
                    "Before",
                    ref primaryBefore,
                    0,
                    SmartBuildPieceScene.MaximumScenePieces - 1,
                    "Copies before instance zero along the primary pattern direction.");
                definition.PrimaryBefore = primaryBefore;
                int primaryAfter = definition.PrimaryAfter;
                DrawAdvancedPatternStepper(
                    "After",
                    ref primaryAfter,
                    0,
                    SmartBuildPieceScene.MaximumScenePieces - 1,
                    "Copies after instance zero along the primary pattern direction.");
                definition.PrimaryAfter = primaryAfter;

                if (definition.Kind == SmartBuildEditablePatternKind.Linear ||
                    definition.Kind == SmartBuildEditablePatternKind.Grid)
                {
                    DrawEditableTextField("Step XYZ", ref _editablePrimaryStepText,
                        "Craft-local integer vector, for example 4,0,0.");
                }
                if (definition.Kind == SmartBuildEditablePatternKind.Grid)
                {
                    int secondaryBefore = definition.SecondaryBefore;
                    DrawAdvancedPatternStepper(
                        "V before",
                        ref secondaryBefore,
                        0,
                        SmartBuildPieceScene.MaximumScenePieces - 1,
                        "Copies before instance zero along the secondary grid direction.");
                    definition.SecondaryBefore = secondaryBefore;
                    int secondaryAfter = definition.SecondaryAfter;
                    DrawAdvancedPatternStepper(
                        "V after",
                        ref secondaryAfter,
                        0,
                        SmartBuildPieceScene.MaximumScenePieces - 1,
                        "Copies after instance zero along the secondary grid direction.");
                    definition.SecondaryAfter = secondaryAfter;
                    DrawEditableTextField("V step XYZ", ref _editableSecondaryStepText,
                        "Independent craft-local integer grid vector.");
                }
                if (definition.Kind == SmartBuildEditablePatternKind.Radial)
                {
                    DrawEditableTextField("Pivot XYZ", ref _editableRadialPivotText,
                        "Craft-local radial pivot cell.");
                    DrawEditableTextField("Angle deg", ref _editableRadialAngleText,
                        "Finite angle step. Rotate orientation requires a 90-degree multiple.");
                    GUILayout.BeginHorizontal();
                    DrawEditableRadialAxisButton(definition, DecorationEditAxis.X);
                    DrawEditableRadialAxisButton(definition, DecorationEditAxis.Y);
                    DrawEditableRadialAxisButton(definition, DecorationEditAxis.Z);
                    GUILayout.EndHorizontal();
                    if (SmartGUILayoutButton(
                            new GUIContent(
                                definition.RadialOrientation == SmartBuildRadialOrientationMode.Keep
                                    ? "Orientation: Keep"
                                    : "Orientation: Rotate cardinal",
                                "Arbitrary angles keep block orientation. Rotating orientation requires exact quarter turns."),
                            DecorationEditorTheme.Button,
                            GUILayout.Height(EsuHudLayout.Scale(22f))))
                    {
                        definition.RadialOrientation =
                            definition.RadialOrientation == SmartBuildRadialOrientationMode.Keep
                                ? SmartBuildRadialOrientationMode.RotateCardinal
                                : SmartBuildRadialOrientationMode.Keep;
                    }
                }
                if (definition.Kind == SmartBuildEditablePatternKind.Polyline)
                {
                    DrawEditableTextField(
                        "Path points",
                        ref _editablePathPointsText,
                        "Semicolon-separated craft-local points: x,y,z; x,y,z.");
                    int pathSpacing = definition.PathSpacingCells;
                    DrawAdvancedPatternStepper(
                        "Spacing",
                        ref pathSpacing,
                        1,
                        64,
                        "Emit one instance every N planned path cells or vertices.");
                    definition.PathSpacingCells = pathSpacing;
                    if (SmartGUILayoutButton(
                            new GUIContent(
                                definition.PathOrientation == SmartBuildPolylineOrientationMode.Keep
                                    ? "Orientation: Keep"
                                    : "Orientation: Cardinal tangent",
                                "Keep source orientation, or rotate each non-source instance so its forward axis follows the dominant craft-local path tangent."),
                            DecorationEditorTheme.Button,
                            GUILayout.Height(EsuHudLayout.Scale(22f))))
                    {
                        definition.PathOrientation =
                            definition.PathOrientation == SmartBuildPolylineOrientationMode.Keep
                                ? SmartBuildPolylineOrientationMode.CardinalTangent
                                : SmartBuildPolylineOrientationMode.Keep;
                    }
                }

                if (SmartGUILayoutButton(
                        new GUIContent("Update editable pattern", "Validate and atomically apply the typed parameters."),
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(24f))))
                {
                    UpdateSelectedEditablePattern(definition);
                }
            }
            else if (node is SmartBuildRegionNode region)
            {
                int cells = region.Spans.Sum(span => span.Length);
                GUILayout.Label(
                    region.RegionKind + " · " +
                    cells.ToString(CultureInfo.InvariantCulture) + " cells · " +
                    region.Spans.Count.ToString(CultureInfo.InvariantCulture) + " RLE spans",
                    DecorationEditorTheme.MiniWrap);
                if (region.RegionKind == SmartBuildRegionKind.Flood &&
                    SmartGUILayoutButton(
                        new GUIContent("Recompute from craft", "Re-run the bounded enclosed flood from the current host cell."),
                        DecorationEditorTheme.Button,
                        GUILayout.Height(EsuHudLayout.Scale(23f))))
                {
                    RecomputeSelectedFloodRegion();
                }
            }

            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent("Bake", "Replace the editable node with independent primitive preview pieces."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                BakeSelectedEditableNode();
            }
            if (SmartGUILayoutButton(
                    new GUIContent("Dissolve", "Restore only the original source group and remove the pattern or region."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(23f))))
            {
                DissolveSelectedEditableNode();
            }
            GUILayout.EndHorizontal();
            GUI.enabled = guiEnabledBeforeViewportGesture;
        }

        private void DrawEditableRadialAxisButton(
            SmartBuildPatternDefinition definition,
            DecorationEditAxis axis)
        {
            if (SmartGUILayoutButton(
                    new GUIContent(axis.ToString(), "Use the craft-local " + axis + " radial axis."),
                    DecorationEditorTheme.ToolButton(definition.RadialAxis == axis),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                definition.RadialAxis = axis;
            }
        }

        private void DrawEditablePatternSourceControls(SmartBuildPatternNode pattern)
        {
            IReadOnlyList<SmartBuildPiece> sources =
                pattern?.SourcePieces ?? Array.Empty<SmartBuildPiece>();
            if (sources.Count == 0)
                return;

            _editablePatternSourceIndex = Math.Max(
                0,
                Math.Min(_editablePatternSourceIndex, sources.Count - 1));
            SmartBuildPiece source = sources[_editablePatternSourceIndex];
            bool controlsEnabled = GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled = controlsEnabled && sources.Count > 1;
            if (SmartGUILayoutButton(
                    new GUIContent("<", "Select the previous embedded primitive source."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(30f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                _editablePatternSourceIndex =
                    (_editablePatternSourceIndex + sources.Count - 1) % sources.Count;
            }
            GUI.enabled = controlsEnabled;
            GUILayout.Label(
                "Edit Source " +
                (_editablePatternSourceIndex + 1).ToString(CultureInfo.InvariantCulture) +
                "/" + sources.Count.ToString(CultureInfo.InvariantCulture) +
                " · " + source.ShapeKind +
                " · " + (source.MaterialOverride?.ToString() ?? "inherited material"),
                DecorationEditorTheme.MiniWrap);
            GUI.enabled = controlsEnabled && sources.Count > 1;
            if (SmartGUILayoutButton(
                    new GUIContent(">", "Select the next embedded primitive source."),
                    DecorationEditorTheme.Button,
                    GUILayout.Width(EsuHudLayout.Scale(30f)),
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                _editablePatternSourceIndex =
                    (_editablePatternSourceIndex + 1) % sources.Count;
            }
            GUI.enabled = controlsEnabled;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Shape <- palette",
                        "Atomically convert this embedded source to the shape currently selected in the Shapes palette."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                EditSelectedPatternSource(pattern, editShape: true, editMaterial: false);
            }
            if (SmartGUILayoutButton(
                    new GUIContent(
                        "Material <- picker",
                        "Atomically assign the current Smart Builder material to this embedded source."),
                    DecorationEditorTheme.Button,
                    GUILayout.Height(EsuHudLayout.Scale(22f))))
            {
                EditSelectedPatternSource(pattern, editShape: false, editMaterial: true);
            }
            GUILayout.EndHorizontal();
        }

        private void EditSelectedPatternSource(
            SmartBuildPatternNode pattern,
            bool editShape,
            bool editMaterial)
        {
            IReadOnlyList<SmartBuildPiece> sources =
                pattern?.SourcePieces ?? Array.Empty<SmartBuildPiece>();
            if (_editablePatternSourceIndex < 0 ||
                _editablePatternSourceIndex >= sources.Count)
            {
                RejectAdvancedPattern("Select a valid embedded source first.");
                return;
            }

            SmartBuildPiece replacement = sources[_editablePatternSourceIndex].Clone();
            if (editShape &&
                !replacement.TryConvertTo(
                    SelectedShapeDescriptor,
                    _selectedSlopeLength,
                    out string conversionReason))
            {
                RejectAdvancedPattern(conversionReason);
                return;
            }
            if (editMaterial)
                replacement.SetMaterialOverride(_selectedMaterial);

            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            if (!_scene.TryUpdateSelectedPatternSource(
                    _editablePatternSourceIndex,
                    replacement,
                    out string reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }

            PushSceneSnapshot(before);
            _draft = pattern.HostPiece;
            SyncEditablePatternFields(pattern);
            RebuildPlan();
            _advancedPatternStatus =
                "Embedded source " +
                (_editablePatternSourceIndex + 1).ToString(CultureInfo.InvariantCulture) +
                " updated atomically.";
            InfoStore.Add("Smart Builder: " + _advancedPatternStatus);
        }

        private void DrawEditableTextField(string label, ref string value, string tooltip)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(label, tooltip),
                DecorationEditorTheme.Mini,
                GUILayout.Width(EsuHudLayout.Scale(76f)));
            value = GUILayout.TextField(value ?? string.Empty, DecorationEditorTheme.TextField);
            GUILayout.EndHorizontal();
        }

        private void SyncEditablePatternFields(SmartBuildPatternNode pattern)
        {
            SmartBuildPatternDefinition definition = pattern.Definition;
            if (_editablePatternFieldsNodeId != pattern.Id)
                _editablePatternSourceIndex = 0;
            _editablePatternFieldsNodeId = pattern.Id;
            _editablePrimaryStepText = FormatPatternCell(definition.PrimaryStep);
            _editableSecondaryStepText = FormatPatternCell(definition.SecondaryStep);
            _editableRadialPivotText = FormatPatternCell(definition.RadialPivot);
            _editableRadialAngleText = definition.RadialAngleStepDegrees.ToString(
                "0.###",
                CultureInfo.InvariantCulture);
            _editablePathPointsText = string.Join(
                "; ",
                (definition.PathPoints ?? Array.Empty<Vector3i>()).Select(FormatPatternCell));
        }

        private void UpdateSelectedEditablePattern(SmartBuildPatternDefinition definition)
        {
            Vector3i primary = new Vector3i(0, 0, 0);
            if ((definition.Kind == SmartBuildEditablePatternKind.Linear ||
                 definition.Kind == SmartBuildEditablePatternKind.Grid) &&
                !TryParseCell(_editablePrimaryStepText, out primary))
            {
                RejectAdvancedPattern("Primary step must be three comma-separated integers.");
                return;
            }
            else if (definition.Kind == SmartBuildEditablePatternKind.Linear ||
                     definition.Kind == SmartBuildEditablePatternKind.Grid)
            {
                definition.PrimaryStep = primary;
            }
            if (definition.Kind == SmartBuildEditablePatternKind.Grid)
            {
                if (!TryParseCell(_editableSecondaryStepText, out Vector3i secondary))
                {
                    RejectAdvancedPattern("Secondary step must be three comma-separated integers.");
                    return;
                }
                definition.SecondaryStep = secondary;
            }
            if (definition.Kind == SmartBuildEditablePatternKind.Radial)
            {
                float angle;
                if (!TryParseCell(_editableRadialPivotText, out Vector3i pivot) ||
                    !TryParsePatternFloat(_editableRadialAngleText, out angle))
                {
                    RejectAdvancedPattern("Radial pivot needs three integers and angle needs one finite number.");
                    return;
                }
                definition.RadialPivot = pivot;
                definition.RadialAngleStepDegrees = angle;
            }
            if (definition.Kind == SmartBuildEditablePatternKind.Polyline)
            {
                if (!TryParsePathPoints(_editablePathPointsText, out IReadOnlyList<Vector3i> points))
                {
                    RejectAdvancedPattern("Path points must use x,y,z; x,y,z with at least two points.");
                    return;
                }
                definition.PathPoints = points;
            }

            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            if (!_scene.TryUpdateSelectedPattern(definition, out string reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }
            PushSceneSnapshot(before);
            RebuildPlan();
            _advancedPatternStatus = "Editable pattern parameters updated.";
            InfoStore.Add("Smart Builder: " + _advancedPatternStatus);
        }

        private void BakeSelectedEditableNode()
        {
            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            if (!_scene.TryBakeSelectedNode(out IReadOnlyList<SmartBuildPiece> pieces, out string reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }
            PushSceneSnapshot(before);
            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
            _editablePatternFieldsNodeId = -1;
            RebuildPlan();
            _advancedPatternStatus =
                "Baked " + pieces.Count.ToString(CultureInfo.InvariantCulture) +
                " independent preview pieces.";
            InfoStore.Add("Smart Builder: " + _advancedPatternStatus);
        }

        private void DissolveSelectedEditableNode()
        {
            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            if (!_scene.TryDissolveSelectedNode(out IReadOnlyList<SmartBuildPiece> pieces, out string reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }
            PushSceneSnapshot(before);
            _draft = _scene.SelectedPiece;
            _sceneSelectionAnchorId = _draft?.Id ?? -1;
            _editablePatternFieldsNodeId = -1;
            RebuildPlan();
            _advancedPatternStatus =
                "Dissolved to " + pieces.Count.ToString(CultureInfo.InvariantCulture) +
                " original source pieces.";
            InfoStore.Add("Smart Builder: " + _advancedPatternStatus);
        }

        private void RecomputeSelectedFloodRegion()
        {
            if (!TryGetAdvancedCellBrushSeed(out Vector3i seed, out string reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }
            SmartBuildAxis normal = ActivePatternPlaneNormal();
            SmartBuildAxis[] axes = SmartBuildAxisHelper.PlaneAxes(normal);
            int radius = Mathf.Clamp(_advancedFloodRadius, 2, 64);
            Vector3i min = seed;
            Vector3i max = seed;
            foreach (SmartBuildAxis axis in axes)
            {
                min = SmartBuildAxisHelper.Set(min, axis, SmartBuildAxisHelper.Get(seed, axis) - radius);
                max = SmartBuildAxisHelper.Set(max, axis, SmartBuildAxisHelper.Get(seed, axis) + radius);
            }
            if (!SmartBuildAdvancedToolPlanner.TryPlanEnclosedPlanarFloodFill(
                    seed,
                    normal,
                    new SmartBuildBounds(min, max),
                    cell => IsCellOccupied(_scene.Construct, cell),
                    SmartBuildAdvancedToolPlanner.MaximumFloodFillCells,
                    out IReadOnlyList<Vector3i> cells,
                    out reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }
            SmartBuildSceneSnapshot before = CaptureSceneSnapshot();
            if (!_scene.TryReplaceSelectedRegionCells(cells, out reason))
            {
                RejectAdvancedPattern(reason);
                return;
            }
            PushSceneSnapshot(before);
            RebuildPlan();
            _advancedPatternStatus = "Flood region recomputed from the current craft boundary.";
            InfoStore.Add("Smart Builder: " + _advancedPatternStatus);
        }

        private bool TrySelectedGroupPivot(out Vector3i pivot, out string reason)
        {
            pivot = new Vector3i(0, 0, 0);
            IReadOnlyList<SmartBuildPiece> pieces = _scene?.SelectedPieces;
            if (pieces == null || pieces.Count == 0)
            {
                reason = "Select at least one primitive source piece.";
                return false;
            }
            int minX = pieces.Min(piece => piece.Bounds.Min.x);
            int minY = pieces.Min(piece => piece.Bounds.Min.y);
            int minZ = pieces.Min(piece => piece.Bounds.Min.z);
            int maxX = pieces.Max(piece => piece.Bounds.Max.x);
            int maxY = pieces.Max(piece => piece.Bounds.Max.y);
            int maxZ = pieces.Max(piece => piece.Bounds.Max.z);
            pivot = new Vector3i(
                (int)((long)minX + ((long)maxX - minX + 1L) / 2L),
                (int)((long)minY + ((long)maxY - minY + 1L) / 2L),
                (int)((long)minZ + ((long)maxZ - minZ + 1L) / 2L));
            reason = null;
            return true;
        }

        private static string FormatPatternCell(Vector3i cell) =>
            cell.x.ToString(CultureInfo.InvariantCulture) + "," +
            cell.y.ToString(CultureInfo.InvariantCulture) + "," +
            cell.z.ToString(CultureInfo.InvariantCulture);

        private static bool TryParseCell(string text, out Vector3i cell)
        {
            cell = new Vector3i(0, 0, 0);
            string[] parts = (text ?? string.Empty).Split(',');
            if (parts.Length != 3 ||
                !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                !int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
            {
                return false;
            }
            int limit = SmartBuildAdvancedToolPlanner.MaximumCoordinateMagnitude;
            if (Math.Abs((long)x) > limit || Math.Abs((long)y) > limit || Math.Abs((long)z) > limit)
                return false;
            cell = new Vector3i(x, y, z);
            return true;
        }

        private static bool TryParsePathPoints(
            string text,
            out IReadOnlyList<Vector3i> points)
        {
            var parsed = new List<Vector3i>();
            foreach (string token in (text ?? string.Empty).Split(';'))
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;
                if (!TryParseCell(token, out Vector3i point))
                {
                    points = Array.Empty<Vector3i>();
                    return false;
                }
                parsed.Add(point);
            }
            points = parsed;
            return parsed.Count >= 2 &&
                   parsed.Count <= SmartBuildAdvancedToolPlanner.MaximumPathControlPoints;
        }

        private static bool TryParsePatternFloat(string text, out float value)
        {
            bool parsed = float.TryParse(
                              text,
                              NumberStyles.Float,
                              CultureInfo.InvariantCulture,
                              out value) ||
                          float.TryParse(
                              text,
                              NumberStyles.Float,
                              CultureInfo.CurrentCulture,
                              out value);
            return parsed && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void RejectAdvancedPattern(string reason)
        {
            _advancedPatternStatus = string.IsNullOrWhiteSpace(reason)
                ? "The advanced pattern was rejected before changing the scene."
                : reason;
            InfoStore.Add("Smart Builder pattern rejected: " + _advancedPatternStatus);
        }

        private SmartBuildAxis ActivePatternPlaneNormal()
        {
            switch (_drawPlane)
            {
                case SmartBuildDrawPlane.XY:
                    return SmartBuildAxis.Z;
                case SmartBuildDrawPlane.YZ:
                    return SmartBuildAxis.X;
                default:
                    return SmartBuildAxis.Y;
            }
        }

        private static SmartBuildAxis FirstAxisPerpendicularTo(DecorationEditAxis axis)
        {
            switch (axis)
            {
                case DecorationEditAxis.X:
                    return SmartBuildAxis.Z;
                case DecorationEditAxis.Y:
                    return SmartBuildAxis.X;
                default:
                    return SmartBuildAxis.X;
            }
        }

        private enum AdvancedRectangleAction
        {
            WallFill,
            WallPerimeter,
            ActivePlaneFill
        }
    }
}
