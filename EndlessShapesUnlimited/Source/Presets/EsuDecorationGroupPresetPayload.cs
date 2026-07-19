using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using DecoLimitLifter.DecorationEditMode;
using UnityEngine;

namespace DecoLimitLifter.Presets
{
    /// <summary>
    /// Portable, data-only decoration group. Anchors are stored relative to the
    /// primary decoration so a preset can be placed on a different construct.
    /// </summary>
    internal sealed class EsuDecorationGroupPresetPayload
    {
        internal const int CurrentSchemaVersion = 1;
        internal const int MaximumDecorations = 100000;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public int PrimaryIndex { get; set; }

        public EsuDecorationPresetItem[] Decorations { get; set; } =
            Array.Empty<EsuDecorationPresetItem>();

        internal static bool TryCapture(
            Decoration primary,
            IEnumerable<Decoration> selection,
            out EsuDecorationGroupPresetPayload payload,
            out string message)
        {
            payload = null;
            if (primary == null || primary.IsDeleted)
            {
                message = "Select a live primary decoration before saving a group preset.";
                return false;
            }

            var ordered = new List<Decoration> { primary };
            foreach (Decoration decoration in selection ?? Enumerable.Empty<Decoration>())
            {
                if (decoration == null || decoration.IsDeleted)
                {
                    message = "The decoration selection changed while the preset was being captured.";
                    return false;
                }
                if (!ReferenceEquals(decoration.OurManager, primary.OurManager))
                {
                    message = "A decoration group preset must come from one construct.";
                    return false;
                }
                if (!ordered.Contains(decoration))
                    ordered.Add(decoration);
            }

            if (ordered.Count > MaximumDecorations)
            {
                message = "Decoration group exceeds the 100,000-object preset safety limit.";
                return false;
            }

            Vector3i origin = primary.TetherPoint.Us;
            var items = new EsuDecorationPresetItem[ordered.Count];
            for (int index = 0; index < ordered.Count; index++)
            {
                var snapshot = new DecorationEditSnapshot(ordered[index]);
                if (!snapshot.HasFiniteTransform ||
                    !DecorationEditMath.IsWithinPositionLimit(snapshot.Positioning) ||
                    snapshot.Color < 0 || snapshot.Color > 31)
                {
                    message = "Decoration #" + (index + 1) + " has invalid transform or color data.";
                    return false;
                }

                items[index] = EsuDecorationPresetItem.FromSnapshot(snapshot, origin);
            }

            payload = new EsuDecorationGroupPresetPayload
            {
                SchemaVersion = CurrentSchemaVersion,
                PrimaryIndex = 0,
                Decorations = items
            };
            message = ordered.Count == 1
                ? "Captured 1 decoration."
                : "Captured " + ordered.Count + " decorations.";
            return true;
        }

        internal bool TryCreateSnapshots(
            Vector3i targetAnchor,
            out DecorationEditSnapshot[] snapshots,
            out string message)
        {
            snapshots = null;
            if (!TryValidate(out message))
                return false;

            var result = new DecorationEditSnapshot[Decorations.Length];
            for (int index = 0; index < Decorations.Length; index++)
            {
                EsuDecorationPresetItem item = Decorations[index];
                Vector3i tether = new Vector3i(
                    targetAnchor.x + item.RelativeAnchor.X,
                    targetAnchor.y + item.RelativeAnchor.Y,
                    targetAnchor.z + item.RelativeAnchor.Z);
                if (!item.TryCreateSnapshot(tether, out result[index], out message))
                {
                    message = "Decoration #" + (index + 1) + ": " + message;
                    return false;
                }
            }

            snapshots = result;
            message = result.Length == 1
                ? "Prepared 1 decoration."
                : "Prepared " + result.Length + " decorations.";
            return true;
        }

