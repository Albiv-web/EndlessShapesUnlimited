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
        private const int BoundaryRefinementIterations = 10;
        internal const float MeshPlacementCraftBubbleRadius = 100f;

        private readonly cBuild _build;
        private readonly List<AllConstruct> _constructs = new List<AllConstruct>();

        private enum AnchorResolutionMode
        {
            Strict,
            Nearest
        }

        internal readonly struct ProbeOptions
        {
            internal ProbeOptions(bool limitToCraftBubble, float craftBubbleRadius)
            {
                LimitToCraftBubble = limitToCraftBubble;
                CraftBubbleRadius = craftBubbleRadius;
            }

            internal bool LimitToCraftBubble { get; }

            internal float CraftBubbleRadius { get; }

            internal static ProbeOptions Default => new ProbeOptions(false, 0f);

            internal static ProbeOptions MeshPlacement =>
                new ProbeOptions(true, MeshPlacementCraftBubbleRadius);
        }

        private readonly struct RayDistanceLimits
        {
            internal RayDistanceLimits(float startDistance, float endDistance)
            {
                StartDistance = startDistance;
                EndDistance = endDistance;
            }

            internal float StartDistance { get; }

            internal float EndDistance { get; }

            internal static RayDistanceLimits Full => new RayDistanceLimits(0f, MaxDistance);
        }

        internal DecorationPointerProbe(cBuild build)
        {
            _build = build ?? throw new ArgumentNullException(nameof(build));
        }

        internal bool TryProbe(out DecorationPointerHit hit)
        {
            return TryProbe(ProbeOptions.Default, out hit);
        }

        internal bool TryProbe(ProbeOptions options, out DecorationPointerHit hit)
        {
            hit = null;
            Camera camera = Camera.main ?? Camera.current;
            if (camera == null)
                return false;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            RefreshConstructs();
            if (_constructs.Count == 0)
                return false;

            RayDistanceLimits limits = RayDistanceLimits.Full;
            if (options.LimitToCraftBubble &&
                !TryGetCraftBubbleRayLimits(ray, options.CraftBubbleRadius, out limits))
            {
                return false;
            }

            if (TryPhysicsHit(ray, limits, out hit))
                return true;

            return TrySampleRay(ray, limits, out hit);
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

        private bool TryPhysicsHit(Ray ray, RayDistanceLimits limits, out DecorationPointerHit hit)
        {
            hit = null;
            RaycastHit[] physicsHits = Physics.RaycastAll(ray, limits.EndDistance);
            if (physicsHits == null || physicsHits.Length == 0)
                return false;

            Array.Sort(physicsHits, (left, right) => left.distance.CompareTo(right.distance));
            Vector3 direction = ray.direction.normalized;
            for (int hitIndex = 0; hitIndex < physicsHits.Length; hitIndex++)
            {
                RaycastHit physicsHit = physicsHits[hitIndex];
                if (physicsHit.distance < limits.StartDistance || physicsHit.distance > limits.EndDistance)
                    continue;

                Vector3 normal = physicsHit.normal.sqrMagnitude > 0.0001f
                    ? physicsHit.normal.normalized
                    : -direction;
                Vector3 reportedWorld = physicsHit.point;
                Vector3[] candidates =
                {
                    reportedWorld,
                    reportedWorld - direction * SurfaceInset,
                    reportedWorld - normal * SurfaceInset
                };

                for (int index = 0; index < candidates.Length; index++)
                {
                    if (TryFindBlockAtWorld(
                            candidates[index],
                            reportedWorld,
                            normal,
                            AnchorResolutionMode.Nearest,
                            out hit))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TrySampleRay(Ray ray, RayDistanceLimits limits, out DecorationPointerHit hit)
        {
            hit = null;
            Vector3 direction = ray.direction.normalized;
            float startDistance = Mathf.Max(Step, limits.StartDistance);
            if (limits.EndDistance < startDistance)
                return false;

            float previousDistance = Mathf.Max(0f, startDistance - Step);
            Vector3 previousWorld = ray.origin + direction * previousDistance;
            bool previousResolved = TryFindBlockAtWorld(
                previousWorld,
                previousWorld,
                -direction,
                AnchorResolutionMode.Strict,
                out _);
            int steps = Mathf.CeilToInt((limits.EndDistance - startDistance) / Step);
            for (int index = 0; index <= steps; index++)
            {
                float distance = Mathf.Min(limits.EndDistance, startDistance + index * Step);
                Vector3 world = ray.origin + direction * distance;
                if (TryFindBlockAtWorld(
                        world,
                        world,
                        -direction,
                        AnchorResolutionMode.Strict,
                        out hit))
                {
                    Vector3 reportedWorld = previousResolved
                        ? world
                        : RefineBoundary(
                            previousWorld,
                            world,
                            sample => TryFindBlockAtWorld(
                                sample,
                                sample,
                                -direction,
                                AnchorResolutionMode.Strict,
                                out _));
                    if (TryFindBlockAtWorld(
                            world,
                            reportedWorld,
                            -direction,
                            AnchorResolutionMode.Strict,
                            out hit))
                    {
                        return true;
                    }

                    return TryFindBlockAtWorld(
                        world,
                        world,
                        -direction,
                        AnchorResolutionMode.Strict,
                        out hit);
                }

                previousWorld = world;
                previousResolved = false;
            }

            return false;
        }

        private bool TryGetCraftBubbleRayLimits(
            Ray ray,
            float bubbleRadius,
            out RayDistanceLimits limits)
        {
            limits = default;
            if (bubbleRadius <= 0f)
                return false;

            float start = MaxDistance;
            float end = 0f;
            bool found = false;
            Vector3 rayDirection = ray.direction.sqrMagnitude > 0.0001f
                ? ray.direction.normalized
                : Vector3.forward;
            Vector3 rayTarget = ray.origin + rayDirection;
            for (int index = 0; index < _constructs.Count; index++)
            {
                AllConstruct construct = _constructs[index];
                if (construct == null)
                    continue;

                if (!TryGetConstructLocalBounds(construct, out Vector3 localMin, out Vector3 localMax) ||
                    !TryWorldToLocal(construct, ray.origin, out Vector3 localOrigin) ||
                    !TryWorldToLocal(construct, rayTarget, out Vector3 localTarget))
                {
                    continue;
                }

                Vector3 localDirection = localTarget - localOrigin;
                if (!TryGetExpandedLocalBoundsRayInterval(
                        localOrigin,
                        localDirection,
                        localMin,
                        localMax,
                        bubbleRadius,
                        out float enter,
                        out float exit))
                {
                    continue;
                }

                float clampedEnter = Mathf.Max(0f, enter);
                float clampedExit = Mathf.Min(MaxDistance, exit);
                if (clampedExit < clampedEnter)
                    continue;

                start = Mathf.Min(start, clampedEnter);
                end = Mathf.Max(end, clampedExit);
                found = true;
            }

            if (!found)
                return false;

            limits = new RayDistanceLimits(start, end);
            return true;
        }

        internal static bool TryGetExpandedLocalBoundsRayIntervalForTests(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 min,
            Vector3 max,
            float expansion,
            out float start,
            out float end) =>
            TryGetExpandedLocalBoundsRayInterval(rayOrigin, rayDirection, min, max, expansion, out start, out end);

        private static bool TryGetExpandedLocalBoundsRayInterval(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 min,
            Vector3 max,
            float expansion,
            out float start,
            out float end)
        {
            start = 0f;
            end = 0f;
            if (rayDirection.sqrMagnitude < 0.0001f || expansion < 0f)
                return false;

            Vector3 direction = rayDirection.normalized;
            Vector3 expandedMin = min - Vector3.one * expansion;
            Vector3 expandedMax = max + Vector3.one * expansion;
            float enter = 0f;
            float exit = float.MaxValue;
            if (!IntersectSlab(rayOrigin.x, direction.x, expandedMin.x, expandedMax.x, ref enter, ref exit) ||
                !IntersectSlab(rayOrigin.y, direction.y, expandedMin.y, expandedMax.y, ref enter, ref exit) ||
                !IntersectSlab(rayOrigin.z, direction.z, expandedMin.z, expandedMax.z, ref enter, ref exit) ||
                exit < 0f)
            {
                return false;
            }

            start = Mathf.Max(0f, enter);
            end = exit;
            return end >= start;
        }

        private static bool IntersectSlab(
            float origin,
            float direction,
            float min,
            float max,
            ref float enter,
            ref float exit)
        {
            if (Mathf.Abs(direction) < 0.0001f)
                return origin >= min && origin <= max;

            float first = (min - origin) / direction;
            float second = (max - origin) / direction;
            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }

            enter = Mathf.Max(enter, first);
            exit = Mathf.Min(exit, second);
            return exit >= enter;
        }

        private static bool TryGetConstructLocalBounds(
            AllConstruct construct,
            out Vector3 min,
            out Vector3 max)
        {
            min = Vector3.zero;
            max = Vector3.zero;
            if (construct == null)
                return false;

            try
            {
                Vector3i constructMin = construct.GetMin();
                Vector3i constructMax = construct.GetMax();
                min = new Vector3(constructMin.x, constructMin.y, constructMin.z) - Vector3.one * 0.5f;
                max = new Vector3(constructMax.x, constructMax.y, constructMax.z) + Vector3.one * 0.5f;
                return true;
            }
            catch
            {
                try
                {
                    min = construct.SafeGlobalToLocal(construct.SafePosition) - Vector3.one * 0.5f;
                    max = min + Vector3.one;
                    return true;
                }
                catch
                {
                    if (construct.myTransform == null)
                        return false;

                    min = construct.myTransform.InverseTransformPoint(construct.SafePosition) - Vector3.one * 0.5f;
                    max = min + Vector3.one;
                    return true;
                }
            }
        }

        private static bool TryWorldToLocal(AllConstruct construct, Vector3 world, out Vector3 local)
        {
            local = Vector3.zero;
            if (construct == null)
                return false;

            try
            {
                local = construct.SafeGlobalToLocal(world);
                return true;
            }
            catch
            {
                try
                {
                    if (construct.myTransform == null)
                        return false;

                    local = construct.myTransform.InverseTransformPoint(world);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static Vector3 RefineBoundaryForTests(
            Vector3 outsideWorld,
            Vector3 insideWorld,
            Predicate<Vector3> resolves) =>
            RefineBoundary(outsideWorld, insideWorld, resolves);

        private static Vector3 RefineBoundary(
            Vector3 outsideWorld,
            Vector3 insideWorld,
            Predicate<Vector3> resolves)
        {
            if (resolves == null)
                return insideWorld;

            Vector3 low = outsideWorld;
            Vector3 high = insideWorld;
            for (int iteration = 0; iteration < BoundaryRefinementIterations; iteration++)
            {
                Vector3 mid = (low + high) * 0.5f;
                if (resolves(mid))
                    high = mid;
                else
                    low = mid;
            }

            return high;
        }

        private bool TryFindBlockAtWorld(
            Vector3 sampleWorld,
            Vector3 reportedWorld,
            Vector3 worldNormal,
            AnchorResolutionMode mode,
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
                    local = construct.SafeGlobalToLocal(sampleWorld);
                }
                catch
                {
                    if (construct.myTransform == null)
                        continue;
                    local = construct.myTransform.InverseTransformPoint(sampleWorld);
                }

                if (!TryResolveAnchor(construct, local, mode, out Vector3i anchor))
                    continue;

                Vector3 reportedLocal;
                try
                {
                    reportedLocal = construct.SafeGlobalToLocal(reportedWorld);
                }
                catch
                {
                    if (construct.myTransform == null)
                        continue;
                    reportedLocal = construct.myTransform.InverseTransformPoint(reportedWorld);
                }

                Vector3 center = new Vector3(anchor.x, anchor.y, anchor.z);
                float distance = (local - center).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = new DecorationPointerHit(construct, anchor, reportedLocal, reportedWorld, worldNormal);
            }

            hit = best;
            return hit != null;
        }

        private static bool TryResolveAnchor(
            AllConstruct construct,
            Vector3 local,
            AnchorResolutionMode mode,
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

            if (mode == AnchorResolutionMode.Strict)
                return false;

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
