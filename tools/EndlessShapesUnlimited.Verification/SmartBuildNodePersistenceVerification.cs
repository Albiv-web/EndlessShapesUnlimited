using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using DecoLimitLifter.SmartBuildMode;
using EndlessShapes2;
using Newtonsoft.Json;

internal static class SmartBuildNodePersistenceVerification
{
    private const string LegacyV1Json = @"
{
  ""schema_version"": 1,
  ""anchor"": { ""x"": 10, ""y"": 20, ""z"": 30 },
  ""pieces"": [
    {
      ""shape"": 0,
      ""descriptor"": ""cuboid"",
      ""origin_offset"": { ""x"": 0, ""y"": 0, ""z"": 0 },
      ""size"": { ""x"": 2, ""y"": 1, ""z"": 1 },
      ""draw_plane"": 0,
      ""slope_length"": 1,
      ""slope_steps"": 1,
      ""slope_width"": 1,
      ""selected_length"": 1,
      ""fixed_forward_tiles"": 1,
      ""fixed_forward_cells"": 1,
      ""fixed_right_tiles"": 1,
      ""fixed_drop_tiles"": 1,
      ""support"": 0,
      ""generator_sides"": 0,
      ""generator_fill"": 0,
      ""generator_smoothing"": 0,
      ""generator_round_lock"": true,
      ""generator_arc_degrees"": 360,
      ""generator_top_scale_percent"": 100,
      ""cuboid_hollow"": false,
      ""forward_axis"": 2,
      ""forward_sign"": 1,
      ""right_axis"": 0,
      ""right_sign"": 1,
      ""drop_axis"": 1,
      ""drop_sign"": 1,
      ""material"": 0
    }
  ],
  ""selected_indexes"": [0],
  ""primary_index"": 0,
  ""active_material"": 0,
  ""tool"": 3,
  ""selected_shape"": 0,
  ""selected_descriptor"": ""cuboid"",
  ""selected_length"": 1,
  ""draw_plane"": 0,
  ""handle_mode"": 3,
  ""default_support"": 0,
  ""preview_mode"": 1,
  ""occupancy_mode"": 0
}";

    internal static string Run()
    {
        VerifyGoldenV1Migration();
        VerifyV2PatternAndRegionRoundTrip();
        return "PASS: Smart Builder node persistence verification completed.";
    }

