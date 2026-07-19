using System;
using System.IO;
using System.Linq;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.Presets;
using DecoLimitLifter.DecorationEditMode;
using EndlessShapes2.Polygon;
using UnityEngine;

internal static partial class Program
{
    private sealed class PresetLibraryFixture
    {
        public string Label { get; set; }

        public int Count { get; set; }
    }

    private static void VerifyPresetLibrary()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "esu-preset-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            DateTime clock = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
            var library = new EsuPresetLibrary(root, () => clock);
            bool saved = library.TrySave(
                " Hull rib ",
                EsuPresetKind.SmartScene,
                new PresetLibraryFixture { Label = "first", Count = 3 },
                overwrite: false,
                out EsuPresetEntry first,
                out string saveMessage,
                " reusable frame ",
                new[] { "Hull", " frame ", "hull" });
            Assert(saved && first != null, "Preset library saves a typed, named Smart scene payload. " + saveMessage);
            Assert(
                first.Name == "Hull rib" &&
                first.Description == "reusable frame" &&
                first.Tags.SequenceEqual(new[] { "frame", "Hull" }) &&
                first.PayloadSha256 == EsuPresetLibrary.HashPayload(first.PayloadJson),
                "Preset library normalizes names, descriptions, tags, and payload hashes deterministically.");

            bool listed = library.TryList(out var visible, out string listMessage);
            Assert(
                listed && visible.Count == 1 && visible[0].Id == first.Id,
                "Preset library lists normal presets without exposing recovery slots. " + listMessage);

            bool read = library.TryRead(
                first.Id,
                out PresetLibraryFixture loaded,
                out EsuPresetEntry loadedEntry,
                out string readMessage);
            Assert(
                read && loaded?.Label == "first" && loaded.Count == 3 && loadedEntry.Id == first.Id,
                "Preset library round-trips typed payloads by stable ID. " + readMessage);

            bool duplicate = library.TrySave(
                "hull RIB",
                EsuPresetKind.SmartScene,
                new PresetLibraryFixture { Label = "duplicate", Count = 4 },
                overwrite: false,
                out _,
                out _);
            Assert(!duplicate, "Preset library rejects case-insensitive duplicate names within one preset kind.");

            clock = clock.AddMinutes(1);
            bool overwritten = library.TrySave(
                "Hull rib",
                EsuPresetKind.SmartScene,
                new PresetLibraryFixture { Label = "second", Count = 7 },
                overwrite: true,
                out EsuPresetEntry second,
                out string overwriteMessage);
            Assert(
                overwritten && second.Id == first.Id && second.CreatedUtc == first.CreatedUtc && second.UpdatedUtc != first.UpdatedUtc,
                "Preset overwrite preserves identity/creation time while advancing the update time. " + overwriteMessage);

            bool recoverySaved = library.TrySaveRecovery(
                "smart-builder",
                EsuPresetKind.SmartScene,
                new PresetLibraryFixture { Label = "recovery", Count = 11 },
                out string recoverySaveMessage);
            bool recoveryRead = library.TryReadRecovery(
                "SMART-builder",
                out PresetLibraryFixture recovery,
                out EsuPresetEntry recoveryEntry,
                out string recoveryReadMessage);
            listed = library.TryList(out visible, out _);
            bool listedAll = library.TryList(out var allEntries, out _, includeRecovery: true);
            Assert(
                recoverySaved && recoveryRead && recovery?.Count == 11 && recoveryEntry.IsRecovery &&
                listed && visible.Count == 1 && listedAll && allEntries.Count == 2,
                "Recovery slots round-trip case-insensitively and stay out of the normal preset browser. " +
                recoverySaveMessage + " " + recoveryReadMessage);

            bool recoveryCleared = library.TryClearRecovery("smart-builder", out string recoveryClearMessage);
            bool recoveryStillReads = library.TryReadRecovery<PresetLibraryFixture>(
                "smart-builder",
                out _,
                out _,
                out _);
            Assert(
                recoveryCleared && !recoveryStillReads,
                "Preset recovery slots clear explicitly after Apply/Cancel. " + recoveryClearMessage);

            bool renamed = library.TryRename(
                second.Id,
                "Hull frame",
                out EsuPresetEntry renamedEntry,
                out string renameMessage);
            Assert(
                renamed && renamedEntry.Id == second.Id && renamedEntry.Name == "Hull frame",
                "Preset rename preserves stable identity. " + renameMessage);

