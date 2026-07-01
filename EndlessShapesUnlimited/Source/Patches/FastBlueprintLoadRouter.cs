using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Assets.Scripts.Persistence;
using BrilliantSkies.Core.FilesAndFolders;
using BrilliantSkies.Core.JsonPlus.Converters;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Serialisation.Bytes;
using BrilliantSkies.DataManagement.DataOwnerInterfaces;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Containers;
using DecoLimitLifter.ExtendedSerialization;
using DecoLimitLifter.SerializationHud;
using HarmonyLib;
using Newtonsoft.Json;

namespace DecoLimitLifter.Patches
{
    internal readonly struct FastBlueprintBlockDataRecord
    {
        internal FastBlueprintBlockDataRecord(
            int ordinal,
            int blockIndex,
            uint start,
            uint end)
        {
            Ordinal = ordinal;
            BlockIndex = blockIndex;
            Start = start;
            End = end;
        }

        internal int Ordinal { get; }
        internal int BlockIndex { get; }
        internal uint Start { get; }
        internal uint End { get; }
    }

    internal static class FastBlueprintLoadRouter
    {
        internal const long LargeBlueprintLoadThresholdBytes = 64L * 1024L * 1024L;

        private const byte BlockDataObjectIdBytes = 3;
        private const int JsonBufferBytes = 64 * 1024;

        private static Type ConstructExtraInfoType =>
            ResolveConstructExtraInfoType();
        private static FieldInfo ConstructExtraInfoDataField =>
            AccessTools.Field(ConstructExtraInfoType, "_data");
        private static FieldInfo ConstructExtraInfoVersionField =>
            AccessTools.Field(ConstructExtraInfoType, "_versionSavedAt");
        private static MethodInfo ConstructExtraInfoConstructGetter =>
            ResolveConstructExtraInfoConstructGetter();
        private static readonly FieldInfo BaseFileSourceField =
            AccessTools.Field(typeof(BaseFile), "_fileSource");
        private static readonly MethodInfo BlueprintFileUpdateConstructMethod =
            AccessTools.Method(
                typeof(BlueprintFile),
                "UpdateConstructAndChildren",
                new[] { typeof(Blueprint), typeof(Func<int, int>) });
        private static readonly MethodInfo BlueprintFilePerformVersionUpdatesMethod =
            AccessTools.Method(
                typeof(BlueprintFile),
                "PerformVersionUpdatesOnBlueprint",
                new[] { typeof(Blueprint) });

        internal static MethodBase ResolveBlueprintFileModelLoadDataTarget()
            => AccessTools.Method(
                typeof(BlueprintFile),
                nameof(BlueprintFile.Load),
                new[] { typeof(bool) });

        internal static MethodBase ResolveConstructExtraInfoDataArrayTarget() =>
            ConstructExtraInfoType == null
                ? null
                : AccessTools.Method(ConstructExtraInfoType, "DataArray");

        internal static bool TryLoadBlueprint(
            BlueprintFile file,
            bool setNameFromFile,
            out Blueprint blueprint)
        {
            blueprint = null;
            string path = BlueprintFilePath(file);
            if (!ShouldRoutePath(FastBlueprintLoadTier.V1, path))
                return false;

            Stopwatch timer = BeginDiagnosticPhase("V1 streamed JSON blueprint model load");
            try
            {
                BlueprintFileModel model = LoadBlueprintFileModelFromPath(
                    path,
                    PreserveReferencesHandling.None,
                    JsonConverters.Converters);
                blueprint = PrepareBlueprintFromModel(file, model, setNameFromFile);
                if (blueprint == null)
                    return false;
                EndDiagnosticPhase("V1 streamed JSON blueprint model load", timer);
                return true;
            }
            catch (Exception exception)
            {
                EndDiagnosticPhase("V1 streamed JSON blueprint model load failed", timer);
                LogException("stream blueprint JSON load from " + path, exception);
                return false;
            }
        }

