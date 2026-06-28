using System;
using System.Collections.Generic;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationPointerHit
    {
        internal DecorationPointerHit(
            AllConstruct construct,
            Vector3i anchor,
            Vector3 localHit,
            Vector3 worldHit,
            Vector3 worldNormal)
        {
            Construct = construct;
            Anchor = anchor;
            LocalHit = localHit;
            WorldHit = worldHit;
            WorldNormal = worldNormal;
        }

        internal AllConstruct Construct { get; }

        internal Vector3i Anchor { get; }

        internal Vector3 LocalHit { get; }

        internal Vector3 WorldHit { get; }

        internal Vector3 WorldNormal { get; }

        internal Vector3 LocalPositioning =>
            DecorationEditMath.Snap(LocalHit - new Vector3(Anchor.x, Anchor.y, Anchor.z));
    }

    internal sealed class DecorationPointerProbe
    {
        private const float MaxDistance = 650f;
        private const float Step = 0.2f;
        private const float SurfaceInset = 0.08f;

        private readonly cBuild _build;
        private readonly List<AllConstruct> _constructs = new List<AllConstruct>();

        internal DecorationPointerProbe(cBuild build)
        {
            _build = build ?? throw new ArgumentNullException(nameof(build));
        }

        internal bool TryProbe(out DecorationPointerHit hit)
        {
            hit = null;
            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return false;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            RefreshConstructs();
            if (_constructs.Count == 0)
                return false;

            if (TryPhysicsHit(ray, out hit))
                return true;

            return TrySampleRay(ray, out hit);
        }

        private void RefreshConstructs()
        {
            _constructs.Clear();
            MainConstruct main = _build.GetCC();
            if (main == null)
                return;

            try
            {
                main.AllBasicsRestricted.GetAllConstructsBelowUsAndIncludingUs(_constructs);
            }
            catch
            {
                AllConstruct current = _build.GetC();
                if (current != null)
                    _constructs.Add(current);
            }
        }

        private bool TryPhysicsHit(Ray ray, out DecorationPointerHit hit)
        {
            hit = null;
            if (!Physics.Raycast(ray, out RaycastHit physicsHit, MaxDistance))
                return false;

            Vector3 normal = physicsHit.normal.sqrMagnitude > 0.0001f
                ? physicsHit.normal.normalized
                : -ray.direction;
            Vector3[] candidates =
            {
                physicsHit.point,
                physicsHit.point - ray.direction.normalized * SurfaceInset,
                physicsHit.point - normal * SurfaceInset
            };

            for (int index = 0; index < candidates.Length; index++)
            {
                if (TryFindBlockAtWorld(candidates[index], normal, out hit))
                    return true;
            }

            return false;
        }

        private bool TrySampleRay(Ray ray, out DecorationPointerHit hit)
        {
            hit = null;
            Vector3 direction = ray.direction.normalized;
            int steps = Mathf.CeilToInt(MaxDistance / Step);
            for (int index = 2; index < steps; index++)
            {
                Vector3 world = ray.origin + direction * (index * Step);
                if (TryFindBlockAtWorld(world, -direction, out hit))
                    return true;
            }

            return false;
        }

        private bool TryFindBlockAtWorld(
            Vector3 world,
            Vector3 worldNormal,
            out DecorationPointerHit hit)
        {
            hit = null;
            float bestDistance = float.MaxValue;
            DecorationPointerHit best = null;
            for (int index = 0; index < _constructs.Count; index++)
            {
                AllConstruct construct = _constructs[index];
                if (construct == null)
                    continue;

                Vector3 local;
                try
                {
                    local = construct.SafeGlobalToLocal(world);
                }
                catch
                {
                    if (construct.myTransform == null)
                        continue;
                    local = construct.myTransform.InverseTransformPoint(world);
                }

                if (!TryResolveAnchor(construct, local, out Vector3i anchor))
                    continue;

                Vector3 center = new Vector3(anchor.x, anchor.y, anchor.z);
                float distance = (local - center).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = new DecorationPointerHit(construct, anchor, local, world, worldNormal);
            }

            hit = best;
            return hit != null;
        }

        private static bool TryResolveAnchor(
            AllConstruct construct,
            Vector3 local,
            out Vector3i anchor)
        {
            anchor = default;
            Vector3i rounded = new Vector3i(
                Mathf.RoundToInt(local.x),
                Mathf.RoundToInt(local.y),
                Mathf.RoundToInt(local.z));

            if (HasBlock(construct, rounded))
            {
                anchor = rounded;
                return true;
            }

            float bestDistance = float.MaxValue;
            Vector3i best = default;
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        var candidate = new Vector3i(rounded.x + x, rounded.y + y, rounded.z + z);
                        if (!HasBlock(construct, candidate))
                            continue;

                        Vector3 center = new Vector3(candidate.x, candidate.y, candidate.z);
                        float distance = (local - center).sqrMagnitude;
                        if (distance >= bestDistance)
                            continue;

                        bestDistance = distance;
                        best = candidate;
                    }

            if (bestDistance == float.MaxValue)
                return false;

            anchor = best;
            return true;
        }

        private static bool HasBlock(AllConstruct construct, Vector3i position)
        {
            try
            {
                return construct?.AllBasics?.GetBlockViaLocalPosition(position) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
