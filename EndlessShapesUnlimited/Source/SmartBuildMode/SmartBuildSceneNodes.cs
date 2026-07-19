using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using DecoLimitLifter.DecorationEditMode;

namespace DecoLimitLifter.SmartBuildMode
{
    internal enum SmartBuildSceneNodeKind
    {
        Primitive = 0,
        Pattern = 1,
        Region = 2
    }

    internal enum SmartBuildEditablePatternKind
    {
        Linear = 0,
        Grid = 1,
        Radial = 2,
        Polyline = 3
    }

    internal enum SmartBuildRadialOrientationMode
    {
        Keep = 0,
        RotateCardinal = 1
    }

    internal enum SmartBuildPolylineOrientationMode
    {
        Keep = 0,
        CardinalTangent = 1
    }

    internal enum SmartBuildRegionKind
    {
        Rectangle = 0,
        Wall = 1,
        Plane = 2,
        Brush = 3,
        Flood = 4
    }

    /// <summary>
    /// Shared bounded-expansion guard. Scene nodes reserve output before cloning
    /// anything, so a rejected pattern cannot leave a partial expansion behind.
    /// </summary>
    internal sealed class SmartBuildExpansionBudget
    {
        internal SmartBuildExpansionBudget(
            int maximumPieces = SmartBuildLimits.HardPlacementCount)
        {
            if (maximumPieces < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumPieces));
            MaximumPieces = maximumPieces;
        }

        internal int MaximumPieces { get; }

        internal int ReservedPieces { get; private set; }

        internal int RemainingPieces => MaximumPieces - ReservedPieces;