        internal static bool TryHandleConstructExtraInfoDataArray(object instance)
        {
            if (!ShouldUseTier(FastBlueprintLoadTier.V2) ||
                instance == null ||
                ConstructExtraInfoDataField == null ||
                ConstructExtraInfoVersionField == null ||
                ConstructExtraInfoConstructGetter == null)
            {
                return false;
            }

            var data = ConstructExtraInfoDataField.GetValue(instance) as byte[];
            if (!ShouldRoutePayload(FastBlueprintLoadTier.V2, data?.LongLength ?? 0L))
                return false;
            if (data == null || data.Length == 0)
                return true;

            if (CurrentTier == FastBlueprintLoadTier.V3)
                LogDiagnostic("V3 bulk load preflight is not enabled for this path; falling back to V2 serial apply.");

            Stopwatch total = BeginDiagnosticPhase("V2 block-data fast load total");
            try
            {
                var version = ConstructExtraInfoVersionField.GetValue(instance) as Version;
                var construct = ConstructExtraInfoConstructGetter.Invoke(instance, null) as AllConstruct;
                if (construct == null)
                    return false;

                Stopwatch scan = BeginDiagnosticPhase("V2 block-data scan");
                FastBlueprintBlockDataRecord[] records = ScanBlockData(data);
                EndDiagnosticPhase("V2 block-data scan", scan);

                Stopwatch predecode = BeginDiagnosticPhase("V2 block-data parallel predecode");
                SuperLoader[] loaders = PredecodeBlockData(data, records);
                EndDiagnosticPhase("V2 block-data parallel predecode", predecode);

                Stopwatch apply = BeginDiagnosticPhase("V2 block-data serial apply");
                ApplyDecodedBlockData(construct, version, records, loaders);
                EndDiagnosticPhase("V2 block-data serial apply", apply);
                EndDiagnosticPhase("V2 block-data fast load total", total);
                return true;
            }
            catch (Exception exception)
            {
                EndDiagnosticPhase("V2 block-data fast load failed", total);
                LogException("fast-load blueprint block data", exception);
                return false;
            }
        }

        internal static bool ShouldRouteForVerification(
            FastBlueprintLoadTier selected,
            FastBlueprintLoadTier minimum,
            long byteLength,
            bool smallBlueprintTesting)
        {
            return selected >= minimum &&
                   selected != FastBlueprintLoadTier.Off &&
                   (smallBlueprintTesting || byteLength >= LargeBlueprintLoadThresholdBytes);
        }

        internal static BlueprintFileModel LoadBlueprintFileModelFromJsonForVerification(
            string json,
            PreserveReferencesHandling referencesHandling,
            JsonConverter[] converters)
        {
            using (var reader = new StringReader(json ?? string.Empty))
            using (var jsonReader = new JsonTextReader(reader))
            {
                return CreateSerializer(referencesHandling, converters)
                    .Deserialize<BlueprintFileModel>(jsonReader);
            }
        }

        internal static FastBlueprintBlockDataRecord[] ScanBlockDataForVerification(
            byte[] data) =>
            ScanBlockData(data);

        internal static int[] PredecodeBlockIdsForVerification(byte[] data)
        {
            FastBlueprintBlockDataRecord[] records = ScanBlockData(data);
            SuperLoader[] loaders = PredecodeBlockData(data, records);
            return loaders.Select(loader => checked((int)loader.Id)).ToArray();
        }

        internal static Stopwatch BeginDiagnosticPhase(string phase)
        {
            if (!DiagnosticsEnabled)
                return null;
            return Stopwatch.StartNew();
        }

        internal static void EndDiagnosticPhase(string phase, Stopwatch timer)
        {
            if (timer == null)
                return;
            timer.Stop();
            LogDiagnostic(
                phase +
                " took " +
                timer.Elapsed.TotalMilliseconds.ToString("0.0 ms", CultureInfo.InvariantCulture));
        }

