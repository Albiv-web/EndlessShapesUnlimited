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

        internal Vector3i TetherPoint { get; }

        internal Vector3 Positioning { get; }

        internal Vector3 Scaling { get; }

        internal Vector3 Orientation { get; }

        internal Guid MeshGuid { get; }

        internal int Color { get; }

        internal bool HideOriginalMesh { get; }

        internal Guid MaterialReplacement { get; }

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
            Mathf.Abs(left.x - right.x) < 0.0001f &&
            Mathf.Abs(left.y - right.y) < 0.0001f &&
            Mathf.Abs(left.z - right.z) < 0.0001f;
    }
}