    private static void VerifyGoldenV1Migration()
    {
        SmartBuildScenePresetPayload migrated = DeserializeLegacy();
        Require(
            migrated.SchemaVersion == SmartBuildScenePresetPayload.LegacyPrimitiveSchemaVersion,
            "the golden legacy payload did not begin as schema v1");
        Require(
            migrated.TryValidate(out string migrationReason),
            "the golden schema-v1 payload did not migrate: " + migrationReason);
        Require(
            migrated.SchemaVersion == SmartBuildScenePresetPayload.CurrentSchemaVersion &&
            migrated.Nodes.Count() == 1 &&
            migrated.Nodes[0].TryGetKind(out SmartBuildSceneNodeKind migratedKind) &&
            migratedKind == SmartBuildSceneNodeKind.Primitive,
            "schema v1 pieces were not migrated to schema-v2 primitive nodes");

        string migratedJson = JsonConvert.SerializeObject(migrated, Formatting.None);
        Require(
            Contains(migratedJson, "\"schema_version\":2") &&
            Contains(migratedJson, "\"nodes\"") &&
            Contains(migratedJson, "\"kind\":\"primitive\"") &&
            !Contains(migratedJson, "\"pieces\""),
            "migrated schema-v2 JSON did not use the canonical node representation");

        AllConstruct construct = NewConstruct();
        Vector3i relocatedAnchor = new Vector3i(100, 200, 300);
        Require(
            migrated.TryRestoreScene(
                construct,
                relocatedAnchor,
                out SmartBuildPieceScene scene,
                out IReadOnlyList<int> selectedIds,
                out int primaryId,
                out string restoreReason),
            "the migrated schema-v1 payload did not restore as a scene: " + restoreReason);
        SmartBuildPiece restored = scene.Pieces.Single();
        Require(
            scene.Nodes.Count == 1 &&
            scene.Nodes[0].Kind == SmartBuildSceneNodeKind.Primitive &&
            restored.Origin.Equals(relocatedAnchor) &&
            restored.PresetCuboidSize.Equals(new Vector3i(2, 1, 1)) &&
            selectedIds.SequenceEqual(new[] { restored.Id }) &&
            primaryId == restored.Id &&
            scene.SelectedPiece?.Id == restored.Id,
            "migrated primitive geometry or selection state changed during restore");
        Require(
            migrated.ActiveMaterial == SmartBuildMaterial.Wood &&
            migrated.Tool == SmartBuildTool.Rotate &&
            migrated.SelectedShape == SmartBuildShapeKind.Cuboid &&
            migrated.SelectedShapeDescriptorKey == SmartBuildShapeDescriptors.CuboidKey &&
            migrated.SelectedSlopeLength == 1 &&
            migrated.DrawPlane == SmartBuildDrawPlane.Camera &&
            migrated.EditHandleMode == SmartBuildEditHandleMode.Corner &&
            migrated.DefaultSlopeSupportMode == SmartBuildSlopeSupportMode.Full &&
            migrated.PreviewMode == SmartBuildPreviewMode.Material &&
            migrated.OccupancyMode == SmartBuildOccupancyMode.SkipOccupied,
            "schema-v1 editor state changed during migration");

        SmartBuildScenePresetPayload legacyCompatibility = DeserializeLegacy();
        Require(
            legacyCompatibility.TryRestore(
                construct,
                null,
                out IReadOnlyList<SmartBuildPiece> restoredPieces,
                out IReadOnlyList<int> legacySelectedIds,
                out int legacyPrimaryId,
                out string legacyReason) &&
            restoredPieces.Count == 1 &&
            legacySelectedIds.Count == 1 &&
            legacyPrimaryId == restoredPieces[0].Id,
            "the existing TryRestore overload lost schema-v1 compatibility: " + legacyReason);
    }