        internal bool TryReserve(int count, out string reason)
        {
            if (count < 0 || (long)ReservedPieces + count > MaximumPieces)
            {
                reason =
                    "The editable pattern expands to more than " +
                    MaximumPieces.ToString("N0", CultureInfo.InvariantCulture) +
                    " pieces.";
                return false;
            }

            ReservedPieces += count;
            reason = null;
            return true;
        }
    }

    internal sealed class SmartBuildNodeExpansion
    {
        internal SmartBuildNodeExpansion(
            IEnumerable<SmartBuildPiece> pieces,
            IEnumerable<string> warnings = null)
        {
            Pieces = (pieces ?? Array.Empty<SmartBuildPiece>())
                .Where(piece => piece != null)
                .ToArray();
            Warnings = (warnings ?? Array.Empty<string>())
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        internal IReadOnlyList<SmartBuildPiece> Pieces { get; }

        internal IReadOnlyList<string> Warnings { get; }
    }

    internal interface ISmartBuildSceneNode
    {
        int Id { get; }

        long Revision { get; }

        SmartBuildSceneNodeKind Kind { get; }

        SmartBuildPiece HostPiece { get; }

        IReadOnlyList<SmartBuildPiece> SourcePieces { get; }

        bool TryExpand(
            SmartBuildExpansionBudget budget,
            out SmartBuildNodeExpansion expansion,
            out string reason);

        bool TryBake(
            int maximumPieces,
            out IReadOnlyList<SmartBuildPiece> pieces,
            out string reason);

        IReadOnlyList<SmartBuildPiece> Dissolve();
    }

    internal sealed class SmartBuildPrimitiveNode : ISmartBuildSceneNode
    {
        internal SmartBuildPrimitiveNode(SmartBuildPiece piece)
        {
            HostPiece = piece ?? throw new ArgumentNullException(nameof(piece));
        }

        public int Id => HostPiece.Id;

        public long Revision => 0L;

        public SmartBuildSceneNodeKind Kind => SmartBuildSceneNodeKind.Primitive;

        public SmartBuildPiece HostPiece { get; }

        public IReadOnlyList<SmartBuildPiece> SourcePieces => new[] { HostPiece };

        public bool TryExpand(
            SmartBuildExpansionBudget budget,
            out SmartBuildNodeExpansion expansion,
            out string reason)
        {
            expansion = null;
            if (budget == null)
            {
                reason = "No Smart Builder expansion budget is available.";
                return false;
            }
            if (!budget.TryReserve(1, out reason))
                return false;

            expansion = new SmartBuildNodeExpansion(new[] { HostPiece });
            return true;
        }

        public bool TryBake(
            int maximumPieces,
            out IReadOnlyList<SmartBuildPiece> pieces,
            out string reason)
        {
            pieces = Array.Empty<SmartBuildPiece>();
            if (maximumPieces < 1)
            {
                reason = "Baking this primitive would exceed the scene limit.";
                return false;
            }

            pieces = new[] { HostPiece.Clone() };
            reason = null;
            return true;
        }

        public IReadOnlyList<SmartBuildPiece> Dissolve() =>
            new[] { HostPiece.Clone() };
    }

    /// <summary>
    /// Portable editable parameters. Counts are additional copies on either side;
    /// the identity/source group is always instance zero.
    /// </summary>
    internal sealed class SmartBuildPatternDefinition
    {
        internal SmartBuildEditablePatternKind Kind { get; set; }

        internal Vector3i PrimaryStep { get; set; } = new Vector3i(1, 0, 0);

        internal Vector3i SecondaryStep { get; set; } = new Vector3i(0, 0, 1);

        internal int PrimaryBefore { get; set; }

        internal int PrimaryAfter { get; set; } = 1;

        internal int SecondaryBefore { get; set; }

        internal int SecondaryAfter { get; set; }

        internal Vector3i RadialPivot { get; set; }

        internal DecorationEditAxis RadialAxis { get; set; } = DecorationEditAxis.Y;

        internal float RadialAngleStepDegrees { get; set; } = 90f;

        internal SmartBuildRadialOrientationMode RadialOrientation { get; set; } =
            SmartBuildRadialOrientationMode.RotateCardinal;

        internal IReadOnlyList<Vector3i> PathPoints { get; set; } =
            Array.Empty<Vector3i>();

        internal SmartBuildPathArrayMode PathMode { get; set; } =
            SmartBuildPathArrayMode.SteppedCells;

        internal SmartBuildPolylineOrientationMode PathOrientation { get; set; } =
            SmartBuildPolylineOrientationMode.Keep;

        internal int PathSpacingCells { get; set; } = 1;

        internal SmartBuildPatternDefinition Clone() =>
            new SmartBuildPatternDefinition
            {
                Kind = Kind,
                PrimaryStep = PrimaryStep,
                SecondaryStep = SecondaryStep,
                PrimaryBefore = PrimaryBefore,
                PrimaryAfter = PrimaryAfter,
                SecondaryBefore = SecondaryBefore,
                SecondaryAfter = SecondaryAfter,
                RadialPivot = RadialPivot,
                RadialAxis = RadialAxis,
                RadialAngleStepDegrees = RadialAngleStepDegrees,
                RadialOrientation = RadialOrientation,
                PathPoints = (PathPoints ?? Array.Empty<Vector3i>()).ToArray(),
                PathMode = PathMode,
                PathOrientation = PathOrientation,
                PathSpacingCells = PathSpacingCells
            };

        internal bool TryValidate(int sourceCount, out int instanceCount, out string reason)
        {
            instanceCount = 0;
            if (sourceCount < 1)
            {
                reason = "An editable pattern needs at least one primitive source.";
                return false;
            }
            if (!Enum.IsDefined(typeof(SmartBuildEditablePatternKind), Kind) ||
                PrimaryBefore < 0 || PrimaryAfter < 0 ||
                SecondaryBefore < 0 || SecondaryAfter < 0)
            {
                reason = "Editable pattern copy counts are invalid.";
                return false;
            }

            long instances;
            switch (Kind)
            {
                case SmartBuildEditablePatternKind.Linear:
                    if (IsZero(PrimaryStep) && PrimaryBefore + PrimaryAfter > 0)
                    {
                        reason = "Linear pattern spacing must move at least one cell.";
                        return false;
                    }
                    instances = 1L + PrimaryBefore + PrimaryAfter;
                    break;

                case SmartBuildEditablePatternKind.Grid:
                    if ((PrimaryBefore + PrimaryAfter > 0 && IsZero(PrimaryStep)) ||
                        (SecondaryBefore + SecondaryAfter > 0 && IsZero(SecondaryStep)))
                    {
                        reason = "Each active grid direction must move at least one cell.";
                        return false;
                    }
                    instances =
                        (1L + PrimaryBefore + PrimaryAfter) *
                        (1L + SecondaryBefore + SecondaryAfter);
                    break;

                case SmartBuildEditablePatternKind.Radial:
                    if (!IsCardinalAxis(RadialAxis) ||
                        float.IsNaN(RadialAngleStepDegrees) ||
                        float.IsInfinity(RadialAngleStepDegrees) ||
                        Math.Abs(RadialAngleStepDegrees) < 0.0001f)
                    {
                        reason = "Radial patterns need a finite non-zero angle and X, Y, or Z axis.";
                        return false;
                    }
                    if (RadialOrientation == SmartBuildRadialOrientationMode.RotateCardinal &&
                        !IsQuarterTurn(RadialAngleStepDegrees))
                    {
                        reason = "Rotate-orientation radial patterns require a 90-degree angle multiple.";
                        return false;
                    }
                    instances = 1L + PrimaryBefore + PrimaryAfter;
                    break;

                case SmartBuildEditablePatternKind.Polyline:
                    Vector3i[] points = (PathPoints ?? Array.Empty<Vector3i>()).ToArray();
                    if (points.Length < 2 ||
                        points.Length > SmartBuildAdvancedToolPlanner.MaximumPathControlPoints ||
                        PathSpacingCells < 1 ||
                        !Enum.IsDefined(
                            typeof(SmartBuildPolylineOrientationMode),
                            PathOrientation))
                    {
                        reason = "Polyline patterns need 2 through " +
                                 SmartBuildAdvancedToolPlanner.MaximumPathControlPoints +
                                 " points and positive cell spacing.";
                        return false;
                    }
                    if (!SmartBuildAdvancedToolPlanner.TryPlanPath(
                            points,
                            PathMode,
                            out IReadOnlyList<SmartBuildGroupTransform> pathTransforms,
                            out reason))
                    {
                        return false;
                    }
                    instances = 1L + pathTransforms
                        .Where((transform, index) => (index + 1) % PathSpacingCells == 0)
                        .LongCount();
                    break;

                default:
                    reason = "The editable pattern kind is not supported.";
                    return false;
            }

            long expanded = instances * sourceCount;
            if (instances < 1L || instances > int.MaxValue ||
                expanded > SmartBuildLimits.HardPlacementCount)
            {
                reason = "The editable pattern exceeds the " +
                         SmartBuildLimits.HardPlacementCount.ToString("N0", CultureInfo.InvariantCulture) +
                         "-piece expansion limit.";
                return false;
            }

            instanceCount = (int)instances;
            reason = null;
            return true;
        }

        private static bool IsZero(Vector3i value) =>
            value.x == 0 && value.y == 0 && value.z == 0;

        private static bool IsCardinalAxis(DecorationEditAxis axis) =>
            axis == DecorationEditAxis.X ||
            axis == DecorationEditAxis.Y ||
            axis == DecorationEditAxis.Z;

        private static bool IsQuarterTurn(float degrees)
        {
            float turns = degrees / 90f;
            return Math.Abs(turns - (float)Math.Round(turns)) <= 0.0001f;
        }
    }

    internal sealed class SmartBuildPatternNode : ISmartBuildSceneNode
    {
        private readonly List<SmartBuildPiece> _sources;
        private SmartBuildPiece _lastHostSnapshot;
        private SmartBuildPatternDefinition _definition;

        internal SmartBuildPatternNode(
            SmartBuildPiece hostPiece,
            IEnumerable<SmartBuildPiece> sources,
            SmartBuildPatternDefinition definition)
        {
            HostPiece = hostPiece ?? throw new ArgumentNullException(nameof(hostPiece));
            _sources = (sources ?? Array.Empty<SmartBuildPiece>())
                .Where(piece => piece != null)
                .Select(piece => piece.Clone())
                .ToList();
            if (_sources.Count == 0)
                throw new ArgumentException("A pattern node needs source pieces.", nameof(sources));

            int primaryIndex = _sources.FindIndex(piece => piece.Id == hostPiece.Id);
            if (primaryIndex < 0)
                _sources.Insert(0, hostPiece.Clone());
            _definition = (definition ?? throw new ArgumentNullException(nameof(definition))).Clone();
            if (!_definition.TryValidate(_sources.Count, out _, out string reason))
                throw new ArgumentException(reason, nameof(definition));
            _lastHostSnapshot = hostPiece.Clone();
        }

        public int Id => HostPiece.Id;

        public long Revision { get; private set; } = 1L;

        public SmartBuildSceneNodeKind Kind => SmartBuildSceneNodeKind.Pattern;

        public SmartBuildPiece HostPiece { get; }

        public IReadOnlyList<SmartBuildPiece> SourcePieces =>
            _sources.Select(piece => piece.Clone()).ToArray();

        internal SmartBuildPatternDefinition Definition => _definition.Clone();

        internal bool TrySetSourcePiece(
            int sourceIndex,
            SmartBuildPiece replacement,
            out string reason)
        {
            if (sourceIndex < 0 || sourceIndex >= _sources.Count)
            {
                reason = "Select a valid embedded pattern source piece.";
                return false;
            }
            if (replacement == null || replacement.Id != _sources[sourceIndex].Id)
            {
                reason = "An embedded source edit must preserve the source piece identity.";
                return false;
            }

            SmartBuildPiece[] previousSources = _sources
                .Select(source => source.Clone())
                .ToArray();
            SmartBuildPiece previousHost = HostPiece.Clone();
            SmartBuildPiece previousHostSnapshot = _lastHostSnapshot.Clone();
            SmartBuildPatternDefinition previousDefinition = _definition.Clone();
            long previousRevision = Revision;
            _sources[sourceIndex].CopyFrom(replacement);
            if (_sources[sourceIndex].Id == HostPiece.Id)
            {
                HostPiece.CopyFrom(replacement);
                _lastHostSnapshot = HostPiece.Clone();
            }
            Revision++;

            if (TryExpand(
                    new SmartBuildExpansionBudget(),
                    out _,
                    out reason))
            {
                reason = null;
                return true;
            }

            for (int index = 0; index < _sources.Count; index++)
                _sources[index].CopyFrom(previousSources[index]);
            HostPiece.CopyFrom(previousHost);
            _lastHostSnapshot = previousHostSnapshot;
            _definition = previousDefinition;
            Revision = previousRevision;
            return false;
        }

        internal bool TrySetDefinition(SmartBuildPatternDefinition definition, out string reason)
        {
            if (definition == null)
            {
                reason = "No editable pattern definition was supplied.";
                return false;
            }
            if (!definition.TryValidate(_sources.Count, out _, out reason))
                return false;
            _definition = definition.Clone();
            Revision++;
            reason = null;
            return true;
        }

        public bool TryExpand(
            SmartBuildExpansionBudget budget,
            out SmartBuildNodeExpansion expansion,
            out string reason)
        {
            expansion = null;
            if (budget == null)
            {
                reason = "No Smart Builder expansion budget is available.";
                return false;
            }

            SynchronizeHostTranslationAndSource();
            if (!_definition.TryValidate(_sources.Count, out int instanceCount, out reason))
                return false;
            int required;
            try
            {
                required = checked(instanceCount * _sources.Count);
            }
            catch (OverflowException)
            {
                reason = "The editable pattern piece count overflowed.";
                return false;
            }
            if (!budget.TryReserve(required, out reason))
                return false;

            var pieces = new List<SmartBuildPiece>(required);
            var warnings = new List<string>();
            if (!TryBuildInstances(pieces, warnings, out reason))
                return false;

            expansion = new SmartBuildNodeExpansion(pieces, warnings);
            return true;
        }

        public bool TryBake(
            int maximumPieces,
            out IReadOnlyList<SmartBuildPiece> pieces,
            out string reason)
        {
            pieces = Array.Empty<SmartBuildPiece>();
            var budget = new SmartBuildExpansionBudget(maximumPieces);
            if (!TryExpand(budget, out SmartBuildNodeExpansion expansion, out reason))
                return false;

            pieces = expansion.Pieces
                .Select(piece => piece.Duplicate(new Vector3i(0, 0, 0)))
                .ToArray();
            reason = null;
            return true;
        }

        public IReadOnlyList<SmartBuildPiece> Dissolve()
        {
            SynchronizeHostTranslationAndSource();
            return _sources
                .Select(piece => piece.Duplicate(new Vector3i(0, 0, 0)))
                .ToArray();
        }

        private bool TryBuildInstances(
            List<SmartBuildPiece> output,
            List<string> warnings,
            out string reason)
        {
            switch (_definition.Kind)
            {
                case SmartBuildEditablePatternKind.Linear:
                    for (int index = -_definition.PrimaryBefore;
                         index <= _definition.PrimaryAfter;
                         index++)
                    {
                        if (!TryScaled(_definition.PrimaryStep, index, out Vector3i offset))
                        {
                            reason = "A linear pattern offset exceeds the safe coordinate range.";
                            return false;
                        }
                        AddTranslatedSources(output, offset);
                    }
                    break;

                case SmartBuildEditablePatternKind.Grid:
                    for (int secondary = -_definition.SecondaryBefore;
                         secondary <= _definition.SecondaryAfter;
                         secondary++)
                    {
                        for (int primary = -_definition.PrimaryBefore;
                             primary <= _definition.PrimaryAfter;
                             primary++)
                        {
                            if (!TryCombined(
                                    _definition.PrimaryStep,
                                    primary,
                                    _definition.SecondaryStep,
                                    secondary,
                                    out Vector3i offset))
                            {
                                reason = "A grid pattern offset exceeds the safe coordinate range.";
                                return false;
                            }
                            AddTranslatedSources(output, offset);
                        }
                    }
                    break;

                case SmartBuildEditablePatternKind.Radial:
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int index = -_definition.PrimaryBefore;
                         index <= _definition.PrimaryAfter;
                         index++)
                    {
                        float angle = index * _definition.RadialAngleStepDegrees;
                        SmartBuildPiece[] instance;
                        if (!TryBuildRadialInstance(angle, out instance, out reason))
                            return false;
                        string signature = string.Join(
                            "|",
                            instance
                                .Select(piece => CellKey(piece.Origin))
                                .OrderBy(value => value, StringComparer.Ordinal));
                        if (!seen.Add(signature))
                        {
                            warnings.Add("Radial rounding collapsed one or more duplicate instances; duplicates were skipped.");
                            continue;
                        }
                        output.AddRange(instance);
                    }
                    break;

                case SmartBuildEditablePatternKind.Polyline:
                    Vector3i[] points = (_definition.PathPoints ?? Array.Empty<Vector3i>()).ToArray();
                    if (!SmartBuildAdvancedToolPlanner.TryPlanPath(
                            points,
                            _definition.PathMode,
                            out IReadOnlyList<SmartBuildGroupTransform> transforms,
                            out reason))
                    {
                        return false;
                    }
                    AddTranslatedSources(output, new Vector3i(0, 0, 0));
                    var emittedTransforms = new List<SmartBuildGroupTransform>();
                    int emitted = 0;
                    foreach (SmartBuildGroupTransform transform in transforms)
                    {
                        emitted++;
                        if (emitted % _definition.PathSpacingCells != 0)
                            continue;
                        emittedTransforms.Add(transform);
                    }
                    for (int index = 0; index < emittedTransforms.Count; index++)
                    {
                        SmartBuildGroupTransform transform = emittedTransforms[index];
                        Vector3i tangent;
                        if (index + 1 < emittedTransforms.Count)
                        {
                            tangent = emittedTransforms[index + 1].Offset - transform.Offset;
                        }
                        else if (index > 0)
                        {
                            tangent = transform.Offset - emittedTransforms[index - 1].Offset;
                        }
                        else
                        {
                            tangent = transform.Offset;
                        }
                        if (!AddPolylineSources(output, transform.Offset, tangent, out reason))
                            return false;
                    }
                    break;

                default:
                    reason = "The editable pattern kind is not supported.";
                    return false;
            }

            reason = null;
            return true;
        }

        private void AddTranslatedSources(List<SmartBuildPiece> output, Vector3i offset)
        {
            foreach (SmartBuildPiece source in _sources)
                output.Add(source.Duplicate(offset));
        }

        private bool AddPolylineSources(
            List<SmartBuildPiece> output,
            Vector3i offset,
            Vector3i tangent,
            out string reason)
        {
            if (_definition.PathOrientation == SmartBuildPolylineOrientationMode.Keep)
            {
                AddTranslatedSources(output, offset);
                reason = null;
                return true;
            }

            Vector3i cardinal = DominantCardinal(tangent);
            if (cardinal.Equals(new Vector3i(0, 0, 0)))
            {
                reason = "A Cardinal Tangent path instance has no usable tangent.";
                return false;
            }
            foreach (SmartBuildPiece source in _sources)
            {
                SmartBuildPiece instance = source.Duplicate(offset);
                if (!instance.TryOrientForwardTo(cardinal))
                {
                    reason = "A path source could not orient to its cardinal tangent.";
                    return false;
                }
                output.Add(instance);
            }

            reason = null;
            return true;
        }

        private static Vector3i DominantCardinal(Vector3i value)
        {
            long x = Math.Abs((long)value.x);
            long y = Math.Abs((long)value.y);
            long z = Math.Abs((long)value.z);
            if (x >= y && x >= z && x > 0L)
                return new Vector3i(value.x >= 0 ? 1 : -1, 0, 0);
            if (y >= z && y > 0L)
                return new Vector3i(0, value.y >= 0 ? 1 : -1, 0);
            if (z > 0L)
                return new Vector3i(0, 0, value.z >= 0 ? 1 : -1);
            return new Vector3i(0, 0, 0);
        }

        private bool TryBuildRadialInstance(
            float angle,
            out SmartBuildPiece[] pieces,
            out string reason)
        {
            pieces = new SmartBuildPiece[_sources.Count];
            if (_definition.RadialOrientation == SmartBuildRadialOrientationMode.RotateCardinal)
            {
                int turns = (int)Math.Round(angle / 90f, MidpointRounding.AwayFromZero);
                for (int index = 0; index < _sources.Count; index++)
                {
                    SmartBuildPiece clone = _sources[index].Duplicate(new Vector3i(0, 0, 0));
                    clone.RotateAroundAxis(_definition.RadialAxis, turns, _definition.RadialPivot);
                    pieces[index] = clone;
                }
                reason = null;
                return true;
            }

            for (int index = 0; index < _sources.Count; index++)
            {
                SmartBuildPiece source = _sources[index];
                if (!TryRotatePosition(
                        source.Origin,
                        _definition.RadialPivot,
                        _definition.RadialAxis,
                        angle,
                        out Vector3i target))
                {
                    reason = "A rounded radial position exceeds the safe coordinate range.";
                    pieces = Array.Empty<SmartBuildPiece>();
                    return false;
                }
                pieces[index] = source.Duplicate(target - source.Origin);
            }

            reason = null;
            return true;
        }

        private void SynchronizeHostTranslationAndSource()
        {
            SmartBuildPiece primary = _sources.FirstOrDefault(piece => piece.Id == HostPiece.Id);
            if (primary == null)
                return;

            Vector3i delta = HostPiece.Origin - _lastHostSnapshot.Origin;
            if (delta.x != 0 || delta.y != 0 || delta.z != 0)
            {
                foreach (SmartBuildPiece source in _sources)
                    source.MoveBy(delta);
                if (_definition.Kind == SmartBuildEditablePatternKind.Radial)
                    _definition.RadialPivot += delta;
                if (_definition.Kind == SmartBuildEditablePatternKind.Polyline)
                {
                    _definition.PathPoints = (_definition.PathPoints ?? Array.Empty<Vector3i>())
                        .Select(point => point + delta)
                        .ToArray();
                }
                Revision++;
            }

            // Shape/material edits to the visible host are source edits. Group rotation
            // is deliberately handled by pattern parameters rather than inferred from
            // arbitrary host mutations.
            primary.CopyFrom(HostPiece);
            _lastHostSnapshot = HostPiece.Clone();
        }

        private static bool TryRotatePosition(
            Vector3i point,
            Vector3i pivot,
            DecorationEditAxis axis,
            float degrees,
            out Vector3i result)
        {
            double radians = degrees * Math.PI / 180.0;
            double cosine = Math.Cos(radians);
            double sine = Math.Sin(radians);
            double x = point.x - (double)pivot.x;
            double y = point.y - (double)pivot.y;
            double z = point.z - (double)pivot.z;
            double rotatedX = x;
            double rotatedY = y;
            double rotatedZ = z;
            switch (axis)
            {
                case DecorationEditAxis.X:
                    rotatedY = y * cosine - z * sine;
                    rotatedZ = y * sine + z * cosine;
                    break;
                case DecorationEditAxis.Y:
                    rotatedX = x * cosine + z * sine;
                    rotatedZ = -x * sine + z * cosine;
                    break;
                case DecorationEditAxis.Z:
                    rotatedX = x * cosine - y * sine;
                    rotatedY = x * sine + y * cosine;
                    break;
                default:
                    result = new Vector3i(0, 0, 0);
                    return false;
            }

            long targetX = RoundCell(pivot.x + rotatedX);
            long targetY = RoundCell(pivot.y + rotatedY);
            long targetZ = RoundCell(pivot.z + rotatedZ);
            int limit = SmartBuildAdvancedToolPlanner.MaximumCoordinateMagnitude;
            if (Math.Abs(targetX) > limit || Math.Abs(targetY) > limit || Math.Abs(targetZ) > limit)
            {
                result = new Vector3i(0, 0, 0);
                return false;
            }

            result = new Vector3i((int)targetX, (int)targetY, (int)targetZ);
            return true;
        }

        private static long RoundCell(double value) =>
            checked((long)Math.Round(value, MidpointRounding.AwayFromZero));

        private static bool TryScaled(Vector3i value, int scale, out Vector3i result)
        {
            long x = (long)value.x * scale;
            long y = (long)value.y * scale;
            long z = (long)value.z * scale;
            return TryCell(x, y, z, out result);
        }

        private static bool TryCombined(
            Vector3i first,
            int firstScale,
            Vector3i second,
            int secondScale,
            out Vector3i result) =>
            TryCell(
                (long)first.x * firstScale + (long)second.x * secondScale,
                (long)first.y * firstScale + (long)second.y * secondScale,
                (long)first.z * firstScale + (long)second.z * secondScale,
                out result);

        private static bool TryCell(long x, long y, long z, out Vector3i result)
        {
            int limit = SmartBuildAdvancedToolPlanner.MaximumCoordinateMagnitude;
            if (Math.Abs(x) > limit || Math.Abs(y) > limit || Math.Abs(z) > limit)
            {
                result = new Vector3i(0, 0, 0);
                return false;
            }
            result = new Vector3i((int)x, (int)y, (int)z);
            return true;
        }

        private static string CellKey(Vector3i cell) =>
            cell.x.ToString(CultureInfo.InvariantCulture) + ":" +
            cell.y.ToString(CultureInfo.InvariantCulture) + ":" +
            cell.z.ToString(CultureInfo.InvariantCulture);
    }

    internal readonly struct SmartBuildRegionSpan
    {
        internal SmartBuildRegionSpan(int y, int z, int startX, int length)
        {
            Y = y;
            Z = z;
            StartX = startX;
            Length = length;
        }

        internal int Y { get; }

        internal int Z { get; }

        internal int StartX { get; }

        internal int Length { get; }

        internal IEnumerable<Vector3i> Cells()
        {
            for (int index = 0; index < Length; index++)
                yield return new Vector3i(StartX + index, Y, Z);
        }
    }

    internal sealed class SmartBuildRegionNode : ISmartBuildSceneNode
    {
        private SmartBuildRegionSpan[] _spans;
        private SmartBuildPiece _lastHostSnapshot;

        internal SmartBuildRegionNode(
            SmartBuildPiece hostPiece,
            SmartBuildRegionKind regionKind,
            IEnumerable<Vector3i> cells)
        {
            HostPiece = hostPiece ?? throw new ArgumentNullException(nameof(hostPiece));
            RegionKind = regionKind;
            if (!TryEncode(cells, out _spans, out string reason))
                throw new ArgumentException(reason, nameof(cells));
            if (!_spans.SelectMany(span => span.Cells()).Any(cell => cell.Equals(hostPiece.Origin)))
                throw new ArgumentException("A region must contain its visible host cell.", nameof(cells));
            _lastHostSnapshot = hostPiece.Clone();
        }

        public int Id => HostPiece.Id;

        public long Revision { get; private set; } = 1L;

        public SmartBuildSceneNodeKind Kind => SmartBuildSceneNodeKind.Region;

        public SmartBuildPiece HostPiece { get; }

        public IReadOnlyList<SmartBuildPiece> SourcePieces => new[] { HostPiece.Clone() };

        internal SmartBuildRegionKind RegionKind { get; }

        internal IReadOnlyList<SmartBuildRegionSpan> Spans => _spans;

        internal bool TryReplaceCells(IEnumerable<Vector3i> cells, out string reason)
        {
            if (!TryEncode(cells, out SmartBuildRegionSpan[] spans, out reason))
                return false;
            if (!spans.SelectMany(span => span.Cells()).Any(cell => cell.Equals(HostPiece.Origin)))
            {
                reason = "The refreshed region no longer contains its host cell.";
                return false;
            }
            _spans = spans;
            Revision++;
            return true;
        }

        public bool TryExpand(
            SmartBuildExpansionBudget budget,
            out SmartBuildNodeExpansion expansion,
            out string reason)
        {
            expansion = null;
            if (budget == null)
            {
                reason = "No Smart Builder expansion budget is available.";
                return false;
            }

            SynchronizeHostTranslation();
            Vector3i[] cells = _spans.SelectMany(span => span.Cells()).ToArray();
            if (!budget.TryReserve(cells.Length, out reason))
                return false;
            expansion = new SmartBuildNodeExpansion(
                cells.Select(cell => HostPiece.Duplicate(cell - HostPiece.Origin)));
            return true;
        }

        public bool TryBake(
            int maximumPieces,
            out IReadOnlyList<SmartBuildPiece> pieces,
            out string reason)
        {
            pieces = Array.Empty<SmartBuildPiece>();
            var budget = new SmartBuildExpansionBudget(maximumPieces);
            if (!TryExpand(budget, out SmartBuildNodeExpansion expansion, out reason))
                return false;
            pieces = expansion.Pieces
                .Select(piece => piece.Duplicate(new Vector3i(0, 0, 0)))
                .ToArray();
            return true;
        }

        public IReadOnlyList<SmartBuildPiece> Dissolve() =>
            new[] { HostPiece.Duplicate(new Vector3i(0, 0, 0)) };

        private void SynchronizeHostTranslation()
        {
            Vector3i delta = HostPiece.Origin - _lastHostSnapshot.Origin;
            if (delta.x == 0 && delta.y == 0 && delta.z == 0)
            {
                _lastHostSnapshot = HostPiece.Clone();
                return;
            }

            _spans = _spans
                .Select(span => new SmartBuildRegionSpan(
                    checked(span.Y + delta.y),
                    checked(span.Z + delta.z),
                    checked(span.StartX + delta.x),
                    span.Length))
                .ToArray();
            Revision++;
            _lastHostSnapshot = HostPiece.Clone();
        }

        internal static bool TryEncode(
            IEnumerable<Vector3i> cells,
            out SmartBuildRegionSpan[] spans,
            out string reason)
        {
            spans = Array.Empty<SmartBuildRegionSpan>();
            Vector3i[] ordered = (cells ?? Array.Empty<Vector3i>())
                .GroupBy(CellKey)
                .Select(group => group.First())
                .OrderBy(cell => cell.z)
                .ThenBy(cell => cell.y)
                .ThenBy(cell => cell.x)
                .ToArray();
            if (ordered.Length == 0 ||
                ordered.Length > SmartBuildAdvancedToolPlanner.MaximumFloodFillCells)
            {
                reason = "Regions must contain 1 through " +
                         SmartBuildAdvancedToolPlanner.MaximumFloodFillCells +
                         " cells.";
                return false;
            }

            var encoded = new List<SmartBuildRegionSpan>();
            foreach (IGrouping<string, Vector3i> row in ordered.GroupBy(
                         cell => cell.y.ToString(CultureInfo.InvariantCulture) + ":" +
                                 cell.z.ToString(CultureInfo.InvariantCulture)))
            {
                Vector3i[] rowCells = row.OrderBy(cell => cell.x).ToArray();
                int start = rowCells[0].x;
                int previous = start;
                for (int index = 1; index < rowCells.Length; index++)
                {
                    int current = rowCells[index].x;
                    if ((long)current == (long)previous + 1L)
                    {
                        previous = current;
                        continue;
                    }
                    encoded.Add(new SmartBuildRegionSpan(
                        rowCells[0].y,
                        rowCells[0].z,
                        start,
                        checked(previous - start + 1)));
                    start = previous = current;
                }
                encoded.Add(new SmartBuildRegionSpan(
                    rowCells[0].y,
                    rowCells[0].z,
                    start,
                    checked(previous - start + 1)));
            }

            spans = encoded.ToArray();
            reason = null;
            return true;
        }

        private static string CellKey(Vector3i cell) =>
            cell.x.ToString(CultureInfo.InvariantCulture) + ":" +
            cell.y.ToString(CultureInfo.InvariantCulture) + ":" +
            cell.z.ToString(CultureInfo.InvariantCulture);
    }
}
