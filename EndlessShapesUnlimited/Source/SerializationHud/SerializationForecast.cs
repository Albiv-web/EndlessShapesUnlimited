using System;
using System.Collections.Generic;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;

namespace DecoLimitLifter.SerializationHud
{
    internal sealed class SerializationForecast
    {
        internal SerializationForecast(
            DecorationUsageSnapshot decorations,
            ulong peakHeaderBytes,
            ulong peakDataBytes,
            SerializationWireFormat format,
            bool exact,
            bool uncalibrated,
            BlueprintSerializationUsage blueprintUsage,
            SerializationWireFormat loadedFormat,
            SerializationWireFormat savedFormat)
        {
            Decorations = decorations;
            PeakHeaderBytes = peakHeaderBytes;
            PeakDataBytes = peakDataBytes;
            Format = format;
            Exact = exact;
            Uncalibrated = uncalibrated;
            BlueprintUsage = blueprintUsage ?? BlueprintSerializationUsage.Empty;
            LoadedFormat = loadedFormat;
            SavedFormat = savedFormat;
        }

        internal DecorationUsageSnapshot Decorations { get; }
        internal ulong PeakHeaderBytes { get; }
        internal ulong PeakDataBytes { get; }
        internal SerializationWireFormat Format { get; }
        internal bool Exact { get; }
        internal bool Uncalibrated { get; }
        internal BlueprintSerializationUsage BlueprintUsage { get; }
        internal ulong PayloadBytes => BlueprintUsage.PayloadBytes;
        internal ulong LargestStreamBytes => BlueprintUsage.LargestStreamBytes;
        internal bool RequiresModBuffer => BlueprintUsage.RequiresModBuffer;
        internal SerializationWireFormat LoadedFormat { get; }
        internal SerializationWireFormat SavedFormat { get; }
    }

    internal static class SerializationForecastCalculator
    {
        internal const uint LegacyHeaderMaximum = 65_534U;
        internal const uint LegacyDataMaximum = 6_553_500U;
        internal const uint FallbackHeaderRecords = 512U;
        internal const uint FallbackFixedDataBytes = 4U * 1024U;
        internal const uint FallbackDataBytesPerDecoration = 160U;

        internal static SerializationForecast Calculate(
            MainConstruct mainConstruct,
            DecorationUsageSnapshot current,
            CraftSerializationSnapshot loaded,
            CraftSerializationSnapshot saved,
            CraftSerializationSnapshot measured = null)
        {
            return Calculate(
                mainConstruct?.DesignChangeCounter ?? ulong.MaxValue,
                current,
                loaded,
                saved,
                measured);
        }