            Assert(
                !library.TrySaveJson("../escape", EsuPresetKind.SurfaceDraft, "{}", false, out _, out _) &&
                !library.TrySaveJson("bad", (EsuPresetKind)999, "{}", false, out _, out _) &&
                !library.TrySaveJson("bad", EsuPresetKind.SurfaceDraft, "{", false, out _, out _),
                "Preset validation rejects path-like names, unknown kinds, and malformed JSON before writing.");

            string oversized = "{\"value\":\"" + new string('x', EsuPresetLibrary.MaximumPayloadBytes) + "\"}";
            Assert(
                !library.TrySaveJson("oversized", EsuPresetKind.SurfaceDraft, oversized, false, out _, out _),
                "Preset library rejects payloads above its bounded per-entry byte ceiling.");

            string validPrimary = File.ReadAllText(library.FilePath);
            Assert(
                !validPrimary.Contains("$type") &&
                Directory.GetFiles(library.DirectoryPath, "*.pending-*", SearchOption.TopDirectoryOnly).Length == 0,
                "Preset files contain data-only JSON and leave no completed transaction debris.");

            File.WriteAllText(library.FilePath, "{not-json");
            bool recoveredBackup = library.TryList(out var recoveredEntries, out string backupMessage, includeRecovery: true);
            Assert(
                recoveredBackup && recoveredEntries.Count >= 1 && backupMessage.Contains("backup"),
                "Preset library falls back to its last known-good transactional backup when the primary file is corrupt.");

            File.WriteAllText(library.FilePath, validPrimary);
            bool deleted = library.TryDelete(renamedEntry.Id, out string deleteMessage);
            listed = library.TryList(out visible, out _);
            Assert(
                deleted && listed && visible.Count == 0,
                "Preset deletion persists without affecting recovery semantics. " + deleteMessage);