        private static BlueprintFileModel LoadBlueprintFileModelFromPath(
            string path,
            PreserveReferencesHandling referencesHandling,
            JsonConverter[] converters)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       JsonBufferBytes,
                       FileOptions.SequentialScan))
            using (var reader = new StreamReader(
                       stream,
                       Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: true,
                       bufferSize: JsonBufferBytes))
            using (var json = new JsonTextReader(reader))
            {
                return CreateSerializer(referencesHandling, converters)
                    .Deserialize<BlueprintFileModel>(json);
            }
        }

        private static JsonSerializer CreateSerializer(
            PreserveReferencesHandling referencesHandling,
            JsonConverter[] converters)
        {
            var serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Auto,
                PreserveReferencesHandling = referencesHandling,
                MaxDepth = null
            };

            foreach (JsonConverter converter in converters ?? Array.Empty<JsonConverter>())
                serializer.Converters.Add(converter);
            return serializer;
        }

        private static FastBlueprintBlockDataRecord[] ScanBlockData(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<FastBlueprintBlockDataRecord>();

            var records = new List<FastBlueprintBlockDataRecord>();
            uint cursor = 0U;
            while (cursor < data.Length)
            {
                uint start = cursor;
                Require(data, cursor, BlockDataObjectIdBytes + 2U, "block-data object id and marker");
                uint objectId = ByteConversion.ConvertOut(
                    data,
                    cursor,
                    BlockDataObjectIdBytes);
                cursor += BlockDataObjectIdBytes;

                uint marker = ByteConversion.ConvertOut(data, cursor, 2);
                cursor += 2U;

                uint headerBytes;
                ulong dataBytes;
                if (marker == SuperSerialisationLayout.Sentinel)
                {
                    Require(data, cursor, 8U, "sentinel block-data lengths");
                    headerBytes = ByteConversion.ConvertOut(data, cursor, 4);
                    cursor += 4U;
                    dataBytes = ByteConversion.ConvertOut(data, cursor, 4);
                    cursor += 4U;
                }
                else
                {
                    headerBytes = marker;
                    Require(data, cursor, 2U, "legacy block-data reserved field");
                    cursor += 2U;
                    dataBytes = ReadLegacyDataLength(data, ref cursor);
                }

                if (headerBytes % 7U != 0U)
                    throw new FormatException("Block-data header byte length is not divisible by seven.");

                ulong payloadBytes = (ulong)headerBytes + dataBytes;
                ulong end = (ulong)cursor + payloadBytes;
                if (end > (ulong)data.Length || end > int.MaxValue)
                    throw new FormatException("Block-data container payload extends beyond the byte array.");

                records.Add(new FastBlueprintBlockDataRecord(
                    records.Count,
                    checked((int)objectId),
                    start,
                    checked((uint)end)));
                cursor = checked((uint)end);
            }

            return records.ToArray();
        }

        private static SuperLoader[] PredecodeBlockData(
            byte[] data,
            FastBlueprintBlockDataRecord[] records)
        {
            var loaders = new SuperLoader[records.Length];
            Parallel.For(
                0,
                records.Length,
                index =>
                {
                    FastBlueprintBlockDataRecord record = records[index];
                    uint cursor = record.Start;
                    var loader = new SuperLoader();
                    ExtendedSuperLoader.Deserialise(
                        loader,
                        data,
                        ref cursor,
                        BlockDataObjectIdBytes);
                    if (cursor != record.End)
                        throw new FormatException("Decoded block-data container ended at an unexpected byte offset.");
                    loaders[index] = loader;
                });
            return loaders;
        }

        private static void ApplyDecodedBlockData(
            AllConstruct construct,
            Version version,
            FastBlueprintBlockDataRecord[] records,
            SuperLoader[] loaders)
        {
            var aliveAndDead = construct.AllBasics.AliveAndDead;
            int blockCount = aliveAndDead.Count;
            for (int i = 0; i < records.Length; i++)
            {
                FastBlueprintBlockDataRecord record = records[i];
                if (record.BlockIndex < 0 || record.BlockIndex >= blockCount)
                {
                    LogError(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Cannot give extra info for block| {0} because only contains {1} blocks. Probably because some blocks were not loaded correctly (duplicate positions) so things were thrown out of order.",
                            record.BlockIndex,
                            blockCount));
                    continue;
                }

                Block block = aliveAndDead[record.BlockIndex];
                if (block == null)
                    continue;

                ((ISaveableDataOwner)block).Load(loaders[i], version, null);
            }
        }

        private static ulong ReadLegacyDataLength(byte[] data, ref uint cursor)
        {
            ulong total = 0UL;
            for (int i = 0; i < SuperSerialisationLayout.MaximumLegacyChunks; i++)
            {
                Require(data, cursor, 2U, "legacy data-length piece");
                uint piece = ByteConversion.ConvertOut(data, cursor, 2);
                cursor += 2U;
                total += piece;
                if (piece < SuperSerialisationLayout.ChunkSize)
                    return total;
            }

            if (total == SuperSerialisationLayout.MaximumLegacyDataBytes)
                return total;
            throw new FormatException("Legacy block-data length did not terminate within 100 pieces.");
        }

        private static bool ShouldRoutePath(
            FastBlueprintLoadTier minimum,
            string path)
        {
            if (!ShouldUseTier(minimum) || string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                return ShouldRoutePayload(minimum, new FileInfo(path).Length);
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldRoutePayload(
            FastBlueprintLoadTier minimum,
            long byteLength)
        {
            SerializationHudProfile.ProfileData data = ProfileData;
            return data != null &&
                   ShouldRouteForVerification(
                       data.FastBlueprintLoadTier,
                       minimum,
                       byteLength,
                       data.FastBlueprintLoadSmallBlueprintTesting);
        }

        private static bool ShouldUseTier(FastBlueprintLoadTier minimum)
        {
            SerializationHudProfile.ProfileData data = ProfileData;
            return data != null &&
                   data.FastBlueprintLoadTier >= minimum &&
                   data.FastBlueprintLoadTier != FastBlueprintLoadTier.Off;
        }

        private static FastBlueprintLoadTier CurrentTier
        {
            get
            {
                try { return SerializationHudProfile.Data.FastBlueprintLoadTier; }
                catch { return FastBlueprintLoadTier.Off; }
            }
        }

        private static bool DiagnosticsEnabled
        {
            get
            {
                try { return SerializationHudProfile.Data.FastBlueprintLoadDiagnostics; }
                catch { return false; }
            }
        }

        private static SerializationHudProfile.ProfileData ProfileData
        {
            get
            {
                try { return SerializationHudProfile.Data; }
                catch { return null; }
            }
        }

        private static Blueprint PrepareBlueprintFromModel(
            BlueprintFile file,
            BlueprintFileModel model,
            bool setNameFromFile)
        {
            Blueprint blueprint = model?.Blueprint;
            if (blueprint == null)
                return null;

            if (Configured.i != null &&
                model.ItemDictionary != null &&
                model.ItemDictionary.Count > 0 &&
                BlueprintFileUpdateConstructMethod != null)
            {
                ModificationComponentContainerItem itemTypes =
                    Configured.i.Get<ModificationComponentContainerItem>();
                int fallback = Math.Max(
                    0,
                    itemTypes.FindTheRuntimeIdOrMinus1(
                        new Guid("9a0ae372-beb4-4009-b14e-36ed0715af73")));
                Func<int, int> translator = new Translator().GetTranslator(
                    model.ItemDictionary,
                    fallback,
                    guid => itemTypes.FindTheRuntimeIdOrMinus1(guid));
                BlueprintFileUpdateConstructMethod.Invoke(
                    file,
                    new object[] { blueprint, translator });
            }

            if (setNameFromFile)
                blueprint.blueprintName = file.Name;

            if (Configured.i != null && BlueprintFilePerformVersionUpdatesMethod != null)
            {
                BlueprintFilePerformVersionUpdatesMethod.Invoke(
                    file,
                    new object[] { blueprint });
            }

            return blueprint;
        }

        private static string BlueprintFilePath(BlueprintFile file)
        {
            var source = BaseFileSourceField?.GetValue(file) as IFileSource;
            return source?.FilePath;
        }

        private static MethodInfo ResolveConstructExtraInfoConstructGetter()
        {
            Type current = ConstructExtraInfoType;
            while (current != null)
            {
                MethodInfo method = AccessTools.Method(current, "get__construct");
                if (method != null)
                    return method;
                current = current.BaseType;
            }
            return null;
        }

        private static Type ResolveConstructExtraInfoType()
        {
            Type type = AccessTools.TypeByName("ConstructExtraInfo");
            if (type != null)
                return type;

            try { return typeof(Blueprint).Assembly.GetType("ConstructExtraInfo"); }
            catch { return null; }
        }

        private static void Require(byte[] data, uint cursor, ulong count, string section)
        {
            ulong end = (ulong)cursor + count;
            if (end > (ulong)(data?.Length ?? 0))
                throw new FormatException("Truncated " + section + ".");
        }

        private static void LogDiagnostic(string message)
        {
            if (!DiagnosticsEnabled)
                return;
            LogInfo("[fast blueprint load] " + message);
        }

        private static void LogInfo(string message)
        {
            try { AdvLogger.LogInfo("[EndlessShapes Unlimited] " + message); }
            catch { }
        }

        private static void LogError(string message)
        {
            try { AdvLogger.LogError("[EndlessShapes Unlimited] " + message, LogOptions._AlertDevInGame); }
            catch { }
        }

        private static void LogException(string action, Exception exception)
        {
            try
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Could not " + action,
                    exception,
                    LogOptions._AlertDevInGame);
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch]
    internal static class BlueprintFile_Load_FastLoad_Patch
    {
        private static MethodBase TargetMethod() =>
            FastBlueprintLoadRouter.ResolveBlueprintFileModelLoadDataTarget();

        private static bool Prefix(
            BlueprintFile __instance,
            [HarmonyArgument(0)] bool setNameFromFile,
            ref Blueprint __result)
        {
            if (!FastBlueprintLoadRouter.TryLoadBlueprint(
                    __instance,
                    setNameFromFile,
                    out Blueprint blueprint))
            {
                return true;
            }

            __result = blueprint;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ConstructExtraInfo_DataArray_FastLoad_Patch
    {
        private static MethodBase TargetMethod() =>
            FastBlueprintLoadRouter.ResolveConstructExtraInfoDataArrayTarget();

        private static bool Prefix(object __instance) =>
            !FastBlueprintLoadRouter.TryHandleConstructExtraInfoDataArray(__instance);
    }

    [HarmonyPatch(typeof(AllConstruct), nameof(AllConstruct.InitialiseStage2))]
    internal static class AllConstruct_InitialiseStage2_FastLoadDiagnostics_Patch
    {
        private static void Prefix(out Stopwatch __state) =>
            __state = FastBlueprintLoadRouter.BeginDiagnosticPhase("block initialization");

        private static void Postfix(Stopwatch __state) =>
            FastBlueprintLoadRouter.EndDiagnosticPhase("block initialization", __state);
    }

    [HarmonyPatch(typeof(AllConstruct), nameof(AllConstruct.Load), new[] { typeof(byte[]), typeof(Version) })]
    internal static class AllConstruct_Load_FastLoadDiagnostics_Patch
    {
        private static void Prefix(out Stopwatch __state) =>
            __state = FastBlueprintLoadRouter.BeginDiagnosticPhase("vehicle-data load");

        private static void Postfix(Stopwatch __state) =>
            FastBlueprintLoadRouter.EndDiagnosticPhase("vehicle-data load", __state);
    }

    [HarmonyPatch(typeof(MainConstruct), nameof(MainConstruct.EverythingLinkedUp))]
    internal static class MainConstruct_EverythingLinkedUp_FastLoadDiagnostics_Patch
    {
        private static void Prefix(out Stopwatch __state) =>
            __state = FastBlueprintLoadRouter.BeginDiagnosticPhase("linkup");

        private static void Postfix(Stopwatch __state) =>
            FastBlueprintLoadRouter.EndDiagnosticPhase("linkup", __state);
    }
}