        internal static SerializationForecast Calculate(
            ulong currentDesignChangeCounter,
            DecorationUsageSnapshot current,
            CraftSerializationSnapshot loaded,
            CraftSerializationSnapshot saved,
            CraftSerializationSnapshot measured = null)
        {
            CraftSerializationSnapshot baseline = measured ?? saved ?? loaded;
            SerializationWireFormat loadedFormat = loaded?.Format ?? SerializationWireFormat.Unknown;
            SerializationWireFormat savedFormat = saved?.Format ?? SerializationWireFormat.Unknown;

            bool countsMatch = baseline != null && ManagerCountsMatch(
                current.Managers,
                baseline.Decorations.Managers);
            bool exact = baseline != null &&
                         currentDesignChangeCounter == baseline.DesignChangeCounter &&
                         countsMatch;
            if (exact)
            {
                return new SerializationForecast(
                    current,
                    baseline.PeakHeaderBytes,
                    baseline.PeakDataBytes,
                    baseline.Format,
                    exact: true,
                    uncalibrated: false,
                    baseline.BlueprintUsage,
                    loadedFormat,
                    savedFormat);
            }

            ulong peakHeader = baseline?.NonDecorationPeakHeaderBytes ??
                               FallbackHeaderRecords * 7UL;
            ulong peakData = baseline?.NonDecorationPeakDataBytes ??
                             FallbackFixedDataBytes;
            bool uncalibrated = baseline == null;
            var calibrations = new Dictionary<DecorationManager, DecorationManagerCalibration>(
                ReferenceEqualityComparer<DecorationManager>.Instance);
            if (baseline != null)
            {
                foreach (DecorationManagerCalibration calibration in baseline.Calibrations)
                {
                    if (calibration.Manager != null)
                        calibrations[calibration.Manager] = calibration;
                }
            }

            foreach (DecorationManagerUsage manager in current.Managers)
            {
                ulong header;
                ulong data;
                if (manager.Manager != null &&
                    calibrations.TryGetValue(manager.Manager, out var calibration))
                {
                    long countDelta = (long)manager.Count - calibration.DecorationCount;
                    header = ApplySignedDelta(
                        calibration.ContainerHeaderBytes,
                        countDelta * 7L);

                    if (calibration.ContributionMeasured &&
                        calibration.DecorationCount > 0 &&
                        calibration.ContributionDataBytes > 0U)
                    {
                        ulong fixedData = calibration.ContainerDataBytes >=
                                          calibration.ContributionDataBytes
                            ? calibration.ContainerDataBytes - calibration.ContributionDataBytes
                            : 0U;
                        ulong bytesPerDecoration = Math.Max(
                            1UL,
                            calibration.ContributionDataBytes /
                            (ulong)calibration.DecorationCount);
                        data = SaturatingAdd(
                            fixedData,
                            SaturatingMultiply(bytesPerDecoration, (ulong)manager.Count));
                    }
                    else
                    {
                        data = ApplySignedDelta(
                            calibration.ContainerDataBytes,
                            countDelta * (long)FallbackDataBytesPerDecoration);
                        uncalibrated = true;
                    }
                }
                else
                {
                    header = SaturatingAdd(
                        FallbackHeaderRecords * 7UL,
                        SaturatingMultiply(7UL, (ulong)manager.Count));
                    data = SaturatingAdd(
                        FallbackFixedDataBytes,
                        SaturatingMultiply(
                            FallbackDataBytesPerDecoration,
                            (ulong)manager.Count));
                    uncalibrated = true;
                }

                peakHeader = Math.Max(peakHeader, header);
                peakData = Math.Max(peakData, data);
            }

            SerializationWireFormat format;
            if (current.PeakManagerDecorations > DecoLimits.MaxDecorations ||
                peakHeader > (ulong)DecoLimits.MaxHeaderBytes ||
                peakData > (ulong)DecoLimits.MaxDataSortedBytes)
            {
                format = SerializationWireFormat.OverLimit;
            }
            else if (peakHeader > LegacyHeaderMaximum || peakData > LegacyDataMaximum)
            {
                format = SerializationWireFormat.Sentinel;
            }
            else
            {
                format = SerializationWireFormat.Legacy;
            }

            return new SerializationForecast(
                current,
                peakHeader,
                peakData,
                format,
                exact: false,
                uncalibrated,
                baseline?.BlueprintUsage ?? BlueprintSerializationUsage.Empty,
                loadedFormat,
                savedFormat);
        }

        private static bool ManagerCountsMatch(
            IReadOnlyList<DecorationManagerUsage> current,
            IReadOnlyList<DecorationManagerUsage> previous)
        {
            if (current.Count != previous.Count)
                return false;
            var counts = new Dictionary<DecorationManager, int>(
                ReferenceEqualityComparer<DecorationManager>.Instance);
            foreach (DecorationManagerUsage manager in previous)
                counts[manager.Manager] = manager.Count;
            foreach (DecorationManagerUsage manager in current)
            {
                if (!counts.TryGetValue(manager.Manager, out int count) || count != manager.Count)
                    return false;
            }
            return true;
        }

        private static ulong ApplySignedDelta(ulong value, long delta)
        {
            if (delta >= 0L)
                return SaturatingAdd(value, (ulong)delta);
            ulong decrease = delta == long.MinValue
                ? (ulong)long.MaxValue + 1UL
                : (ulong)(-delta);
            return decrease >= value ? 0UL : value - decrease;
        }

        private static ulong SaturatingAdd(ulong left, ulong right) =>
            ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

        private static ulong SaturatingMultiply(ulong left, ulong right) =>
            left != 0UL && right > ulong.MaxValue / left
                ? ulong.MaxValue
                : left * right;
    }
}