            VerifyPortableDraftPresetPayloads(library);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void VerifyPortableDraftPresetPayloads(EsuPresetLibrary library)
    {
        AllConstruct constructA = NewConstructIdentityForTests();
        AllConstruct constructB = NewConstructIdentityForTests();
        var surface = new SurfaceDraft();
        surface.SetConstructForTests(constructA);
        surface.AddPointForTests(new Vector3(2f, 3f, 4f));
        surface.AddPointForTests(new Vector3(4f, 3f, 4f));
        surface.AddPointForTests(new Vector3(2f, 5f, 4f));
        surface.Settings.ColorIndex = 7;
        surface.Settings.FaceThickness = 0.125f;
        surface.Settings.StructureBlockType = StructureBlockType.Alloy;
        Assert(
            surface.TryAddFace(0, 1, 2, out string surfaceMessage) &&
            surface.TrySetSharedAnchor(constructA, new Vector3i(3, 3, 4), out surfaceMessage),
            "Surface preset verification creates a styled, anchored source draft. " + surfaceMessage);

        EsuSurfaceDraftPresetPayload surfacePayload =
            EsuSurfaceDraftPresetPayload.Capture(surface, preserveSelection: false);
        Assert(
            surfacePayload.TryValidate(out surfaceMessage),
            "Surface draft capture produces a bounded portable payload. " + surfaceMessage);
        bool surfaceSaved = library.TrySave(
            "Surface fixture",
            EsuPresetKind.SurfaceDraft,
            surfacePayload,
            overwrite: false,
            out EsuPresetEntry surfaceEntry,
            out surfaceMessage);
        bool surfaceRead = library.TryRead(
            surfaceEntry?.Id,
            out EsuSurfaceDraftPresetPayload loadedSurface,
            out _,
            out string surfaceReadMessage);
        SurfaceDraftSnapshot restoredSurfaceSnapshot = null;
        string surfaceRestoreMessage = "Surface preset was not read.";
        bool surfaceRestored = surfaceRead && loadedSurface.TryCreateSnapshot(
            constructB,
            new Vector3(20f, 30f, 40f),
            out restoredSurfaceSnapshot,
            out surfaceRestoreMessage);
        var restoredSurface = new SurfaceDraft();
        if (surfaceRestored)
            restoredSurface.Restore(restoredSurfaceSnapshot);
        Assert(
            surfaceSaved && surfaceRestored &&
            ReferenceEquals(restoredSurface.Construct, constructB) &&
            restoredSurface.Points.Count == 3 &&
            VectorApproximately(restoredSurface.Points[0], new Vector3(20f, 30f, 40f), 0.00001f) &&
            VectorApproximately(restoredSurface.Points[1], new Vector3(22f, 30f, 40f), 0.00001f) &&
            restoredSurface.Faces.Count == 1 &&
            restoredSurface.FaceStyles[0].ColorIndex == 7 &&
            restoredSurface.Settings.StructureBlockType == StructureBlockType.Alloy &&
            Math.Abs(restoredSurface.Settings.FaceThickness - 0.125f) < 0.00001f &&
            restoredSurface.HasSharedAnchor &&
            restoredSurface.SharedAnchor.x == 21 &&
            restoredSurface.SharedAnchor.y == 30 &&
            restoredSurface.SharedAnchor.z == 40,
            "Surface presets round-trip topology/style and translate points/anchors onto a different construct. " +
            surfaceMessage + " " + surfaceReadMessage + " " + surfaceRestoreMessage);

        var generatorDraft = new DecorationGeneratorDraft();
        generatorDraft.SetTool(SurfaceExtraTool.PartialCircle);
        bool centerSet = generatorDraft.TrySetCircleCenterWithBasis(
            constructA,
            new Vector3(5f, 6f, 7f),
            Vector3.right,
            Vector3.forward,
            out string generatorMessage);
        bool generatorAnchorSet = generatorDraft.TrySetSharedAnchor(
            constructA,
            new Vector3i(6, 6, 7),
            out generatorMessage);
        var generatorSettings = new DecorationGeneratorSettings
        {
            CircleRadius = 4.5f,
            CircleSegments = 32,
            ArcDegrees = 135f,
            ColorIndex = 9
        };
        Assert(
            centerSet && generatorAnchorSet,
            "Generator preset verification creates a centered, anchored source draft. " + generatorMessage);

        EsuGeneratorDraftPresetPayload generatorPayload =
            EsuGeneratorDraftPresetPayload.Capture(generatorDraft, generatorSettings, preserveSelection: false);
        Assert(
            generatorPayload.TryValidate(out generatorMessage),
            "Generator draft capture produces a bounded portable payload. " + generatorMessage);
        bool generatorSaved = library.TrySave(
            "Generator fixture",
            EsuPresetKind.GeneratorDraft,
            generatorPayload,
            overwrite: false,
            out EsuPresetEntry generatorEntry,
            out generatorMessage);
        bool generatorRead = library.TryRead(
            generatorEntry?.Id,
            out EsuGeneratorDraftPresetPayload loadedGenerator,
            out _,
            out string generatorReadMessage);
        DecorationGeneratorEditSnapshot restoredGeneratorSnapshot = null;
        string generatorRestoreMessage = "Generator preset was not read.";
        bool generatorRestored = generatorRead && loadedGenerator.TryCreateSnapshot(
            constructB,
            new Vector3(30f, 40f, 50f),
            out restoredGeneratorSnapshot,
            out generatorRestoreMessage);
        var restoredGeneratorDraft = new DecorationGeneratorDraft();
        var restoredGeneratorSettings = new DecorationGeneratorSettings();
        if (generatorRestored)
        {
            restoredGeneratorDraft.Restore(restoredGeneratorSnapshot.Draft);
            restoredGeneratorSnapshot.Settings.Restore(restoredGeneratorSettings);
        }
        Assert(
            generatorSaved && generatorRestored &&
            ReferenceEquals(restoredGeneratorDraft.Construct, constructB) &&
            restoredGeneratorDraft.Tool == SurfaceExtraTool.PartialCircle &&
            VectorApproximately(restoredGeneratorDraft.CircleCenter, new Vector3(30f, 40f, 50f), 0.00001f) &&
            restoredGeneratorDraft.HasSharedAnchor &&
            restoredGeneratorDraft.SharedAnchor.x == 31 &&
            restoredGeneratorDraft.SharedAnchor.y == 40 &&
            restoredGeneratorDraft.SharedAnchor.z == 50 &&
            Math.Abs(restoredGeneratorSettings.CircleRadius - 4.5f) < 0.00001f &&
            restoredGeneratorSettings.CircleSegments == 32 &&
            Math.Abs(restoredGeneratorSettings.ArcDegrees - 135f) < 0.00001f &&
            restoredGeneratorSettings.ColorIndex == 9,
            "Generator presets round-trip settings/basis and translate geometry/anchors onto a different construct. " +
            generatorMessage + " " + generatorReadMessage + " " + generatorRestoreMessage);
    }
}
