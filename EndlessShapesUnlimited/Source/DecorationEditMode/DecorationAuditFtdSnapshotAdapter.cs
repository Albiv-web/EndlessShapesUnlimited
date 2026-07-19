using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using DecoLimitLifter.SerializationHud;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationAuditFtdMetadataContext
    {
        private readonly Func<Guid, DecorationAuditMeshMetadata> _meshResolver;
        private readonly Func<Guid, DecorationAuditReferenceState> _materialResolver;
        private readonly Func<AllConstruct, Decoration, string> _layerResolver;
        private readonly Func<AllConstruct, Decoration, bool> _workspaceLockResolver;

        internal DecorationAuditFtdMetadataContext(
            Func<Guid, DecorationAuditMeshMetadata> meshResolver,
            Func<Guid, DecorationAuditReferenceState> materialResolver,
            Func<AllConstruct, Decoration, string> layerResolver,
            Func<AllConstruct, Decoration, bool> workspaceLockResolver,
            IEnumerable<DecorationAuditLayerSnapshot> layers)
        {
            _meshResolver = meshResolver;
            _materialResolver = materialResolver;
            _layerResolver = layerResolver;
            _workspaceLockResolver = workspaceLockResolver;
            Layers = (layers ?? Enumerable.Empty<DecorationAuditLayerSnapshot>()).ToArray();
        }

        internal IReadOnlyList<DecorationAuditLayerSnapshot> Layers { get; }

        internal DecorationAuditMeshMetadata ResolveMesh(Guid meshGuid)
        {
            if (meshGuid == Guid.Empty)
            {
                return new DecorationAuditMeshMetadata(
                    DecorationAuditReferenceState.Missing,
                    diagnostic: "The decoration stores an empty mesh GUID.");
            }
            if (_meshResolver == null)
                return DecorationAuditMeshMetadata.Unknown;
            try
            {
                return _meshResolver(meshGuid) ?? DecorationAuditMeshMetadata.Unknown;
            }
            catch (Exception exception)
            {
                return new DecorationAuditMeshMetadata(
                    DecorationAuditReferenceState.Unreadable,
                    diagnostic: exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal DecorationAuditReferenceState ResolveMaterial(Guid materialGuid)
        {
            if (materialGuid == Guid.Empty)
                return DecorationAuditReferenceState.NotApplicable;
            if (_materialResolver == null)
                return DecorationAuditReferenceState.Unknown;
            try
            {
                return _materialResolver(materialGuid);
            }
            catch
            {
                return DecorationAuditReferenceState.Unreadable;
            }
        }

        internal string ResolveLayer(AllConstruct construct, Decoration decoration)
        {
            if (_layerResolver == null)
                return string.Empty;
            try
            {
                return _layerResolver(construct, decoration) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        internal bool ResolveWorkspaceLock(AllConstruct construct, Decoration decoration)
        {
            if (_workspaceLockResolver == null)
                return false;
            try
            {
                return _workspaceLockResolver(construct, decoration);
            }
            catch
            {
                // If metadata cannot be read, the apply adapter still performs
                // an independent live lock preflight before any mutation.
                return false;
            }
        }
    }

    // All live FtD reads are isolated here. DecorationAuditEngine consumes only
    // immutable domain snapshots and has no dependency on FtD or Unity types.
    internal static class DecorationAuditFtdSnapshotAdapter
    {
        internal static DecorationAuditCraftSnapshot Capture(
            MainConstruct mainConstruct,
            SerializationForecast forecast = null,
            string sourceId = null,
            DecorationAuditFtdMetadataContext metadata = null)
        {
            if (mainConstruct == null)
                throw new ArgumentNullException(nameof(mainConstruct));

            sourceId = string.IsNullOrWhiteSpace(sourceId)
                ? "ftd-mainconstruct-" +
                  RuntimeHelpers.GetHashCode(mainConstruct).ToString("X8", CultureInfo.InvariantCulture)
                : sourceId.Trim();

            var constructs = new List<AllConstruct>();
            string enumerationFailure = null;
            try
            {
                mainConstruct.AllBasicsRestricted?.GetAllConstructsBelowUsAndIncludingUs(constructs);
            }
            catch (Exception exception)
            {
                enumerationFailure = "FtD construct enumeration failed: " +
                                     exception.GetType().Name + ": " + exception.Message;
            }

            var managers = new List<DecorationAuditManagerSnapshot>();
            for (int index = 0; index < constructs.Count; index++)
            {
                AllConstruct construct = constructs[index];
                var decorations = construct?.Decorations as AllConstructDecorations;
                if (decorations == null)
                    continue;

                managers.Add(CaptureManager(construct, decorations, index, metadata));
            }

            if (!string.IsNullOrEmpty(enumerationFailure))
            {
                managers.Add(new DecorationAuditManagerSnapshot(
                    "capture-error",
                    Array.Empty<DecorationAuditDecorationSnapshot>(),
                    "Incomplete craft capture",
                    captureComplete: false,
                    captureDiagnostic: enumerationFailure));
            }

            return new DecorationAuditCraftSnapshot(
                sourceId,
                managers,
                CaptureSerialization(forecast),
                "FtD craft at design revision " +
                mainConstruct.DesignChangeCounter.ToString(CultureInfo.InvariantCulture),
                metadata?.Layers);
        }

        private static DecorationAuditManagerSnapshot CaptureManager(
            AllConstruct construct,
            AllConstructDecorations manager,
            int constructIndex,
            DecorationAuditFtdMetadataContext metadata)
        {
            string managerId = "construct-" +
                               constructIndex.ToString("D6", CultureInfo.InvariantCulture);
            var diagnostics = new List<string>();
            int reportedCount = 0;
            try
            {
                reportedCount = Math.Max(0, manager.DecorationCount);
            }
            catch (Exception exception)
            {
                diagnostics.Add("DecorationCount: " + exception.GetType().Name);
            }

            Decoration[] live;
            try
            {
                live = manager.DecorationList?.ToArray() ?? Array.Empty<Decoration>();
            }
            catch (Exception exception)
            {
                live = Array.Empty<Decoration>();
                diagnostics.Add(
                    "DecorationList: " + exception.GetType().Name + ": " + exception.Message);
            }

            var snapshots = new List<DecorationAuditDecorationSnapshot>(live.Length);
            for (int index = 0; index < live.Length; index++)
            {
                Decoration decoration = live[index];
                if (decoration == null)
                    continue;

                try
                {
                    if (decoration.IsDeleted)
                        continue;
                    snapshots.Add(CaptureDecoration(
                        construct,
                        manager,
                        decoration,
                        index,
                        metadata));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(
                        "Decoration " + index.ToString(CultureInfo.InvariantCulture) + ": " +
                        exception.GetType().Name + ": " + exception.Message);
                    snapshots.Add(UnreadableDecoration(index));
                }
            }

            bool complete = diagnostics.Count == 0;
            return new DecorationAuditManagerSnapshot(
                managerId,
                snapshots,
                "Construct decoration manager " +
                constructIndex.ToString(CultureInfo.InvariantCulture),
                reportedCount,
                DecoLimits.MaxDecorations,
                complete,
                complete ? string.Empty : string.Join("; ", diagnostics.Take(8)));
        }

        private static DecorationAuditDecorationSnapshot CaptureDecoration(
            AllConstruct construct,
            AllConstructDecorations manager,
            Decoration decoration,
            int decorationIndex,
            DecorationAuditFtdMetadataContext metadata)
        {
            Vector3i tether = decoration.TetherPoint.Us;
            DecorationAuditTetherState tetherState = ResolveTetherState(
                construct,
                manager,
                decoration,
                tether);
            Vector3 position = decoration.Positioning.Us;
            Vector3 scale = decoration.Scaling.Us;
            Vector3 orientation = decoration.Orientation.Us;
            Guid meshGuid = decoration.MeshGuid.Us;
            Guid materialReplacement = decoration.MaterialReplacement.Us;
            uint uniqueId = decoration.UniqueId;
            string decorationId = "decoration-" +
                                  decorationIndex.ToString("D8", CultureInfo.InvariantCulture);

            return new DecorationAuditDecorationSnapshot(
                decorationId,
                new DecorationAuditCell(tether.x, tether.y, tether.z),
                tetherState,
                ToAuditVector(position),
                ToAuditVector(scale),
                ToAuditVector(orientation),
                meshGuid,
                decoration.Color.Us,
                "Decoration " + decorationIndex.ToString(CultureInfo.InvariantCulture) +
                " / " + meshGuid.ToString("N").Substring(0, 8),
                storageKey: uniqueId == 0U
                    ? null
                    : uniqueId.ToString(CultureInfo.InvariantCulture),
                materialReplacement: materialReplacement,
                hideOriginalMesh: decoration.HideOriginalMesh.Us,
                meshMetadata: metadata?.ResolveMesh(meshGuid),
                materialReferenceState: metadata?.ResolveMaterial(materialReplacement) ??
                    DecorationAuditReferenceState.Unknown,
                layerName: metadata?.ResolveLayer(construct, decoration),
                workspaceLocked: metadata?.ResolveWorkspaceLock(construct, decoration) ?? false);
        }

        private static DecorationAuditTetherState ResolveTetherState(
            AllConstruct construct,
            AllConstructDecorations manager,
            Decoration decoration,
            Vector3i tether)
        {
            try
            {
                if (!ReferenceEquals(decoration.OurManager, manager))
                    return DecorationAuditTetherState.ManagerMismatch;
                if (construct?.AllBasics == null)
                    return DecorationAuditTetherState.Unreadable;
                return construct.AllBasics.GetBlockViaLocalPosition(tether) == null
                    ? DecorationAuditTetherState.MissingBlock
                    : DecorationAuditTetherState.Valid;
            }
            catch
            {
                return DecorationAuditTetherState.Unreadable;
            }
        }

        private static DecorationAuditDecorationSnapshot UnreadableDecoration(int index)
        {
            string decorationId = "decoration-" +
                                  index.ToString("D8", CultureInfo.InvariantCulture);
            var invalid = new DecorationAuditVector3(double.NaN, double.NaN, double.NaN);
            return new DecorationAuditDecorationSnapshot(
                decorationId,
                new DecorationAuditCell(0, 0, 0),
                DecorationAuditTetherState.Unreadable,
                invalid,
                invalid,
                invalid,
                Guid.Empty,
                0,
                "Unreadable decoration " + index.ToString(CultureInfo.InvariantCulture));
        }

        private static DecorationAuditSerializationSnapshot CaptureSerialization(
            SerializationForecast forecast)
        {
            if (forecast == null)
                return DecorationAuditSerializationSnapshot.Unavailable;

            return new DecorationAuditSerializationSnapshot(
                available: true,
                exact: forecast.Exact,
                uncalibrated: forecast.Uncalibrated,
                wireFormat: forecast.Format.ToString(),
                peakHeaderBytes: forecast.PeakHeaderBytes,
                peakDataBytes: forecast.PeakDataBytes,
                legacyHeaderMaximum: SerializationForecastCalculator.LegacyHeaderMaximum,
                legacyDataMaximum: SerializationForecastCalculator.LegacyDataMaximum,
                maximumHeaderBytes: DecoLimits.MaxHeaderBytes,
                maximumDataBytes: DecoLimits.MaxDataSortedBytes,
                largestBlueprintStreamBytes: forecast.LargestStreamBytes,
                maximumSaveBufferBytes: DecoLimits.MaxSaveBufferBytes,
                requiresModBuffer: forecast.RequiresModBuffer);
        }

        private static DecorationAuditVector3 ToAuditVector(Vector3 value) =>
            new DecorationAuditVector3(value.x, value.y, value.z);
    }
}
