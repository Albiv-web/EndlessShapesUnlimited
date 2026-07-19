using System;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationEditSnapshot
    {
        internal DecorationEditSnapshot(Decoration decoration)
        {
            if (decoration == null)
                throw new ArgumentNullException(nameof(decoration));

            TetherPoint = decoration.TetherPoint.Us;
            Positioning = decoration.Positioning.Us;
            Scaling = decoration.Scaling.Us;
            Orientation = decoration.Orientation.Us;
            MeshGuid = decoration.MeshGuid.Us;
            Color = decoration.Color.Us;
            HideOriginalMesh = decoration.HideOriginalMesh.Us;
            MaterialReplacement = decoration.MaterialReplacement.Us;
        }

        private DecorationEditSnapshot(DecorationEditSnapshot source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            TetherPoint = source.TetherPoint;
            Positioning = source.Positioning;
            Scaling = source.Scaling;
            Orientation = source.Orientation;
            MeshGuid = source.MeshGuid;
            Color = source.Color;
            HideOriginalMesh = source.HideOriginalMesh;
            MaterialReplacement = source.MaterialReplacement;
        }

        private DecorationEditSnapshot(
            Vector3i tetherPoint,
            Vector3 positioning,
            Vector3 scaling,
            Vector3 orientation,
            Guid meshGuid,
            int color,
            bool hideOriginalMesh,
            Guid materialReplacement)
        {
            TetherPoint = tetherPoint;
            Positioning = positioning;
            Scaling = scaling;
            Orientation = orientation;
            MeshGuid = meshGuid;
            Color = color;
            HideOriginalMesh = hideOriginalMesh;
            MaterialReplacement = materialReplacement;
        }

        internal Vector3i TetherPoint { get; }

        internal Vector3 Positioning { get; }

        internal Vector3 Scaling { get; }

        internal Vector3 Orientation { get; }

        internal Guid MeshGuid { get; }

        internal int Color { get; }

        internal bool HideOriginalMesh { get; }

        internal Guid MaterialReplacement { get; }

        internal bool HasFiniteTransform =>
            DecorationEditMath.IsFinite(Positioning) &&
            DecorationEditMath.IsFinite(Scaling) &&
            DecorationEditMath.IsFinite(Orientation);

        internal DecorationEditSnapshot Copy() =>
            new DecorationEditSnapshot(this);

        /// <summary>
        /// Creates a data-only snapshot for portable preset, array, and repair plans.
        /// Runtime identity (manager and UniqueId) is deliberately not part of a
        /// decoration snapshot and is assigned by FTD when the result is placed.
        /// </summary>
        internal static bool TryCreatePortable(
            Vector3i tetherPoint,
            Vector3 positioning,
            Vector3 scaling,
            Vector3 orientation,
            Guid meshGuid,
            int color,
            bool hideOriginalMesh,
            Guid materialReplacement,
            out DecorationEditSnapshot snapshot,
            out string message)
        {
            snapshot = null;
            if (!DecorationEditMath.IsFinite(positioning) ||
                !DecorationEditMath.IsFinite(scaling) ||
                !DecorationEditMath.IsFinite(orientation))
            {
                message = "Decoration snapshot contains NaN or infinity.";
                return false;
            }
            if (!DecorationEditMath.IsWithinPositionLimit(positioning))
            {
                message = "Decoration snapshot positioning exceeds FTD's +/-10 metre tether limit.";
                return false;
            }
            if (Math.Abs(scaling.x) < 0.00001f ||
                Math.Abs(scaling.y) < 0.00001f ||
                Math.Abs(scaling.z) < 0.00001f)
            {
                message = "Decoration snapshot scale contains a zero-sized axis.";
                return false;
            }
            if (color < 0 || color > 31)
            {
                message = "Decoration snapshot color must be between 0 and 31.";
                return false;
            }

            snapshot = new DecorationEditSnapshot(
                tetherPoint,
                positioning,
                scaling,
                orientation,
                meshGuid,
                color,
                hideOriginalMesh,
                materialReplacement);
            message = "Decoration snapshot is valid.";
            return true;
        }

        internal void Restore(Decoration decoration)
        {
            TryRestore(decoration);
        }

        internal bool TryRestore(Decoration decoration)
        {
            if (decoration == null || decoration.IsDeleted)
                return false;

            Vector3i currentTether = decoration.TetherPoint.Us;
            if (!SameTether(currentTether, TetherPoint))
            {
                if (decoration.OurManager == null)
                    return false;

                var shift = new Vector3i(
                    TetherPoint.x - currentTether.x,
                    TetherPoint.y - currentTether.y,
                    TetherPoint.z - currentTether.z);
                if (!decoration.OurManager.ShiftDecoration(decoration, shift))
                    return false;
            }

            decoration.Positioning.Us = Positioning;
            DecorationScaleBounds.AllowExtendedScale(decoration);
            decoration.Scaling.Us = Scaling;
            decoration.Orientation.Us = Orientation;
            decoration.MeshGuid.Us = MeshGuid;
            decoration.Color.Us = Color;
            decoration.HideOriginalMesh.Us = HideOriginalMesh;
            decoration.MaterialReplacement.Us = MaterialReplacement;
            decoration.Changed();
            return true;
        }

        internal bool Matches(Decoration decoration)
        {
            if (decoration == null || decoration.IsDeleted)
                return false;

            return SameTether(TetherPoint, decoration.TetherPoint.Us) &&
                   SameVector(Positioning, decoration.Positioning.Us) &&
                   SameVector(Scaling, decoration.Scaling.Us) &&
                   SameVector(Orientation, decoration.Orientation.Us) &&
                   MeshGuid == decoration.MeshGuid.Us &&
                   Color == decoration.Color.Us &&
                   HideOriginalMesh == decoration.HideOriginalMesh.Us &&
                   MaterialReplacement == decoration.MaterialReplacement.Us;
        }

        private static bool SameTether(Vector3i left, Vector3i right) =>
            left.x == right.x && left.y == right.y && left.z == right.z;

        private static bool SameVector(Vector3 left, Vector3 right) =>
            left.x == right.x &&
            left.y == right.y &&
            left.z == right.z;
    }
}