    private static void VerifyV2PatternAndRegionRoundTrip()
    {
        AllConstruct construct = NewConstruct();
        var sourceScene = new SmartBuildPieceScene(construct);
        SmartBuildPiece firstSource = SmartBuildPiece.CreateCuboid(
            construct,
            new Vector3i(10, 4, 6),
            new Vector3i(2, 1, 1),
            SmartBuildDrawPlane.XZ);
        SmartBuildPiece patternHost = SmartBuildPiece.CreateCuboid(
            construct,
            new Vector3i(12, 4, 6),
            SmartBuildDrawPlane.XZ);
        Require(sourceScene.Add(firstSource) && sourceScene.Add(patternHost),
            "the pattern test sources could not be added");
        sourceScene.SetSelection(
            new[] { firstSource.Id, patternHost.Id },
            patternHost.Id);
        var definition = new SmartBuildPatternDefinition
        {
            Kind = SmartBuildEditablePatternKind.Grid,
            PrimaryStep = new Vector3i(3, 0, 0),
            SecondaryStep = new Vector3i(0, 0, 4),
            PrimaryBefore = 1,
            PrimaryAfter = 2,
            SecondaryBefore = 0,
            SecondaryAfter = 1
        };
        Require(
            sourceScene.TryCreatePatternFromSelection(
                definition,
                out SmartBuildPatternNode sourcePattern,
                out string patternReason),
            "the editable pattern test node could not be created: " + patternReason);

        SmartBuildPiece regionHost = SmartBuildPiece.CreateCuboid(
            construct,
            new Vector3i(30, 2, 8),
            SmartBuildDrawPlane.YZ);
        Require(sourceScene.Add(regionHost), "the region test host could not be added");
        var regionCells = new[]
        {
            new Vector3i(30, 2, 8),
            new Vector3i(31, 2, 8),
            new Vector3i(32, 2, 8),
            new Vector3i(30, 3, 8)
        };
        Require(
            sourceScene.TryCreateRegionFromSelection(
                SmartBuildRegionKind.Wall,
                regionCells,
                out SmartBuildRegionNode sourceRegion,
                out string regionReason),
            "the editable region test node could not be created: " + regionReason);
        sourceScene.SetSelection(
            new[] { sourcePattern.Id, sourceRegion.Id },
            sourceRegion.Id);

        SmartBuildScenePresetPayload captured = SmartBuildScenePresetPayload.Capture(
            sourceScene,
            SmartBuildMaterial.Alloy,
            SmartBuildTool.Scale,
            SmartBuildShapeKind.Cuboid,
            SmartBuildShapeDescriptors.CuboidKey,
            4,
            SmartBuildDrawPlane.YZ,
            SmartBuildEditHandleMode.Edge,
            SmartBuildSlopeSupportMode.Step,
            SmartBuildPreviewMode.MaterialOnly,
            SmartBuildOccupancyMode.EraseOccupied);
        Require(captured.TryValidate(out string captureReason),
            "the captured schema-v2 scene was invalid: " + captureReason);

        string json = JsonConvert.SerializeObject(captured, Formatting.None);
        Require(
            Contains(json, "\"schema_version\":2") &&
            Contains(json, "\"kind\":\"pattern\"") &&
            Contains(json, "\"kind\":\"grid\"") &&
            Contains(json, "\"kind\":\"region\"") &&
            Contains(json, "\"kind\":\"wall\"") &&
            Contains(json, "\"sources\"") &&
            Contains(json, "\"spans\"") &&
            !Contains(json, "\"pieces\""),
            "schema-v2 JSON did not use explicit node, source, pattern, and region payloads");

        SmartBuildScenePresetPayload reloaded =
            JsonConvert.DeserializeObject<SmartBuildScenePresetPayload>(json);
        Require(reloaded != null, "schema-v2 JSON deserialized to null");
        Vector3i relocatedAnchor = new Vector3i(100, -50, 25);
        Require(
            reloaded.TryRestoreScene(
                construct,
                relocatedAnchor,
                out SmartBuildPieceScene restoredScene,
                out IReadOnlyList<int> selectedIds,
                out int primaryId,
                out string restoreReason),
            "the schema-v2 editable scene did not restore: " + restoreReason);

        Require(
            restoredScene.Nodes.Count == 2 &&
            restoredScene.EditableNodeCount == 2 &&
            restoredScene.Nodes[0] is SmartBuildPatternNode &&
            restoredScene.Nodes[1] is SmartBuildRegionNode,
            "restored editable node identity or ordering changed");
        var restoredPattern = (SmartBuildPatternNode)restoredScene.Nodes[0];
        SmartBuildPatternDefinition restoredDefinition = restoredPattern.Definition;
        Require(
            restoredPattern.SourcePieces.Count == 2 &&
            restoredPattern.HostPiece.Origin.Equals(new Vector3i(100, -48, 25)) &&
            restoredPattern.SourcePieces.Any(
                source => source.Origin.Equals(new Vector3i(98, -48, 25))) &&
            restoredDefinition.Kind == SmartBuildEditablePatternKind.Grid &&
            restoredDefinition.PrimaryStep.Equals(new Vector3i(3, 0, 0)) &&
            restoredDefinition.SecondaryStep.Equals(new Vector3i(0, 0, 4)) &&
            restoredDefinition.PrimaryBefore == 1 &&
            restoredDefinition.PrimaryAfter == 2 &&
            restoredDefinition.SecondaryBefore == 0 &&
            restoredDefinition.SecondaryAfter == 1,
            "editable pattern sources or definition changed during round-trip");

        var restoredRegion = (SmartBuildRegionNode)restoredScene.Nodes[1];
        Vector3i[] restoredRegionCells = restoredRegion.Spans
            .SelectMany(span => span.Cells())
            .ToArray();
        Require(
            restoredRegion.RegionKind == SmartBuildRegionKind.Wall &&
            restoredRegion.HostPiece.Origin.Equals(new Vector3i(118, -50, 27)) &&
            restoredRegionCells.Length == 4 &&
            restoredRegionCells.Contains(new Vector3i(118, -50, 27)) &&
            restoredRegionCells.Contains(new Vector3i(120, -50, 27)) &&
            restoredRegionCells.Contains(new Vector3i(118, -49, 27)),
            "editable region kind, spans, or relocation changed during round-trip");
        Require(
            selectedIds.SequenceEqual(
                new[] { restoredPattern.Id, restoredRegion.Id }) &&
            primaryId == restoredRegion.Id &&
            restoredScene.SelectedPiece?.Id == restoredRegion.Id &&
            reloaded.ActiveMaterial == SmartBuildMaterial.Alloy &&
            reloaded.Tool == SmartBuildTool.Scale &&
            reloaded.SelectedSlopeLength == 4 &&
            reloaded.DrawPlane == SmartBuildDrawPlane.YZ &&
            reloaded.EditHandleMode == SmartBuildEditHandleMode.Edge &&
            reloaded.DefaultSlopeSupportMode == SmartBuildSlopeSupportMode.Step &&
            reloaded.PreviewMode == SmartBuildPreviewMode.MaterialOnly &&
            reloaded.OccupancyMode == SmartBuildOccupancyMode.EraseOccupied,
            "selection or editor state changed during schema-v2 round-trip");

        var pathDefinition = new SmartBuildPatternDefinition
        {
            Kind = SmartBuildEditablePatternKind.Polyline,
            PathPoints = new[]
            {
                new Vector3i(10, 4, 6),
                new Vector3i(14, 4, 6),
                new Vector3i(14, 4, 10)
            },
            PathMode = SmartBuildPathArrayMode.SteppedCells,
            PathOrientation = SmartBuildPolylineOrientationMode.CardinalTangent,
            PathSpacingCells = 2
        };
        SmartBuildPatternDefinitionPresetPayload pathPayload =
            SmartBuildPatternDefinitionPresetPayload.Capture(
                pathDefinition,
                new Vector3i(10, 4, 6));
        string pathJson = JsonConvert.SerializeObject(pathPayload, Formatting.None);
        SmartBuildPatternDefinitionPresetPayload restoredPathPayload =
            JsonConvert.DeserializeObject<SmartBuildPatternDefinitionPresetPayload>(pathJson);
        Require(restoredPathPayload != null, "polyline schema-v2 payload deserialized to null");
        bool restoredPathValid = restoredPathPayload.TryRestore(
            new Vector3i(100, -50, 25),
            1,
            out SmartBuildPatternDefinition restoredPath,
            out string pathReason);
        Require(
            Contains(pathJson, "\"path_orientation\":\"cardinal_tangent\"") &&
            restoredPathValid &&
            restoredPath.PathOrientation == SmartBuildPolylineOrientationMode.CardinalTangent &&
            restoredPath.PathPoints.SequenceEqual(
                new[]
                {
                    new Vector3i(100, -50, 25),
                    new Vector3i(104, -50, 25),
                    new Vector3i(104, -50, 29)
                }),
            "polyline orientation or control points changed during schema-v2 round-trip: " +
            pathReason);

        string preOrientationJson = pathJson.Replace(
            ",\"path_orientation\":\"cardinal_tangent\"",
            string.Empty);
        SmartBuildPatternDefinitionPresetPayload compatiblePathPayload =
            JsonConvert.DeserializeObject<SmartBuildPatternDefinitionPresetPayload>(
                preOrientationJson);
        Require(
            compatiblePathPayload != null,
            "pre-orientation schema-v2 payload deserialized to null");
        bool compatiblePathValid = compatiblePathPayload.TryRestore(
            new Vector3i(10, 4, 6),
            1,
            out SmartBuildPatternDefinition compatiblePath,
            out string compatibilityReason);
        Require(
            compatiblePathValid &&
            compatiblePath.PathOrientation == SmartBuildPolylineOrientationMode.Keep,
            "schema-v2 payloads written before path orientation was added no longer default to Keep: " +
            compatibilityReason);
    }

    private static SmartBuildScenePresetPayload DeserializeLegacy()
    {
        SmartBuildScenePresetPayload payload =
            JsonConvert.DeserializeObject<SmartBuildScenePresetPayload>(LegacyV1Json);
        Require(payload != null, "the golden schema-v1 payload deserialized to null");
        return payload;
    }

    private static AllConstruct NewConstruct() =>
        (AllConstruct)FormatterServices.GetUninitializedObject(
            typeof(TelescopicPistonSubConstructable));

    private static bool Contains(string value, string expected) =>
        value?.IndexOf(expected, StringComparison.Ordinal) >= 0;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                "Smart Builder node persistence verification failed: " + message);
        }
    }
}