        internal bool TryValidate(out string message)
        {
            if (SchemaVersion != CurrentSchemaVersion)
            {
                message = "Decoration preset schema " + SchemaVersion + " is not supported.";
                return false;
            }
            Decorations = Decorations ?? Array.Empty<EsuDecorationPresetItem>();
            if (Decorations.Length == 0 || Decorations.Length > MaximumDecorations)
            {
                message = "Decoration preset must contain 1 through 100,000 objects.";
                return false;
            }
            if (PrimaryIndex < 0 || PrimaryIndex >= Decorations.Length)
            {
                message = "Decoration preset primary index is invalid.";
                return false;
            }

            for (int index = 0; index < Decorations.Length; index++)
            {
                message = "Decoration entry is missing.";
                if (Decorations[index] == null || !Decorations[index].TryValidate(out message))
                {
                    message = "Decoration #" + (index + 1) + ": " + message;
                    return false;
                }
            }

            message = "Decoration group preset is valid.";
            return true;
        }
    }

    internal sealed class EsuDecorationPresetItem
    {
        public EsuPresetCell RelativeAnchor { get; set; } = new EsuPresetCell();

        public EsuPresetVector3 Positioning { get; set; } = new EsuPresetVector3();

        public EsuPresetVector3 Scaling { get; set; } = new EsuPresetVector3(1f, 1f, 1f);

        public EsuPresetVector3 Orientation { get; set; } = new EsuPresetVector3();

        public string MeshGuid { get; set; } = Guid.Empty.ToString("D");

        public int Color { get; set; }

        public bool HideOriginalMesh { get; set; }

        public string MaterialReplacement { get; set; } = Guid.Empty.ToString("D");

        internal static EsuDecorationPresetItem FromSnapshot(
            DecorationEditSnapshot snapshot,
            Vector3i origin) =>
            new EsuDecorationPresetItem
            {
                RelativeAnchor = new EsuPresetCell(
                    snapshot.TetherPoint.x - origin.x,
                    snapshot.TetherPoint.y - origin.y,
                    snapshot.TetherPoint.z - origin.z),
                Positioning = EsuPresetVector3.From(snapshot.Positioning),
                Scaling = EsuPresetVector3.From(snapshot.Scaling),
                Orientation = EsuPresetVector3.From(snapshot.Orientation),
                MeshGuid = snapshot.MeshGuid.ToString("D"),
                Color = snapshot.Color,
                HideOriginalMesh = snapshot.HideOriginalMesh,
                MaterialReplacement = snapshot.MaterialReplacement.ToString("D")
            };

        internal bool TryValidate(out string message)
        {
            if (!Guid.TryParse(MeshGuid, out _) ||
                !Guid.TryParse(MaterialReplacement, out _))
            {
                message = "Mesh or material GUID is invalid.";
                return false;
            }
            if (!Positioning.IsFinite || !Scaling.IsFinite || !Orientation.IsFinite)
            {
                message = "Transform contains NaN or infinity.";
                return false;
            }
            Vector3 positioning = Positioning.ToVector3();
            Vector3 scaling = Scaling.ToVector3();
            if (!DecorationEditMath.IsWithinPositionLimit(positioning) ||
                Math.Abs(scaling.x) < 0.00001f ||
                Math.Abs(scaling.y) < 0.00001f ||
                Math.Abs(scaling.z) < 0.00001f)
            {
                message = "Positioning or scale is outside safe limits.";
                return false;
            }
            if (Color < 0 || Color > 31)
            {
                message = "Color must be between 0 and 31.";
                return false;
            }

            message = "Decoration preset item is valid.";
            return true;
        }

        internal bool TryCreateSnapshot(
            Vector3i tether,
            out DecorationEditSnapshot snapshot,
            out string message)
        {
            snapshot = null;
            if (!TryValidate(out message))
                return false;

            return DecorationEditSnapshot.TryCreatePortable(
                tether,
                Positioning.ToVector3(),
                Scaling.ToVector3(),
                Orientation.ToVector3(),
                Guid.Parse(MeshGuid),
                Color,
                HideOriginalMesh,
                Guid.Parse(MaterialReplacement),
                out snapshot,
                out message);
        }
    }
}
