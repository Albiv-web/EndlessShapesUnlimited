using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Core.Serialisation.Bytes;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.DataManagement.Serialisation.VariableTypes;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using BrilliantSkies.Modding.Types;
using DecoLimitLifter;
using DecoLimitLifter.DecorationEditMode;
using DecoLimitLifter.ExtendedSerialization;
using DecoLimitLifter.Patches;
using DecoLimitLifter.SerializationHud;
using DecoLimitLifter.SmartBuildMode;
using EndlessShapes2;
using EndlessShapes2.Polygon;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class Program
{
    private const string Owner = "alb.endlessshapesunlimited.verification";
    private static PropertyInfo _headerCountProperty;
    private static string _managedDirectory;
    private static int _passed;

    private static int Main()
    {
        string gameDirectory = Environment.GetEnvironmentVariable("FTD_DIR");
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            Console.Error.WriteLine("FTD_DIR must point to the From The Depths installation.");
            return 2;
        }

        _managedDirectory = Path.Combine(gameDirectory, "From_The_Depths_Data", "Managed");
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name);
            string candidate = Path.Combine(_managedDirectory, name.Name + ".dll");
            return File.Exists(candidate)
                ? Assembly.LoadFrom(candidate)
                : null;
        };

        try
        {
            return Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        _headerCountProperty = typeof(SuperBase).GetProperty(
            "HeaderCount",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
            throw new MissingMemberException(typeof(SuperBase).FullName, "HeaderCount");

        byte[] vanillaLegacyFixture = CaptureVanillaLegacyFixture();
        var harmony = new Harmony(Owner);
        try
        {
            ApplySerializerPatches(harmony);
            VerifyPatchInstallation();
            VerifyVanillaLegacyCompatibility(vanillaLegacyFixture);
            VerifyLegacyRoundTrip();
            VerifySentinelRoundTrip();
            VerifyHeaderFormatBoundary();
            VerifyDataFormatBoundary();
            VerifyHeaderGrowthPreservesData();
            VerifyDataGrowthPreservesData();
            VerifyLoaderPromotesGrownPools();
            VerifyExactMultipleConvertToReader();
            VerifySentinelConvertToReader();
            VerifyByteStoreGrowth();
            VerifyAutoSyncBufferGrowth();
            VerifySequentialSharedBufferGrowth();
            VerifyUnknownDestinationRejected();
            VerifyZeroLengthLoaderBuffers();
            VerifyAutoSyncTargetResolution();
            VerifyDecorationLimitRollback();
            VerifyOversizedPacketRejected();
            VerifyTruncatedPacketRejected();
            VerifyInvalidHeaderLengthRejected();
            VerifyInvalidHeaderOffsetRejected();
            VerifyConfiguredSerializerCeilings();
            VerifyObjectIdWidthValidation();
            VerifyObjParser();
            VerifyObjParserLimits();
            VerifyOptionalExternalObj();
            VerifyFilePathInput();
            VerifyFlexibleFloatParsing();
            VerifyImagePreflight();
            VerifyGeometryClassification();
            VerifyGeometryRejectionAndLimit();
            VerifyLargeGeometryRun();
            VerifyTetherMath();
            VerifyTetherTransaction();
            VerifyExporterFormatting();
            VerifyUiPatchTarget();
            VerifyActiveStatusApi();
            VerifySerializationHudTargets();
            VerifySerializationTelemetryAggregation();
            VerifyBlueprintSerializationUsageAnalyzer();
            VerifyBlueprintSerializationUsageSampleFiles();
            VerifySerializationForecasting();
            VerifySerializationTelemetryScopes();
            VerifySerializationHudProfiles();
            VerifyDecorationEditModeMvp();
            VerifySmartBlockBuilder();
            VerifyBeamificationBundle();
            VerifyPackageIdentityAndAssets();

            Console.WriteLine($"PASS: {_passed} verification checks completed.");
            return 0;
        }
        finally
        {
            harmony.UnpatchAll(Owner);
        }
    }

    private static void ApplySerializerPatches(Harmony harmony)
    {
        string[] patchTypeNames =
        {
            "DecoLimitLifter.Patches.ByteStorePatch",
            "DecoLimitLifter.Patches.SuperLoader_Deserialise_All_Patch",
            "DecoLimitLifter.Patches.SuperSaverBuffersPatch",
            "DecoLimitLifter.Patches.SuperSaver_ConvertToReader_BufferPatch",
            "DecoLimitLifter.Patches.SuperSaver_WriteHeader_Guard",
            "DecoLimitLifter.Patches.SuperSaver_ByIdHelpWrite_Guard",
            "DecoLimitLifter.Patches.SuperSaver_Serialise_Patch",
            "DecoLimitLifter.SerializationHud.BlueprintConverter_SaveTelemetry_Patch",
            "DecoLimitLifter.SerializationHud.BlueprintConverter_LoadTelemetry_Patch",
            "DecoLimitLifter.SerializationHud.Decoration_SaveTelemetry_Patch",
            "DecoLimitLifter.SerializationHud.DecorationManager_LoadTelemetry_Patch"
        };

        Assembly modAssembly = typeof(ExtendedSuperSaver).Assembly;
        foreach (string patchTypeName in patchTypeNames)
        {
            Type patchType = modAssembly.GetType(patchTypeName, throwOnError: true);
            harmony.CreateClassProcessor(patchType).Patch();
        }
    }

    private static void VerifyPatchInstallation()
    {
        MethodBase[] required =
        {
            AccessTools.Method(typeof(SuperSaver), nameof(SuperSaver.Serialise)),
            AccessTools.Method(typeof(SuperSaver), nameof(SuperSaver.ConvertToReader)),
            AccessTools.Method(typeof(SuperSaver), nameof(SuperSaver.WriteHeader)),
            AccessTools.Method(typeof(SuperLoader), nameof(SuperLoader.Deserialise))
        };

        var requiredMethods = required.ToList();
        requiredMethods.Add(Plugin.ResolveBlueprintSaveTarget());
        requiredMethods.Add(Plugin.ResolveBlueprintLoadTarget());
        requiredMethods.Add(Plugin.ResolveDecorationSaveTarget());
        requiredMethods.Add(Plugin.ResolveDecorationLoadTarget());
        InterfaceMapping writeMap = typeof(SuperSaver).GetInterfaceMap(typeof(IVariableWriteHelp));
        for (int i = 0; i < writeMap.InterfaceMethods.Length; i++)
        {
            MethodInfo method = writeMap.InterfaceMethods[i];
            ParameterInfo[] parameters = method.GetParameters();
            if (method.Name == nameof(IVariableWriteHelp.ByIdHelpWrite) &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(uint) &&
                parameters[1].ParameterType == typeof(uint))
            {
                requiredMethods.Add(writeMap.TargetMethods[i]);
                break;
            }
        }

        foreach (MethodBase method in requiredMethods)
        {
            Assert(method != null, "Required Harmony target resolves.");
            Assert(Harmony.GetPatchInfo(method)?.Owners?.Contains(Owner) == true,
                $"Harmony owner is attached to {method.DeclaringType?.Name}.{method.Name}.");
        }

        MethodBase constructor = AccessTools.Constructor(typeof(SuperSaver), Type.EmptyTypes);
        VerifyExactPatch(
            constructor,
            AccessTools.Method(typeof(SuperSaverBuffersPatch), "ConstructorPrefix"),
            prefix: true,
            "SuperSaver pool constructor prefix is the exact required method.");
        VerifyExactPatch(
            constructor,
            AccessTools.Method(typeof(ByteStorePatch), "AfterSuperSaverConstructor"),
            prefix: false,
            "ByteStore constructor postfix is the exact required method.");

        MethodBase blueprintSave = Plugin.ResolveBlueprintSaveTarget();
        VerifyExactPatch(
            blueprintSave,
            AccessTools.Method(
                typeof(BlueprintConverter_SaveTelemetry_Patch),
                nameof(BlueprintConverter_SaveTelemetry_Patch.Prefix)),
            prefix: true,
            "Blueprint save telemetry prefix is the exact required method.");
        VerifyExactPatch(
            blueprintSave,
            AccessTools.Method(
                typeof(BlueprintConverter_SaveTelemetry_Patch),
                nameof(BlueprintConverter_SaveTelemetry_Patch.Postfix)),
            prefix: false,
            "Blueprint save telemetry postfix is the exact required method.");
        VerifyExactFinalizer(
            blueprintSave,
            AccessTools.Method(
                typeof(BlueprintConverter_SaveTelemetry_Patch),
                nameof(BlueprintConverter_SaveTelemetry_Patch.Finalizer)),
            "Blueprint save telemetry finalizer is the exact required method.");
        VerifyExactFinalizer(
            Plugin.ResolveBlueprintLoadTarget(),
            AccessTools.Method(
                typeof(BlueprintConverter_LoadTelemetry_Patch),
                nameof(BlueprintConverter_LoadTelemetry_Patch.Finalizer)),
            "Blueprint load telemetry finalizer is the exact required method.");
        Assert(Plugin.ResolveDecorationEditorHudTarget("DrawRhs") != null &&
               Plugin.ResolveDecorationEditorHudTarget("DrawInteractionIcon") != null &&
               Plugin.ResolveDecorationEditorHudTarget("DrawWeaponInfo") != null &&
               Plugin.ResolveDecorationEditorHudTarget("DisplayCorrectToolBar") != null &&
               Plugin.ResolveDecorationEditorBuildUpdateTarget() != null &&
               Plugin.ResolveDecorationEditorCameraUpdateTarget() != null,
            "Decoration editor HUD/input Harmony targets resolve without installing UI patches in the non-Unity verifier.");
    }

    private static void VerifyExactPatch(
        MethodBase target,
        MethodInfo patchMethod,
        bool prefix,
        string description)
    {
        HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(target);
        IEnumerable<Patch> patches = prefix ? patchInfo?.Prefixes : patchInfo?.Postfixes;
        Assert(
            target != null &&
            patchMethod != null &&
            patches?.Any(patch => patch.owner == Owner && patch.PatchMethod == patchMethod) == true,
            description);
    }

    private static void VerifyExactFinalizer(
        MethodBase target,
        MethodInfo patchMethod,
        string description)
    {
        HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(target);
        Assert(
            target != null &&
            patchMethod != null &&
            patchInfo?.Finalizers?.Any(
                patch => patch.owner == Owner && patch.PatchMethod == patchMethod) == true,
            description);
    }

    private static byte[] CaptureVanillaLegacyFixture()
    {
        var saver = NewSaver(headerCount: 1, dataBytes: 4);
        ByteConversion.ConvertIn(saver.Header, 0U, 3, 123U);
        ByteConversion.ConvertInLegacyElements(saver.Header, 3U, 0U);
        saver.DataSorted[0] = 11;
        saver.DataSorted[1] = 22;
        saver.DataSorted[2] = 33;
        saver.DataSorted[3] = 44;

        var buffer = new byte[128];
        uint cursor = 0U;
        saver.Serialise(buffer, ref cursor, 42UL, 1);
        var fixture = new byte[checked((int)cursor)];
        Array.Copy(buffer, fixture, fixture.Length);
        return fixture;
    }

    private static void VerifyVanillaLegacyCompatibility(byte[] vanillaFixture)
    {
        var saver = NewSaver(headerCount: 1, dataBytes: 4);
        ByteConversion.ConvertIn(saver.Header, 0U, 3, 123U);
        ByteConversion.ConvertInLegacyElements(saver.Header, 3U, 0U);
        saver.DataSorted[0] = 11;
        saver.DataSorted[1] = 22;
        saver.DataSorted[2] = 33;
        saver.DataSorted[3] = 44;

        var buffer = new byte[128];
        uint cursor = 0U;
        saver.Serialise(buffer, ref cursor, 42UL, 1);
        Assert(cursor == vanillaFixture.Length &&
               buffer.Take((int)cursor).SequenceEqual(vanillaFixture),
            "Patched legacy output is byte-for-byte identical to the game's original serializer.");
    }

    private static void VerifyLegacyRoundTrip()
    {
        var saver = NewSaver(headerCount: 1, dataBytes: 4);
        ByteConversion.ConvertIn(saver.Header, 0U, 3, 123U);
        ByteConversion.ConvertInLegacyElements(saver.Header, 3U, 0U);
        saver.DataSorted[0] = 11;
        saver.DataSorted[1] = 22;
        saver.DataSorted[2] = 33;
        saver.DataSorted[3] = 44;

        var buffer = new byte[128];
        uint cursor = 0U;
        saver.Serialise(buffer, ref cursor, 42UL, 1);
        Assert(buffer[1] == 7 && buffer[2] == 0, "Small payload uses the legacy format.");

        uint read = 0U;
        var loader = new SuperLoader();
        ulong id = loader.Deserialise(buffer, ref read, 1);
        Assert(id == 42UL && read == cursor, "Legacy round trip preserves ID and cursor.");
        Assert(loader.HeaderCount == 1U && loader._totalDataLengthSorted == 4U,
            "Legacy round trip preserves declared lengths.");
        Assert(loader.DataSorted.Take(4).SequenceEqual(new byte[] { 11, 22, 33, 44 }),
            "Legacy round trip preserves payload bytes.");
    }

    private static void VerifySentinelRoundTrip()
    {
        var saver = NewSaver(headerCount: 9_363, dataBytes: 0);
        ByteConversion.ConvertIn(saver.Header, 0U, 3, 321U);
        ByteConversion.ConvertInLegacyElements(saver.Header, 3U, 0U);

        var buffer = new byte[70_000];
        uint cursor = 0U;
        saver.Serialise(buffer, ref cursor, 43UL, 1);
        Assert(buffer[1] == byte.MaxValue && buffer[2] == byte.MaxValue,
            "Oversized header uses the sentinel format.");

        uint read = 0U;
        var loader = new SuperLoader();
        ulong id = loader.Deserialise(buffer, ref read, 1);
        Assert(id == 43UL && loader.HeaderCount == 9_363U && read == cursor,
            "Sentinel round trip preserves ID, header count, and cursor.");
    }

    private static void VerifyHeaderFormatBoundary()
    {
        var legacySaver = NewSaver(headerCount: 9_362, dataBytes: 0);
        var legacyBuffer = new byte[70_000];
        uint legacyCursor = 0U;
        legacySaver.Serialise(legacyBuffer, ref legacyCursor, 1UL, 1);
        Assert(!(legacyBuffer[1] == byte.MaxValue && legacyBuffer[2] == byte.MaxValue),
            "9,362 headers remain in the legacy format.");

        var sentinelSaver = NewSaver(headerCount: 9_363, dataBytes: 0);
        var sentinelBuffer = new byte[70_000];
        uint sentinelCursor = 0U;
        sentinelSaver.Serialise(sentinelBuffer, ref sentinelCursor, 1UL, 1);
        Assert(sentinelBuffer[1] == byte.MaxValue && sentinelBuffer[2] == byte.MaxValue,
            "9,363 headers cross to the sentinel format.");
    }

    private static void VerifyDataFormatBoundary()
    {
        const uint maximumLegacyData = 100U * 65_535U;
        var legacySaver = NewSaver(headerCount: 0, dataBytes: maximumLegacyData);
        var legacyBuffer = new byte[checked((int)(maximumLegacyData + 256U))];
        uint legacyCursor = 0U;
        legacySaver.Serialise(legacyBuffer, ref legacyCursor, 1UL, 1);
        Assert(!(legacyBuffer[1] == byte.MaxValue && legacyBuffer[2] == byte.MaxValue),
            "Exactly 100 full data-length pieces remain legacy-compatible.");
        uint legacyRead = 0U;
        var legacyLoader = new SuperLoader();
        legacyLoader.Deserialise(legacyBuffer, ref legacyRead, 1);
        Assert(legacyLoader._totalDataLengthSorted == maximumLegacyData && legacyRead == legacyCursor,
            "The maximum legacy data length round-trips without a terminator piece.");

        var sentinelSaver = NewSaver(headerCount: 0, dataBytes: maximumLegacyData + 1U);
        var sentinelBuffer = new byte[checked((int)(maximumLegacyData + 256U))];
        uint sentinelCursor = 0U;
        sentinelSaver.Serialise(sentinelBuffer, ref sentinelCursor, 1UL, 1);
        Assert(sentinelBuffer[1] == byte.MaxValue && sentinelBuffer[2] == byte.MaxValue,
            "The first byte beyond 100 legacy pieces uses sentinel.");
        uint sentinelRead = 0U;
        var sentinelLoader = new SuperLoader();
        sentinelLoader.Deserialise(sentinelBuffer, ref sentinelRead, 1);
        Assert(sentinelLoader._totalDataLengthSorted == maximumLegacyData + 1U &&
               sentinelRead == sentinelCursor,
            "The first sentinel data length round-trips exactly.");
    }

    private static void VerifyHeaderGrowthPreservesData()
    {
        SuperSaverReusableByteArray.Header = new byte[70_000];
        var saver = new SuperSaver();
        SetHeaderCount(saver, 10_000U);
        saver.Header[0] = 123;
        saver.WriteHeader(20_000U);

        Assert(saver.Header.Length >= 70_007, "Header guard grows beyond the vanilla pool.");
        Assert(saver.Header[0] == 123, "Header guard preserves existing header bytes.");
        Assert(ReferenceEquals(saver.Header, SuperSaverReusableByteArray.Header),
            "Grown header becomes the reusable pool.");
    }

    private static void VerifyDataGrowthPreservesData()
    {
        SuperSaverReusableByteArray.DataSorted = new byte[2_000_000];
        var saver = new SuperSaver
        {
            _datasWrittenSorted = 1_999_900U
        };
        saver.DataSorted[0] = 123;
        ((IVariableWriteHelp)saver).ByIdHelpWrite(7U, 255U);

        Assert(saver.DataSorted.Length >= 2_000_158, "Data guard grows beyond the vanilla pool.");
        Assert(saver.DataSorted[0] == 123, "Data guard preserves existing data bytes.");
        Assert(ReferenceEquals(saver.DataSorted, SuperSaverReusableByteArray.DataSorted),
            "Grown data array becomes the reusable pool.");
    }

    private static void VerifyLoaderPromotesGrownPools()
    {
        var saver = NewSaver(headerCount: 20_000, dataBytes: 3_000_000);
        saver.Header = new byte[140_000];
        saver.DataSorted = new byte[3_000_000];

        var packet = new byte[3_200_000];
        uint write = 0U;
        saver.Serialise(packet, ref write, 1UL, 1);

        SuperSaverReusableByteArray.Header = new byte[70_000];
        SuperSaverReusableByteArray.DataSorted = new byte[2_000_000];
        var loader = new SuperLoader();
        uint read = 0U;
        loader.Deserialise(packet, ref read, 1);

        Assert(ReferenceEquals(loader.Header, SuperSaverReusableByteArray.Header) &&
               loader.Header.Length >= 140_000,
            "Loader promotes a grown header array to the reusable pool.");
        Assert(ReferenceEquals(loader.DataSorted, SuperSaverReusableByteArray.DataSorted) &&
               loader.DataSorted.Length >= 3_000_000,
            "Loader promotes a grown data array to the reusable pool.");
    }

    private static void VerifyExactMultipleConvertToReader()
    {
        var saver = NewSaver(headerCount: 0, dataBytes: 65_535);
        SuperLoader loader = saver.ConvertToReader();
        Assert(loader._totalDataLengthSorted == 65_535U,
            "ConvertToReader accounts for the zero terminator after an exact 65,535-byte chunk.");
    }

    private static void VerifySentinelConvertToReader()
    {
        var saver = NewSaver(headerCount: 9_363, dataBytes: 0);
        SuperLoader loader = saver.ConvertToReader();
        Assert(loader.HeaderCount == 9_363U,
            "ConvertToReader allocates the exact sentinel metadata size.");
    }

    private static void VerifyByteStoreGrowth()
    {
        var saver = NewSaver(headerCount: 0, dataBytes: 100);
        ByteStore.MegaBytes = new byte[64];
        ByteStore.MegaBytes[0] = 77;
        uint cursor = 16U;
        saver.Serialise(ByteStore.MegaBytes, ref cursor, 1UL, 1);

        Assert(ByteStore.MegaBytes.Length >= cursor, "ByteStore grows from the exact required output size.");
        Assert(ByteStore.MegaBytes[0] == 77, "ByteStore growth preserves the existing stream prefix.");
    }

    private static void VerifyAutoSyncBufferGrowth()
    {
        Type autoSyncType = typeof(SuperSaver).Assembly.GetType(
            "BrilliantSkies.DataManagement.Synching.AutoSyncroniser",
            throwOnError: true);
        FieldInfo bufferField = autoSyncType.GetField(
            "fullArray",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingFieldException(autoSyncType.FullName, "fullArray");

        byte[] original = (byte[])bufferField.GetValue(null);
        try
        {
            var small = new byte[64];
            small[0] = 88;
            bufferField.SetValue(null, small);

            var saver = NewSaver(headerCount: 0, dataBytes: 100);
            uint cursor = 16U;
            saver.Serialise((byte[])bufferField.GetValue(null), ref cursor, 1UL, 1);

            var grown = (byte[])bufferField.GetValue(null);
            Assert(grown.Length >= cursor, "AutoSyncroniser output buffer grows on exact demand.");
            Assert(grown[0] == 88, "AutoSyncroniser growth preserves the stream prefix.");
        }
        finally
        {
            bufferField.SetValue(null, original);
        }
    }

    private static void VerifySequentialSharedBufferGrowth()
    {
        byte[] originalByteStore = ByteStore.MegaBytes;
        FieldInfo autoSyncField = ResolveAutoSyncBufferField();
        byte[] originalAutoSync = (byte[])autoSyncField.GetValue(null);
        try
        {
            ByteStore.MegaBytes = new byte[32];
            ByteStore.MegaBytes[0] = 41;
            uint byteStoreCursor = 8U;
            NewSaver(0, 100).Serialise(ByteStore.MegaBytes, ref byteStoreCursor, 1UL, 1);
            NewSaver(0, 200).Serialise(ByteStore.MegaBytes, ref byteStoreCursor, 2UL, 1);
            Assert(ByteStore.MegaBytes.Length >= byteStoreCursor && ByteStore.MegaBytes[0] == 41,
                "Sequential ByteStore writes use the promoted shared buffer and preserve the prefix.");

            var smallAutoSync = new byte[32];
            smallAutoSync[0] = 42;
            autoSyncField.SetValue(null, smallAutoSync);
            uint autoSyncCursor = 8U;
            NewSaver(0, 100).Serialise(
                (byte[])autoSyncField.GetValue(null), ref autoSyncCursor, 3UL, 1);
            NewSaver(0, 200).Serialise(
                (byte[])autoSyncField.GetValue(null), ref autoSyncCursor, 4UL, 1);
            byte[] promoted = (byte[])autoSyncField.GetValue(null);
            Assert(promoted.Length >= autoSyncCursor && promoted[0] == 42,
                "Sequential AutoSyncroniser writes reload the promoted shared buffer.");
        }
        finally
        {
            ByteStore.MegaBytes = originalByteStore;
            autoSyncField.SetValue(null, originalAutoSync);
        }
    }

    private static void VerifyUnknownDestinationRejected()
    {
        var saver = NewSaver(0, 100);
        Expect<IndexOutOfRangeException>(() =>
        {
            uint cursor = 0U;
            saver.Serialise(new byte[16], ref cursor, 1UL, 1);
        }, "Unknown undersized destination buffers are rejected instead of being replaced.");
    }

    private static void VerifyZeroLengthLoaderBuffers()
    {
        var saver = NewSaver(0, 0);
        var packet = new byte[32];
        uint written = 0U;
        saver.Serialise(packet, ref written, 9UL, 1);

        var loader = new SuperLoader
        {
            Header = null,
            DataSorted = null
        };
        uint read = 0U;
        loader.Deserialise(packet, ref read, 1);
        Assert(loader.Header != null && loader.DataSorted != null && read == written,
            "Zero-length loader payloads receive non-null safe buffers.");
    }

    private static void VerifyAutoSyncTargetResolution()
    {
        DestinationBuffer.VerifyAutoSyncTarget();
        FieldInfo field = ResolveAutoSyncBufferField();
        Assert(field.FieldType == typeof(byte[]),
            "AutoSyncroniser.fullArray resolves as the exact required byte-array field.");
    }

    private static void VerifyDecorationLimitRollback()
    {
        int currentLimit = 200;
        bool unpatched = false;
        var startup = new StartupTransaction(() => unpatched = true);
        int previous = currentLimit;
        currentLimit = DecoLimits.MaxDecorations;
        startup.TrackDecorationLimit(previous, value => currentLimit = value);
        IReadOnlyList<Exception> errors = startup.Rollback();
        Assert(currentLimit == previous && unpatched && errors.Count == 0,
            "Failed startup restores the preceding decoration limit and unpatches Harmony.");

        bool unpatchAfterRestoreFailure = false;
        var failing = new StartupTransaction(() => unpatchAfterRestoreFailure = true);
        failing.TrackDecorationLimit(previous, _ => throw new InvalidOperationException("test"));
        IReadOnlyList<Exception> rollbackErrors = failing.Rollback();
        Assert(unpatchAfterRestoreFailure && rollbackErrors.Count == 1,
            "Startup rollback keeps unpatching when decoration-limit restoration fails.");

        int committedLimit = DecoLimits.MaxDecorations;
        bool committedUnpatch = false;
        var committed = new StartupTransaction(() => committedUnpatch = true);
        committed.TrackDecorationLimit(previous, value => committedLimit = value);
        committed.Commit();
        Assert(committed.Rollback().Count == 0 &&
               committedLimit == DecoLimits.MaxDecorations && !committedUnpatch,
            "A committed startup cannot be invalidated by later success-reporting failure.");

        var rollbackOrder = new List<int>();
        var ordered = new StartupTransaction(() => rollbackOrder.Add(0));
        ordered.TrackRollback(() => rollbackOrder.Add(1));
        ordered.TrackRollback(() => rollbackOrder.Add(2));
        ordered.Rollback();
        Assert(rollbackOrder.SequenceEqual(new[] { 2, 1, 0 }),
            "Startup registrations roll back in reverse order before Harmony is removed.");

        bool laterCleanupRan = false;
        var independent = new StartupTransaction(() => { });
        independent.TrackRollback(() => laterCleanupRan = true);
        independent.TrackRollback(() => throw new InvalidOperationException("test"));
        Assert(independent.Rollback().Count == 1 && laterCleanupRan,
            "A failed HUD cleanup cannot prevent the remaining startup rollback actions.");
    }

    private static FieldInfo ResolveAutoSyncBufferField()
    {
        Type autoSyncType = typeof(SuperSaver).Assembly.GetType(
            "BrilliantSkies.DataManagement.Synching.AutoSyncroniser",
            throwOnError: true);
        return autoSyncType.GetField(
                   "fullArray",
                   BindingFlags.Static | BindingFlags.NonPublic) ??
               throw new MissingFieldException(autoSyncType.FullName, "fullArray");
    }

    private static void VerifyOversizedPacketRejected()
    {
        var packet = new byte[11];
        uint cursor = 1U;
        ByteConversion.ConvertIn(packet, cursor, 2, ushort.MaxValue);
        cursor += 2U;
        ByteConversion.ConvertIn(packet, cursor, 4, 0U);
        cursor += 4U;
        ByteConversion.ConvertIn(packet, cursor, 4, 64U * 1024U * 1024U + 1U);

        Expect<FormatException>(() =>
        {
            uint read = 0U;
            new SuperLoader().Deserialise(packet, ref read, 1);
        }, "Oversized sentinel data length is rejected before allocation.");
    }

    private static void VerifyTruncatedPacketRejected()
    {
        Expect<FormatException>(() =>
        {
            uint read = 0U;
            new SuperLoader().Deserialise(Array.Empty<byte>(), ref read, 1);
        }, "Truncated object ID is rejected instead of returning without cursor progress.");
    }

    private static void VerifyInvalidHeaderLengthRejected()
    {
        var packet = new byte[8];
        uint cursor = 1U;
        ByteConversion.ConvertIn(packet, cursor, 2, 1U);
        cursor += 2U;
        ByteConversion.ConvertIn(packet, cursor, 2, 0U);
        cursor += 2U;
        ByteConversion.ConvertIn(packet, cursor, 2, 0U);

        Expect<FormatException>(() =>
        {
            uint read = 0U;
            new SuperLoader().Deserialise(packet, ref read, 1);
        }, "Header lengths that are not multiples of seven are rejected.");
    }

    private static void VerifyInvalidHeaderOffsetRejected()
    {
        var saver = NewSaver(headerCount: 1, dataBytes: 0);
        ByteConversion.ConvertIn(saver.Header, 0U, 3, 1U);
        ByteConversion.ConvertInLegacyElements(saver.Header, 3U, 1U);
        var packet = new byte[64];
        uint cursor = 0U;
        saver.Serialise(packet, ref cursor, 1UL, 1);

        Expect<FormatException>(() =>
        {
            uint read = 0U;
            new SuperLoader().Deserialise(packet, ref read, 1);
        }, "Header segment offsets beyond the data block are rejected.");
    }

    private static void VerifyConfiguredSerializerCeilings()
    {
        var headerSaver = NewSaver(headerCount: 0, dataBytes: 0);
        SetHeaderCount(headerSaver, 4U * 1024U * 1024U / 7U + 1U);
        Expect<InvalidOperationException>(() =>
        {
            uint cursor = 0U;
            headerSaver.Serialise(new byte[64], ref cursor, 1UL, 1);
        }, "Serializer rejects headers above the configured ceiling before allocation.");

        var dataSaver = NewSaver(headerCount: 0, dataBytes: 0);
        dataSaver._datasWrittenSorted = 64U * 1024U * 1024U + 1U;
        Expect<InvalidOperationException>(() =>
        {
            uint cursor = 0U;
            dataSaver.Serialise(new byte[64], ref cursor, 1UL, 1);
        }, "Serializer rejects data above the configured ceiling before allocation.");
    }

    private static void VerifyObjectIdWidthValidation()
    {
        var saver = NewSaver(headerCount: 0, dataBytes: 0);
        Expect<ArgumentException>(() =>
        {
            uint cursor = 0U;
            saver.Serialise(new byte[64], ref cursor, 256UL, 1);
        }, "Object IDs that do not fit the requested width are rejected.");
    }

    private static void VerifyObjParser()
    {
        const string obj = @"
# invariant decimals, extra whitespace, and negative indices
v  1.5  0  0
v 0 2.25 0
v 0 0 3.75
vt 0.1 0.2
vt 0.3 0.4
vt 0.5 0.6
o Example mesh
f -3/-3 -2/-2 -1/-1
l 1 2 3
";

        CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            ObjParseResult result = ObjParser.Parse(new StringReader(obj));
            Assert(result.Vertices.Count == 3, "OBJ parser reads invariant-culture vertices.");
            Assert(result.TextureCoordinates.Count == 3,
                "OBJ parser reads texture coordinates.");
            Assert(result.Meshes.Count == 1 && result.Meshes[0].Name == "Example mesh",
                "OBJ parser preserves mesh names.");
            Assert(result.Meshes[0].FaceDatas.Count == 1 &&
                   result.Meshes[0].FaceDatas[0][0][0] == 2 &&
                   result.Meshes[0].FaceDatas[0][2][0] == 0,
                "OBJ parser resolves negative indices and preserves mirrored winding.");
            Assert(result.Meshes[0].LineDatas.Single().SequenceEqual(new[] { 0, 1, 2 }),
                "OBJ parser reads line primitives.");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }

        const string commonFaceForms = @"
v 0 0 0
v 1 0 0
v 0 1 0
vn 0 0 1
f 1//1 2//1 3//1
";
        ObjParseResult defaultGroup = ObjParser.Parse(new StringReader(commonFaceForms));
        Assert(defaultGroup.Meshes.Count == 1 && defaultGroup.Meshes[0].Name == "Default",
            "OBJ parser creates a default group and accepts v//vn faces.");

        Expect<InvalidDataException>(() => ObjParser.Parse(new StringReader(@"
v 0 0 0
v 1 0 0
v 0 1 0
f 0 2 3
")), "OBJ parser rejects zero indices with a format error.");
    }

    private static void VerifyObjParserLimits()
    {
        Assert(ObjParser.MaxFileBytes == 256L * 1024L * 1024L,
            "OBJ input has a 256 MiB file ceiling.");
        Assert(ObjParser.MaxVertices == 2_000_000,
            "OBJ input has a two-million-vertex ceiling.");
        Assert(ObjParser.MaxTextureCoordinates == 2_000_000,
            "OBJ input has a two-million-UV ceiling.");
        Assert(ObjParser.MaxPrimitives == 100_000,
            "OBJ input is capped at the decoration limit.");
    }

    private static void VerifyOptionalExternalObj()
    {
        string path = Environment.GetEnvironmentVariable("ESU_VERIFY_OBJ");
        if (string.IsNullOrWhiteSpace(path))
            return;

        ObjParseResult result = ObjParser.ParseFile(path);
        int faces = result.Meshes.Sum(mesh => mesh.FaceDatas.Count);
        int lines = result.Meshes.Sum(mesh => mesh.LineDatas.Count);
        var vertices = result.Vertices
            .Select(vertex => new Vector3(-vertex.X, vertex.Y, vertex.Z))
            .ToList();
        var uvs = result.TextureCoordinates
            .Select(uv => new Vector2(uv.X, uv.Y))
            .ToList();
        int decorations = 0;
        foreach (OBJ_Mesh mesh in result.Meshes)
        {
            var plan = new List<PolygonData>();
            PolygonDataControl.PolygonClassify(
                plan,
                mesh.FaceDatas,
                mesh.LineDatas,
                vertices,
                uvs,
                mesh.FaceSourceLines,
                mesh.LineSourceLines,
                100_000 - decorations);
            decorations += plan.Count;
        }
        Console.WriteLine(
            $"PASS: External OBJ parsed: {result.Vertices.Count:N0} vertices, " +
            $"{faces:N0} faces, {lines:N0} lines, {result.Meshes.Count:N0} mesh groups, " +
            $"{decorations:N0} planned decorations.");
    }

    private static void VerifyFilePathInput()
    {
        string normalized = FilePathInput.Normalize(
            "  \"C:\\Models With Spaces\\ship.obj\"  ");
        Assert(normalized == "C:\\Models With Spaces\\ship.obj",
            "Pasted file paths preserve spaces while trimming whitespace and matching quotes.");

        string missing = FilePathInput.MissingFileMessage(
            "OBJ",
            "C:\\Private Profile\\Downloads\\ship.obj");
        Assert(missing.Contains("ship.obj") && !missing.Contains("Private Profile"),
            "Missing-file feedback names the file without exposing its full local path in an alert.");
    }

    private static void VerifyFlexibleFloatParsing()
    {
        CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert(FlexibleFloatParser.TryParse("0.05", out float invariant) &&
                   Math.Abs(invariant - 0.05f) < 0.000001f,
                "Float input accepts an invariant decimal point under de-DE without treating it as thousands.");
            Assert(FlexibleFloatParser.TryParse("0,05", out float local) &&
                   Math.Abs(local - 0.05f) < 0.000001f,
                "Float input accepts the current culture decimal comma.");
            Assert(!FlexibleFloatParser.TryParse("0,", out _) &&
                   !FlexibleFloatParser.TryParse("-", out _),
                "Incomplete float input is rejected without producing a replacement value.");
            Assert(!FlexibleFloatParser.TryParse("NaN", out _) &&
                   !FlexibleFloatParser.TryParse("Infinity", out _),
                "NaN and infinity are rejected by UI numeric parsing.");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    private static void VerifyImagePreflight()
    {
        using (var png = new MemoryStream(PngHeader(640, 480)))
        {
            ImageDimensions dimensions = ImagePreflight.ReadDimensions(png);
            ImagePreflight.Validate(dimensions);
            Assert(dimensions.Width == 640 && dimensions.Height == 480,
                "PNG dimensions are read before Unity texture decoding.");
        }

        using (var jpeg = new MemoryStream(JpegHeader(320, 200)))
        {
            ImageDimensions dimensions = ImagePreflight.ReadDimensions(jpeg);
            ImagePreflight.Validate(dimensions);
            Assert(dimensions.Width == 320 && dimensions.Height == 200,
                "JPEG dimensions are read before Unity texture decoding.");
        }

        Expect<InvalidDataException>(
            () => ImagePreflight.Validate(new ImageDimensions(8_193, 1)),
            "Texture dimensions above 8,192 pixels are rejected before decoding.");
        Expect<InvalidDataException>(
            () => ImagePreflight.Validate(new ImageDimensions(4_097, 4_097)),
            "Textures above 16,777,216 total pixels are rejected before decoding.");
    }

    private static void VerifyGeometryClassification()
    {
        var output = new List<PolygonData>();

        var rightVertices = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f)
        };
        PolygonDataControl.TriangleClassify(output, Face(0, 1, 2), rightVertices, null);

        var isoscelesVertices = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 0f, 0f),
            new Vector3(1f, 0f, 3f)
        };
        PolygonDataControl.TriangleClassify(output, Face(0, 1, 2), isoscelesVertices, null);

        var scaleneVertices = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(4f, 0f, 0f),
            new Vector3(1f, 0f, 2f)
        };
        PolygonDataControl.TriangleClassify(output, Face(0, 1, 2), scaleneVertices, null);

        var rectangleVertices = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 0f, 0f),
            new Vector3(2f, 0f, 1f),
            new Vector3(0f, 0f, 1f)
        };
        Assert(PolygonDataControl.RectangleClassify(
                output, Face(0, 1, 2, 3), rectangleVertices, null),
            "Rectangle geometry is recognized without triangulation.");

        var ellipseVertices = EllipseVertices();
        Assert(PolygonDataControl.EllipseClassify(
                output,
                Face(Enumerable.Range(0, 16).ToArray()),
                ellipseVertices,
                null),
            "Sixteen-point ellipse geometry is recognized without triangulation.");

        PolygonDataControl.LineClassify(
            output,
            new[] { 0, 1 },
            new List<Vector3> { Vector3.zero, Vector3.forward });

        PolygonType[] emitted = output.Select(polygon => polygon.PolyType).Distinct().ToArray();
        Assert(Enum.GetValues(typeof(PolygonType)).Cast<PolygonType>().All(emitted.Contains),
            "Every supported polygon type produces a bounded decoration plan entry.");

        MADCD_PolygonInput.ValidateGeneratedTransform(
            new Vector3(1f, 2f, 3f),
            new Vector3(0.05f, 2f, 3f),
            new Vector3(0f, 90f, 180f),
            0);
        Pass("The generated-transform guard accepts finite position, scale, and rotation values.");
        Expect<InvalidDataException>(() => MADCD_PolygonInput.ValidateGeneratedTransform(
                new Vector3(float.NaN, 0f, 0f),
                Vector3.one,
                Vector3.zero,
                79),
            "The generated-transform guard rejects non-finite output.");

        var uvOutput = new List<PolygonData>();
        var faces = new List<int[][]>
        {
            Face(0, 1, 2),
            new[] { new[] { 0, 0 }, new[] { 1, 1 }, new[] { 2, 2 } }
        };
        var uvs = new List<Vector2>
        {
            new Vector2(0.3f, 0.3f),
            new Vector2(0.6f, 0.3f),
            new Vector2(0.3f, 0.6f)
        };
        PolygonDataControl.PolygonClassify(
            uvOutput,
            faces,
            new List<int[]>(),
            rightVertices,
            uvs,
            new[] { 10, 11 },
            Array.Empty<int>(),
            10);
        Assert(uvOutput.Count == 2 && uvOutput[0].UV == Vector2.zero && uvOutput[1].UV != Vector2.zero,
            "UV availability is determined independently for each face.");
    }

    private static void VerifyGeometryRejectionAndLimit()
    {
        ExpectGeometryError(
            new List<Vector3> { Vector3.zero, Vector3.right, Vector3.forward },
            Face(0, 1, 1),
            71,
            "repeated vertex",
            "Repeated face points are rejected with source-line diagnostics.");
        ExpectGeometryError(
            new List<Vector3> { Vector3.zero, Vector3.zero, Vector3.forward },
            Face(0, 1, 2),
            72,
            "zero-length edge",
            "Transform-collapsed face edges are rejected with source-line diagnostics.");
        ExpectGeometryError(
            new List<Vector3> { Vector3.zero, Vector3.right, Vector3.right * 2f },
            Face(0, 1, 2),
            73,
            "zero area",
            "Collinear faces are rejected with source-line diagnostics.");
        ExpectGeometryError(
            new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(2f, 0f, 0f),
                new Vector3(1f, 0f, 1f),
                new Vector3(2f, 0f, 2f),
                new Vector3(0f, 0f, 2f)
            },
            Face(0, 1, 2, 3, 4),
            74,
            "concave",
            "Concave faces are rejected instead of being repaired silently.");
        ExpectGeometryError(
            new List<Vector3>
            {
                Vector3.zero,
                Vector3.right,
                new Vector3(float.NaN, 0f, 1f)
            },
            Face(0, 1, 2),
            75,
            "non-finite",
            "Non-finite transformed geometry is rejected with source-line diagnostics.");
        ExpectGeometryError(
            new List<Vector3>
            {
                new Vector3(float.MaxValue, 0f, 0f),
                new Vector3(-float.MaxValue, 0f, 0f),
                Vector3.forward
            },
            Face(0, 1, 2),
            76,
            "non-finite",
            "Finite inputs whose derived edge math overflows are rejected with source-line diagnostics.");

        var bounded = new List<PolygonData>();
        Expect<InvalidDataException>(() => PolygonDataControl.PolygonClassify(
                bounded,
                new List<int[][]> { Face(0, 1, 2) },
                new List<int[]>(),
                new List<Vector3>
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(4f, 0f, 0f),
                    new Vector3(1f, 0f, 2f)
                },
                null,
                new[] { 77 },
                Array.Empty<int>(),
                1),
            "Expanded scalene-triangle output is stopped at the final plan cap.");
        Assert(bounded.Count <= 1, "Plan-cap enforcement never allocates beyond the configured output limit.");
        Expect<InvalidDataException>(
            () => new PolygonDecorationSettings(false, 0f, 0.05f, StructureBlockType.Metal).Validate(0),
            "Zero face thickness is rejected before construct state changes.");
    }

    private static void VerifyLargeGeometryRun()
    {
        const int count = 100_000;
        var faces = new List<int[][]>(count);
        for (int index = 0; index < count; index++)
            faces.Add(Face(0, 1, 2));

        var output = new List<PolygonData>(count);
        var stopwatch = Stopwatch.StartNew();
        PolygonDataControl.PolygonClassify(
            output,
            faces,
            new List<int[]>(),
            new List<Vector3> { Vector3.zero, Vector3.right, Vector3.forward },
            null,
            null,
            null,
            count);
        stopwatch.Stop();
        Assert(output.Count == count && stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            "A 100,000-entry geometry run completes with bounded queue/index processing.");
    }

    private static void VerifyTetherMath()
    {
        Assert(DecorationTetherMove.IsOffsetWithinBounds(new Vector3(10f, -10f, 0f)) &&
               !DecorationTetherMove.IsOffsetWithinBounds(new Vector3(10.001f, 0f, 0f)) &&
               !DecorationTetherMove.IsOffsetWithinBounds(new Vector3(float.PositiveInfinity, 0f, 0f)),
            "Tether preflight accepts only finite positioning within the inclusive +/-10 bounds.");
        var axis = DecorationTetherMove.DominantAxis(new Vector3(0.2f, -0.9f, 0.3f));
        Assert(axis.x == 0 && axis.y == -1 && axis.z == 0,
            "Tether movement selects one deterministic dominant local axis.");
        Assert(TetherMoveRules.IsExpectedSource(TetherMoveRules.TetherBlockGuid) &&
               !TetherMoveRules.IsExpectedSource(Guid.NewGuid()),
            "Tether movement rejects pointed blocks with the wrong component GUID.");
        Assert(!TetherMoveRules.TryMovePosition(
                new Vector3(-10f, 0f, 0f),
                Vector3.right,
                out _),
            "Tether preflight aborts the complete move when one resulting offset is out of range.");
    }

    private static void VerifyTetherTransaction()
    {
        var commandEvents = new List<string>();
        var commandFailure = new TetherMoveTransaction<int>(new[] { 0, 1 }).Execute(
            () =>
            {
                commandEvents.Add("place");
                return true;
            },
            () => commandEvents.Add("undo-place"),
            () =>
            {
                commandEvents.Add("remove");
                return false;
            },
            () => commandEvents.Add("undo-remove"),
            _ => commandEvents.Add("apply"),
            entry => commandEvents.Add("restore-" + entry));
        Assert(!commandFailure.Succeeded &&
               commandEvents.SequenceEqual(new[]
               {
                   "place", "remove", "undo-remove", "undo-place"
               }),
            "A block-command failure rolls back entries and commands in reverse operation order.");

        int[] values = { 10, 20 };
        var propertyEvents = new List<string>();
        var propertyFailure = new TetherMoveTransaction<int>(new[] { 0, 1 }).Execute(
            () => true,
            () => propertyEvents.Add("undo-place"),
            () => true,
            () => propertyEvents.Add("undo-remove"),
            entry =>
            {
                values[entry] += 100;
                if (entry == 1)
                    throw new InvalidOperationException("property failure");
            },
            entry =>
            {
                values[entry] = entry == 0 ? 10 : 20;
                propertyEvents.Add("restore-" + entry);
            });
        Assert(!propertyFailure.Succeeded && values.SequenceEqual(new[] { 10, 20 }) &&
               propertyEvents.SequenceEqual(new[]
               {
                   "restore-1", "restore-0", "undo-remove", "undo-place"
               }),
            "A decoration-property failure completely restores the journal and both block commands.");

        bool sourceUndone = false;
        bool destinationUndone = false;
        var rollbackFailure = new TetherMoveTransaction<int>(new[] { 0 }).Execute(
            () => true,
            () => destinationUndone = true,
            () => true,
            () => sourceUndone = true,
            _ => throw new InvalidOperationException("property failure"),
            _ => throw new InvalidOperationException("restore failure"));
        Assert(!rollbackFailure.Succeeded && rollbackFailure.RollbackErrors.Count == 1 &&
               sourceUndone && destinationUndone,
            "Tether rollback actions remain independent when one restoration action throws.");
    }

    private static void VerifyExporterFormatting()
    {
        CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert(OBJ_FileCreation.Number(1.5f) == "1.5",
                "OBJ export always emits invariant decimal points under de-DE.");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }

        Expect<InvalidDataException>(() => OBJ_FileCreation.Number(float.NaN),
            "OBJ export rejects non-finite numbers.");
        Assert(OBJ_FileCreation.SanitizeFileName("  valid:name.  ", "fallback")
                .IndexOfAny(Path.GetInvalidFileNameChars()) < 0,
            "OBJ export sanitizes generated filesystem names.");

        string firstCollisionName = OBJ_FileCreation.AssetNameWithGuid(
            "shared-texture", new Guid("11111111-1111-1111-1111-111111111111"), "Texture");
        string secondCollisionName = OBJ_FileCreation.AssetNameWithGuid(
            "shared-texture", new Guid("11111111-2222-2222-2222-222222222222"), "Texture");
        Assert(firstCollisionName != secondCollisionName,
            "Texture filenames remain collision-proof when source names match.");

        var textureRegistry = new Dictionary<Guid, string>();
        Guid textureGuid = Guid.NewGuid();
        int nameCreations = 0;
        string textureName = OBJ_FileCreation.GetOrAddTextureName(
            textureRegistry, textureGuid, () => "texture-" + ++nameCreations, out bool firstAdded);
        string sharedTextureName = OBJ_FileCreation.GetOrAddTextureName(
            textureRegistry, textureGuid, () => "texture-" + ++nameCreations, out bool secondAdded);
        Assert(firstAdded && !secondAdded && nameCreations == 1 && textureName == sharedTextureName,
            "A texture referenced by multiple used materials is encoded once.");

        IReadOnlyList<SubMeshBinding<string>> bindings = OBJ_FileCreation.BindSubMeshes(
            3,
            new[] { "material-0", "material-1" });
        Assert(bindings.Count == 2 &&
               bindings[0].SubMesh == 0 && bindings[0].Material == "material-0" &&
               bindings[1].SubMesh == 1 && bindings[1].Material == "material-1",
            "Carried-object submeshes bind to the matching sharedMaterials entry.");

        var usedMaterials = new Dictionary<int, string>();
        OBJ_FileCreation.TrackIfEmitted(usedMaterials, 1, "unused", emitted: false);
        OBJ_FileCreation.TrackIfEmitted(usedMaterials, 2, "used", emitted: true);
        Assert(usedMaterials.Count == 1 && usedMaterials[2] == "used",
            "Exporter material tracking contains only materials with emitted geometry.");

        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "EndlessShapesUnlimited-verifier-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            string finalFolder = Path.Combine(temporaryRoot, "Export");
            Expect<InvalidOperationException>(() => OBJ_FileCreation.CommitStagedDirectory(
                    finalFolder,
                    staging =>
                    {
                        File.WriteAllText(Path.Combine(staging, "partial.obj"), "partial");
                        throw new InvalidOperationException("simulated export failure");
                    }),
                "A failed OBJ export propagates the write failure.");
            Assert(!Directory.Exists(finalFolder) &&
                   Directory.GetDirectories(temporaryRoot, "*.partial-*").Length == 0,
                "A failed OBJ export removes its partial directory and never publishes the final folder.");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void VerifyUiPatchTarget()
    {
        Assembly ftd = Assembly.LoadFrom(Path.Combine(_managedDirectory, "FtD.dll"));
        Type generalTab = ftd.GetType(
            "BrilliantSkies.Ftd.Constructs.UI.GeneralTab",
            throwOnError: true);
        MethodInfo mesh = generalTab.GetMethod(
            "Mesh",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(mesh != null && EndlessShapes2Patch.ResolveTarget() == mesh,
            "Current FTD GeneralTab.Mesh target resolves for the OBJ export patch.");
    }

    private static void VerifyActiveStatusApi()
    {
        Assembly modding = Assembly.LoadFrom(Path.Combine(_managedDirectory, "Modding.dll"));
        Type modProblems = modding.GetType(
            "BrilliantSkies.Modding.ModProblems",
            throwOnError: true);
        MethodInfo addProblem = modProblems.GetMethod(
            "AddModProblem",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(string), typeof(string), typeof(string), typeof(bool) },
            null);
        MemberInfo allProblems = modProblems.GetMember(
            "AllModProblems",
            BindingFlags.Public | BindingFlags.Static).SingleOrDefault();

        Assert(addProblem != null && allProblems != null,
            "Current FTD active-mod status APIs resolve without forcing GUI activation during boot.");
    }

    private static void VerifySerializationHudTargets()
    {
        MethodBase[] targets =
        {
            Plugin.ResolveBlueprintSaveTarget(),
            Plugin.ResolveBlueprintLoadTarget(),
            Plugin.ResolveDecorationSaveTarget(),
            Plugin.ResolveDecorationLoadTarget(),
            Plugin.ResolveSerializationHudTarget()
        };
        Assert(targets.All(target => target != null),
            "Current FTD blueprint, decoration-manager, and native HUD targets resolve exactly.");

        Assert(
            targets[0].GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(new[] { typeof(MainConstruct), typeof(bool) }) &&
            targets[4].GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(new[]
                {
                    typeof(MainConstruct),
                    Type.GetType("BrilliantSkies.Ftd.Avatar.HUD.Rectum, FtD", throwOnError: true)
                }),
            "Serialization HUD verification selects the intended overloads, not name-only matches.");
    }

    private static void VerifySerializationTelemetryAggregation()
    {
        var emptyUsage = new DecorationUsageSnapshot(
            Array.Empty<DecorationManagerUsage>(),
            0L,
            0);
        var containers = new[]
        {
            new SerializationContainerSample(
                new object(),
                50_000U,
                100U,
                SerializationWireFormat.Legacy),
            new SerializationContainerSample(
                new object(),
                100U,
                7_000_000U,
                SerializationWireFormat.Sentinel)
        };
        CraftSerializationSnapshot snapshot = CraftSerializationSnapshotFactory.Create(
            SerializationOperationKind.Save,
            containers,
            7UL,
            emptyUsage);
        Assert(snapshot.PeakHeaderBytes == 50_000U && snapshot.PeakDataBytes == 7_000_000U,
            "HUD telemetry reports independent peak-container header and data usage instead of sums.");
        Assert(snapshot.Format == SerializationWireFormat.Sentinel &&
               snapshot.SentinelContainerCount == 1 && snapshot.ContainerCount == 2,
            "One sentinel container classifies the complete blueprint serialization as sentinel.");
        Assert(snapshot.TotalHeaderBytes == 50_100UL &&
               snapshot.TotalDataBytes == 7_000_100UL &&
               snapshot.TotalWireBytes == 7_050_200UL &&
               snapshot.PeakContainerWireBytes == 7_000_100UL,
            "HUD telemetry keeps aggregate wire/header/data totals separate from peak-container limits.");
    }

    private static void VerifyBlueprintSerializationUsageAnalyzer()
    {
        byte[] legacy = SerialiseFixture(headerCount: 1U, dataBytes: 4U, objectId: 42UL, objectIdBytes: 3);
        byte[] sentinel = SerialiseFixture(headerCount: 9_363U, dataBytes: 0U, objectId: 43UL, objectIdBytes: 8);
        Blueprint blueprint = NewBlueprintForUsageTest();
        blueprint.BlockData = legacy;
        blueprint.VehicleData = sentinel;
        blueprint.BLP = new Vector3[2];
        blueprint.BLR = new int[2];
        blueprint.BCI = new int[2];
        blueprint.SCs = new List<Blueprint>();

        BlueprintSerializationUsage usage =
            BlueprintSerializationUsageAnalyzer.Analyze(blueprint);
        Assert(usage.BlockDataBytes == (ulong)legacy.Length &&
               usage.VehicleDataBytes == (ulong)sentinel.Length &&
               usage.PayloadBytes == (ulong)(legacy.Length + sentinel.Length),
            "Blueprint usage analyzer reports exact raw BlockData and VehicleData byte-array sizes.");
        Assert(usage.ContainerCount == 2 &&
               usage.SentinelContainerCount == 1 &&
               usage.TotalWireBytes == usage.PayloadBytes &&
               usage.PeakHeaderBytes == 9_363UL * 7UL,
            "Blueprint usage analyzer parses mixed legacy/sentinel container streams without copying payloads.");
        Assert(!usage.RequiresModBuffer &&
               !usage.AggregatePayloadExceedsVanillaBuffer &&
               usage.StructuralEntries >= 6UL,
            "Blueprint usage analyzer distinguishes ordinary payloads from ESU-buffer-sized payloads.");

        byte[] splitA = SerialiseFixture(
            headerCount: 1U,
            dataBytes: 5_100_000U,
            objectId: 44UL,
            objectIdBytes: 3);
        byte[] splitB = SerialiseFixture(
            headerCount: 1U,
            dataBytes: 5_100_000U,
            objectId: 45UL,
            objectIdBytes: 3);
        Blueprint splitChild = NewBlueprintForUsageTest();
        splitChild.BlockData = splitB;
        splitChild.VehicleData = Array.Empty<byte>();
        splitChild.SCs = new List<Blueprint>();
        Blueprint splitParent = NewBlueprintForUsageTest();
        splitParent.BlockData = splitA;
        splitParent.VehicleData = Array.Empty<byte>();
        splitParent.SCs = new List<Blueprint> { splitChild };
        BlueprintSerializationUsage splitUsage =
            BlueprintSerializationUsageAnalyzer.Analyze(splitParent);
        Assert(splitUsage.PayloadBytes > (ulong)DecoLimits.VanillaSaveBufferBytes &&
               splitUsage.LargestStreamBytes < (ulong)DecoLimits.VanillaSaveBufferBytes &&
               splitUsage.AggregatePayloadExceedsVanillaBuffer &&
               !splitUsage.RequiresModBuffer &&
               splitUsage.SentinelContainerCount == 0,
            "Blueprint usage analyzer treats split vanilla-sized streams as vanilla buffer compatible even when aggregate payload exceeds 10 MB.");

        Blueprint largeBlueprint = NewBlueprintForUsageTest();
        largeBlueprint.BlockData = new byte[DecoLimits.VanillaSaveBufferBytes + 1];
        largeBlueprint.VehicleData = Array.Empty<byte>();
        largeBlueprint.SCs = new List<Blueprint>();
        BlueprintSerializationUsage largeUsage =
            BlueprintSerializationUsageAnalyzer.Analyze(largeBlueprint);
        Assert(largeUsage.RequiresModBuffer &&
               largeUsage.AggregatePayloadExceedsVanillaBuffer,
            "Blueprint usage analyzer flags individual streams that exceed vanilla ByteStore capacity.");
    }

    private static void VerifyBlueprintSerializationUsageSampleFiles()
    {
        string folder = Path.Combine(
            "F:\\",
            "FTD saves",
            "From The Depths",
            "Player Profiles",
            "New",
            "Constructables");
        VerifyBlueprintSampleIfPresent(
            Path.Combine(folder, "OneBlockTest.blueprint"),
            expectedVehicleDataBytes: 652UL,
            expectedBlockDataBytes: 0UL,
            expectedSavedBlocks: 1,
            expectedBlockIds: 1);
        VerifyBlueprintSampleIfPresent(
            Path.Combine(folder, "TwoBlockTest.blueprint"),
            expectedVehicleDataBytes: 659UL,
            expectedBlockDataBytes: 0UL,
            expectedSavedBlocks: 2,
            expectedBlockIds: 2);
        VerifyBlueprintSampleIfPresent(
            Path.Combine(folder, "OneBlockTestWithOneDeco.blueprint"),
            expectedVehicleDataBytes: 741UL,
            expectedBlockDataBytes: 0UL,
            expectedSavedBlocks: 1,
            expectedBlockIds: 1);
        VerifyBlueprintSampleIfPresent(
            Path.Combine(folder, "4004 Gun Ironclad Super Heavy 1st Rate AA.blueprint"),
            expectedVehicleDataBytes: 16_207UL,
            expectedBlockDataBytes: 17_473_046UL,
            expectedSavedBlocks: 3_257_602,
            expectedBlockIds: 3_219_209,
            expectedFileBytes: 100_353_303L,
            expectedBlockDataContainers: 407_216,
            expectedVehicleDataContainers: 18);
    }

    private static void VerifyBlueprintSampleIfPresent(
        string path,
        ulong expectedVehicleDataBytes,
        ulong expectedBlockDataBytes,
        int expectedSavedBlocks,
        int expectedBlockIds,
        long? expectedFileBytes = null,
        int? expectedBlockDataContainers = null,
        int? expectedVehicleDataContainers = null)
    {
        if (!File.Exists(path))
        {
            Pass("Optional blueprint sample is absent: " + Path.GetFileName(path));
            return;
        }

        JObject root = JObject.Parse(File.ReadAllText(path));
        JObject blueprintJson = (JObject)root["Blueprint"];
        byte[] blockData = Convert.FromBase64String((string)blueprintJson["BlockData"] ?? string.Empty);
        byte[] vehicleData = Convert.FromBase64String((string)blueprintJson["VehicleData"] ?? string.Empty);
        Blueprint blueprint = NewBlueprintForUsageTest();
        blueprint.BlockData = blockData;
        blueprint.VehicleData = vehicleData;
        blueprint.SCs = new List<Blueprint>();
        BlueprintSerializationUsage usage =
            BlueprintSerializationUsageAnalyzer.Analyze(blueprint);

        Assert(usage.BlockDataBytes == expectedBlockDataBytes &&
               usage.VehicleDataBytes == expectedVehicleDataBytes,
            "Blueprint sample raw payload bytes match expected values for " + Path.GetFileName(path));
        Assert((int)root["SavedTotalBlockCount"] == expectedSavedBlocks &&
               ((JArray)blueprintJson["BlockIds"]).Count == expectedBlockIds,
            "Blueprint sample block counters match expected values for " + Path.GetFileName(path));
        if (expectedFileBytes.HasValue)
        {
            Assert(new FileInfo(path).Length == expectedFileBytes.Value &&
                   usage.RequiresModBuffer &&
                   usage.LargestStreamBytes > (ulong)DecoLimits.VanillaSaveBufferBytes,
                "Large blueprint sample confirms a legacy wire stream can still require ESU save buffers.");
        }
        if (expectedBlockDataContainers.HasValue || expectedVehicleDataContainers.HasValue)
        {
            Blueprint blockOnlyBlueprint = NewBlueprintForUsageTest();
            blockOnlyBlueprint.BlockData = blockData;
            blockOnlyBlueprint.VehicleData = Array.Empty<byte>();
            blockOnlyBlueprint.SCs = new List<Blueprint>();
            Blueprint vehicleOnlyBlueprint = NewBlueprintForUsageTest();
            vehicleOnlyBlueprint.BlockData = Array.Empty<byte>();
            vehicleOnlyBlueprint.VehicleData = vehicleData;
            vehicleOnlyBlueprint.SCs = new List<Blueprint>();
            BlueprintSerializationUsage blockOnly =
                BlueprintSerializationUsageAnalyzer.Analyze(blockOnlyBlueprint);
            BlueprintSerializationUsage vehicleOnly =
                BlueprintSerializationUsageAnalyzer.Analyze(vehicleOnlyBlueprint);
            Assert(blockOnly.ContainerCount == expectedBlockDataContainers.GetValueOrDefault() &&
                   vehicleOnly.ContainerCount == expectedVehicleDataContainers.GetValueOrDefault() &&
                   blockOnly.SentinelContainerCount == 0 &&
                   vehicleOnly.SentinelContainerCount == 0,
                "Large blueprint sample exact containers remain legacy while the largest stream requires ESU.");
        }
    }

    private static void VerifySerializationForecasting()
    {
        var manager = new DecorationManager(10U, 11U, 12U);
        var savedUsage = new DecorationUsageSnapshot(
            new[] { new DecorationManagerUsage(manager, 90) },
            90L,
            90);
        var container = new SerializationContainerSample(
            new object(),
            1_000U,
            20_000U,
            SerializationWireFormat.Legacy);
        container.Calibrations.Add(new DecorationManagerCalibration(
            manager,
            90,
            1_000U,
            20_000U,
            700U,
            9_000U,
            contributionMeasured: true));
        CraftSerializationSnapshot saved = CraftSerializationSnapshotFactory.Create(
            SerializationOperationKind.Save,
            new[] { container },
            25UL,
            savedUsage);

        SerializationForecast exact = SerializationForecastCalculator.Calculate(
            25UL,
            savedUsage,
            loaded: null,
            saved);
        Assert(exact.Exact && exact.PeakHeaderBytes == 1_000U &&
               exact.PeakDataBytes == 20_000U,
            "An unchanged craft keeps the exact captured serializer measurements.");

        var changedUsage = new DecorationUsageSnapshot(
            new[] { new DecorationManagerUsage(manager, 100) },
            100L,
            100);
        SerializationForecast forecast = SerializationForecastCalculator.Calculate(
            26UL,
            changedUsage,
            loaded: null,
            saved);
        Assert(!forecast.Exact && forecast.PeakHeaderBytes == 1_070U,
            "Live header forecasting applies the deterministic seven-byte decoration delta.");
        Assert(forecast.PeakDataBytes == 21_000U && !forecast.Uncalibrated,
            "Live data forecasting uses the measured decoration bytes-per-item calibration.");

        var unknownManager = new DecorationManager(20U, 21U, 22U);
        var unknownUsage = new DecorationUsageSnapshot(
            new[] { new DecorationManagerUsage(unknownManager, 100) },
            100L,
            100);
        SerializationForecast uncalibrated = SerializationForecastCalculator.Calculate(
            1UL,
            unknownUsage,
            loaded: null,
            saved: null);
        Assert(uncalibrated.Uncalibrated &&
               uncalibrated.PeakHeaderBytes ==
                   SerializationForecastCalculator.FallbackHeaderRecords * 7UL + 700UL &&
               uncalibrated.PeakDataBytes ==
                   SerializationForecastCalculator.FallbackFixedDataBytes + 16_000UL,
            "A craft without telemetry uses the documented conservative forecast baseline.");

        var sentinelUsage = new DecorationUsageSnapshot(
            new[] { new DecorationManagerUsage(unknownManager, 10_000) },
            10_000L,
            10_000);
        Assert(SerializationForecastCalculator.Calculate(
                   1UL,
                   sentinelUsage,
                   null,
                   null).Format == SerializationWireFormat.Sentinel,
            "Forecasting identifies the sentinel wire format before the next save.");

        var overLimitUsage = new DecorationUsageSnapshot(
            new[] { new DecorationManagerUsage(unknownManager, DecoLimits.MaxDecorations + 1) },
            DecoLimits.MaxDecorations + 1L,
            DecoLimits.MaxDecorations + 1);
        Assert(SerializationForecastCalculator.Calculate(
                   1UL,
                   overLimitUsage,
                   null,
                   null).Format == SerializationWireFormat.OverLimit,
            "Forecasting distinguishes a hard-limit violation from ordinary sentinel output.");
    }

    private static void VerifySerializationTelemetryScopes()
    {
        SerializationTelemetry.ResetForTests();
        var outer = SerializationTelemetry.Begin(SerializationOperationKind.Save);
        var inner = SerializationTelemetry.Begin(SerializationOperationKind.Load);
        Assert(SerializationTelemetry.CurrentDepthForTests == 2,
            "Nested blueprint telemetry scopes retain the complete operation stack.");
        inner.Dispose();
        Assert(SerializationTelemetry.CurrentDepthForTests == 1,
            "Completing an inner telemetry scope restores its parent operation.");

        int otherThreadInitialDepth = -1;
        int otherThreadActiveDepth = -1;
        var thread = new Thread(() =>
        {
            otherThreadInitialDepth = SerializationTelemetry.CurrentDepthForTests;
            using (SerializationTelemetry.Begin(SerializationOperationKind.Load))
                otherThreadActiveDepth = SerializationTelemetry.CurrentDepthForTests;
        });
        thread.Start();
        thread.Join();
        Assert(otherThreadInitialDepth == 0 && otherThreadActiveDepth == 1 &&
               SerializationTelemetry.CurrentDepthForTests == 1,
            "Blueprint telemetry operation state is isolated per serialization thread.");
        outer.Dispose();
        Assert(SerializationTelemetry.CurrentDepthForTests == 0,
            "Telemetry scope disposal leaves no state for unrelated multiplayer serialization.");

        var baselineSaver = NewSaver(1U, 4U);
        var measuredSaver = NewSaver(1U, 4U);
        ByteConversion.ConvertIn(baselineSaver.Header, 0U, 3, 55U);
        ByteConversion.ConvertIn(measuredSaver.Header, 0U, 3, 55U);
        baselineSaver.DataSorted[0] = measuredSaver.DataSorted[0] = 1;
        baselineSaver.DataSorted[1] = measuredSaver.DataSorted[1] = 2;
        baselineSaver.DataSorted[2] = measuredSaver.DataSorted[2] = 3;
        baselineSaver.DataSorted[3] = measuredSaver.DataSorted[3] = 4;
        var baselineBytes = new byte[128];
        var measuredBytes = new byte[128];
        uint baselineCursor = 0U;
        uint measuredCursor = 0U;
        baselineSaver.Serialise(baselineBytes, ref baselineCursor, 9UL, 1);
        using (SerializationTelemetry.Begin(SerializationOperationKind.Save))
            measuredSaver.Serialise(measuredBytes, ref measuredCursor, 9UL, 1);
        Assert(baselineCursor == measuredCursor &&
               baselineBytes.Take((int)baselineCursor)
                   .SequenceEqual(measuredBytes.Take((int)measuredCursor)),
            "Active HUD telemetry does not change any serializer output byte.");
    }

    private static void VerifySerializationHudProfiles()
    {
        var data = new SerializationHudProfile.ProfileData();
        Assert(!data.Enabled,
            "The serialization HUD is disabled by default for a new profile.");

        PropertyInfo filename = typeof(SerializationHudProfile).GetProperty(
            "FilenameAndExtension",
            BindingFlags.Instance | BindingFlags.NonPublic);
        PropertyInfo keyFilename = typeof(SerializationHudKeyMap).GetProperty(
            "FilenameAndExtension",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(GetSingleStringLiteral(filename?.GetMethod) ==
                   "profile.endlessshapesunlimited" &&
               GetSingleStringLiteral(keyFilename?.GetMethod) ==
                   "profile.keymappingendlessshapesunlimited",
            "HUD visibility and key mapping use repository-specific profile files.");

        string profileSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "SerializationHudProfile.cs"));
        Assert(profileSource.Contains("Q(Key.F8)") &&
               profileSource.Contains("MeasureUsage") &&
               profileSource.Contains("Q(Key.Shift, Key.F8)"),
            "The configurable serialization HUD toggle defaults to F8 and exact measurement defaults to Shift+F8.");
        Assert(SerializationHudRenderer.FormatName(SerializationWireFormat.Legacy) == "LEGACY WIRE" &&
               SerializationHudRenderer.FormatBytes(1024UL) == "1 KiB",
            "Serialization HUD labels use readable invariant wire-format and byte text.");

        string rendererSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "SerializationHudRenderer.cs"));
        Assert(rendererSource.Contains("Payload total:") &&
               rendererSource.Contains("Largest stream:") &&
               rendererSource.Contains("Save buffer:") &&
               rendererSource.Contains("Vanilla OK") &&
               rendererSource.Contains("10 MB") &&
               rendererSource.Contains("GetRuntimeIcon"),
            "Serialization HUD shows split aggregate/stream buffer rows without generated fallback HUD icons.");
        Assert(SerializationHudRenderer.FormatSaveBuffer(BlueprintSerializationUsage.Empty) == "unknown" &&
               SerializationHudRenderer.FormatHeaderLimit(34UL * 1024UL) == "64 KiB legacy" &&
               SerializationHudRenderer.FormatHeaderLimit(70_000UL) == "4 MiB ESU" &&
               SerializationHudRenderer.FormatDataLimit(540UL * 1024UL) == "6.25 MiB legacy" &&
               SerializationHudRenderer.FormatDataLimit(7_000_000UL) == "64 MiB ESU",
            "Serialization HUD labels vanilla legacy limits and ESU sentinel ceilings explicitly.");
    }

    private static void VerifyDecorationEditModeMvp()
    {
        Assert(DecorationEditMath.Snap(0.076f) == 0.1f &&
               DecorationEditMath.Snap(0.024f) == 0f &&
               DecorationEditMath.IsWithinPositionLimit(new Vector3(10f, -10f, 0f)) &&
               !DecorationEditMath.IsWithinPositionLimit(new Vector3(10.01f, 0f, 0f)) &&
               !DecorationEditMath.IsWithinPositionLimit(new Vector3(float.NaN, 0f, 0f)),
            "Decoration Edit Mode movement snaps to 0.05m and enforces finite +/-10 bounds.");

        DecorationEditAxis picked = DecorationEditMath.PickAxis(
            new Vector2(50f, 2f),
            Vector2.zero,
            new Vector2(100f, 0f),
            new Vector2(0f, 100f),
            new Vector2(-100f, 0f),
            10f);
        float projected = DecorationEditMath.ProjectMouseDeltaToAxis(
            new Vector2(25f, 0f),
            Vector2.zero,
            new Vector2(100f, 0f),
            1f);
        Assert(picked == DecorationEditAxis.X && Math.Abs(projected - 0.25f) < 0.001f,
            "Decoration Edit Mode picks the nearest screen-space axis and converts drag pixels to local-axis motion.");

        string root = FindRepositoryRoot();
        string profileSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "SerializationHudProfile.cs"));
        string sessionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditSession.cs"));
        string historySource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditHistory.cs"));
        string historySourceNormalized = historySource.Replace("\r\n", "\n");
        string decorationBehaviourSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditModeBehaviour.cs"));
        string inputScopeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditorInputScope.cs"));
        string buildModeInputGateSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuBuildModeInputGate.cs"));
        string vanillaInputBridgeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuVanillaInputBridge.cs"));
        string registrationSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditModeRegistration.cs"));
        string pointerProbeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationPointerProbe.cs"));
        string transactionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditTransactionSet.cs"));
        string viewControllerSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditorViewModeController.cs"));
        string previewSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationMeshPreviewRenderer.cs"));
        string overlaySource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditorOverlay.cs"));
        string serializationHudSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "SerializationHudRenderer.cs"));
        string technicalLogSource = File.ReadAllText(Path.Combine(
            root,
            "TECHNICAL_LOG.md"));
        string inGameTestPlanSource = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "IN_GAME_TEST_PLAN.md"));
        string builderUiSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EndlessShapes2",
            "ES2_UI",
            "DBUI_BasicSettingTab.cs"));
        string sessionSourceNormalized = sessionSource.Replace("\r\n", "\n");
        string inspectorSource = ExtractMethodSource(sessionSource, "DrawInspector");
        int toolButtonSignature = sessionSource.IndexOf(
            "private void ToolButton(",
            StringComparison.Ordinal);
        string toolButtonSource = toolButtonSignature >= 0
            ? ExtractMethodSource(sessionSource.Substring(toolButtonSignature), "ToolButton").Replace("\r\n", "\n")
            : string.Empty;
        Assert(profileSource.Contains("ToggleDecorationEditMode") &&
               profileSource.Contains("Q(Key.Control, Key.D)") &&
               profileSource.Contains("SwitchEsuBuildMode") &&
               profileSource.Contains("Q(Key.Tab)") &&
               profileSource.Contains("UndoDecorationEdit") &&
               profileSource.Contains("Q(Key.Control, Key.Z)") &&
               profileSource.Contains("RedoDecorationEdit") &&
               profileSource.Contains("Q(Key.Control, Key.Y)") &&
               decorationBehaviourSource.Contains("ConsumeDecorationEditToggleDown") &&
               !decorationBehaviourSource.Contains("SerializationHudKeyInput.ToggleDecorationEditMode") &&
               registrationSource.Contains("SmartBuildModeRegistration.Active") &&
               registrationSource.Contains("CanOpenFromModeSwitch") &&
               registrationSource.Contains("ignoreSmartBuildMode") &&
               registrationSource.Contains("Close Smart Block Builder before opening Decoration Edit Mode.") &&
               buildModeInputGateSource.Contains("_decorationEditToggleRequiresRelease") &&
               buildModeInputGateSource.Contains("ReadDecorationEditToggleDown") &&
               buildModeInputGateSource.Contains("IsDecorationEditToggleDefaultHeld"),
            "Decoration Edit Mode has repository-profile keybinds, rejects direct opens over Smart Builder, and uses the shared one-press Ctrl+D input gate.");
        Assert(sessionSource.Contains("SetGameControlOptions(") &&
                !sessionSource.Contains("gameDisableKeys: true") &&
                sessionSource.Contains("GUI.ModalWindow") &&
                sessionSource.Contains("ApplyEditorViewMode") &&
                sessionSource.Contains("_previousWireframe") &&
                sessionSource.Contains("DecorationList") &&
                sessionSource.Contains("NewDecoration(") &&
                sessionSource.Contains("_selectedCreatedInSession") &&
                sessionSource.Contains("_transactions.Cancel()") &&
               inputScopeSource.Contains("ClaimBuildInputForFrames") &&
               inputScopeSource.Contains("Time.frameCount <= _buildInputClaimUntilFrame") &&
               inputScopeSource.Contains("ForceResetIfActive") &&
               sessionSource.Contains("DecorationEditorInputScope.End();") &&
               !inputScopeSource.Contains("SuppressBuildInput() => _active") &&
                inputScopeSource.Contains("HudBuildCommands") &&
                inputScopeSource.Contains("DrawInsideBuildMode") &&
                inputScopeSource.Contains("cBuild") &&
                inputScopeSource.Contains("BuildCameraMode"),
            "Decoration Edit Mode uses modal focus without disabling camera keys, scoped build-input claims, safe decoration wireframe, public decoration enumeration/creation APIs, native HUD/input suppression, and transaction rollback.");
        Assert(inputScopeSource.Contains("DrawRhs") &&
               inputScopeSource.Contains("DrawInteractionIcon") &&
               inputScopeSource.Contains("DrawWeaponInfo") &&
               serializationHudSource.Contains("DecorationEditorInputScope.Active"),
            "Decoration Edit Mode suppresses the native RHS HUD, hovered-block paint/interaction hints, weapon HUD, and ESU serialization HUD while active.");
        Assert(builderUiSource.Contains("Decoration Edit Mode") &&
               builderUiSource.Contains("DecorationEditModeRegistration.ToggleFromUi"),
            "Decoration Builder exposes a visible Decoration Edit Mode button.");

        Assert(!sessionSourceNormalized.Contains("if (_placingMesh != null ||\n                _dragAxis") &&
               sessionSource.Contains("return current.type == EventType.MouseDown &&") &&
               sessionSource.Contains("PlaceSelectedMeshAtPointer") &&
               inputScopeSource.Contains("ScrollWheelOverEditorUi") &&
               inputScopeSource.Contains("GuiDisplayBase.MouseWheelInUse.Now()") &&
               inputScopeSource.Contains("OwnsCameraInputThisFrame") &&
               inputScopeSource.Contains("ClaimCameraInputForFrames") &&
               !inputScopeSource.Contains("MouseOverEditorUi || SmartBuildInputScope.SuppressCameraInput()"),
            "Decoration Edit Mode keeps camera input alive during idle hover/mesh placement, but claims FTD mouse-wheel/camera input for ESU-owned panel scrolls.");

        var definitions = DecorationEditorIconCatalog.Definitions;
        Assert(definitions.Any(icon => icon.Key == "move" &&
                                       icon.Guid == new Guid("68419445-57e1-41ac-89c9-7683976ddcff")) &&
               definitions.Any(icon => icon.Key == "select" &&
                                       icon.Guid == new Guid("f417ee2c-2aa4-4fb2-ab9f-de4c59b94e45")) &&
               definitions.Any(icon => icon.Key == "outliner" && icon.Guid == Guid.Empty),
            "Decoration Edit Mode icon catalog records FTD runtime icon GUIDs and ESU-owned fallback concepts.");

        string themeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditorTheme.cs"));
        Assert(themeSource.Contains("PanelColor") &&
               themeSource.Contains("ToolButton") &&
               !sessionSource.Contains("DimTextureFor(_viewMode)") &&
               !sessionSource.Contains("GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height)") &&
               themeSource.Contains("DecorationEditorViewMode") &&
               themeSource.Contains("FTD") == false,
            "Decoration Edit Mode has an ESU-owned native-style theme layer without copied FTD texture data or a full-screen dark overlay.");

        Assert(sessionSource.Contains("DrawEditorShell") &&
               sessionSource.Contains("DrawTopToolbar") &&
               sessionSource.Contains("DrawAnchorMenu") &&
               sessionSource.Contains("AnchorMenuRect") &&
               sessionSource.Contains("_anchorMenuOpen") &&
               sessionSource.Contains("DrawOutliner") &&
               sessionSource.Contains("DrawInspector") &&
               sessionSource.Contains("DrawMeshPalette") &&
               sessionSource.Contains("DrawBottomPanel") &&
               sessionSource.Contains("DrawPaletteDetails(Rect rect)") &&
               sessionSource.Contains("Rect anchorRect") &&
               sessionSource.Contains("DrawAnchorContext(anchorRect.height)") &&
               sessionSource.Contains("DrawCompactAnchorHeader") &&
               sessionSource.Contains("GUILayoutUtility.GetRect(1f, 22f") &&
               sessionSource.Contains("16f, 16f") &&
               !sessionSource.Contains("new GUIContent(\" Selected anchor\", DecorationEditorIconCatalog.Get(\"anchor\"))") &&
               sessionSource.Contains("const float countHeight = 18f") &&
               sessionSource.Contains("Rect countRect") &&
               sessionSource.Contains("GUI.Label(countRect, MeshCountText(rows.Count)") &&
               sessionSource.Contains("total meshes") &&
               sessionSource.Contains("search: ") &&
               sessionSource.Contains("InspectorPanelButton") &&
               sessionSource.Contains("AnchorPanelButton") &&
               sessionSource.Contains("Rect listRect") &&
               sessionSource.Contains("Rect detailsRect") &&
               sessionSource.Contains("GUI.BeginGroup(listRect)") &&
               sessionSource.Contains("alwaysShowHorizontal: false") &&
               sessionSource.Contains("CompactText") &&
                sessionSource.Contains("480f, 1050f") &&
                sessionSource.Contains("const float topLimit = 44f") &&
                sessionSource.Contains("1170f") &&
               !themeSource.Contains("DecorationEditorPaletteTab") &&
               !sessionSource.Contains("PaletteDetailTab") &&
               !sessionSource.Contains("DecorationEditorPaletteTab.Anchor") &&
               !sessionSource.Contains("DecorationEditorBottomTab") &&
               sessionSource.Contains("PanelToggle(\"mesh\"") &&
               sessionSource.Contains("PanelToggle(\"outliner\"") &&
               !sessionSource.Contains("PanelToggle(\"settings\"") &&
               !sessionSource.Contains("PanelToggle(\"anchor\""),
            "Decoration Edit Mode renders the native shell with compact toolbar, outliner plus right-panel anchor details, taller Inspector-only mesh palette, bottom status strip, and panel toggles.");

        Assert(!sessionSource.Contains("Mesh/Add") &&
               !sessionSource.Contains("Add here") &&
               !sessionSource.Contains("Add deco here") &&
               !sessionSource.Contains("AddDecorationHere") &&
               !sessionSource.Contains("DrawSelectionControls") &&
               !sessionSource.Contains("ToolButton(DecorationEditorTool.Mesh"),
            "Decoration Edit Mode removed the dedicated Mesh/Add toolbar button and Add Here button; mesh placement starts from palette selection.");

        Assert(sessionSource.Contains("DrawViewModeMenu") &&
               sessionSource.Contains("DrawAnchorMenu(toolbarRect)") &&
               sessionSource.Contains("SelectViewMode") &&
               sessionSource.Contains("toolbarRect.xMax - width") &&
               sessionSource.Contains("private static bool s_anchorMenuOpen") &&
               sessionSource.Contains("private bool _anchorMenuOpen = s_anchorMenuOpen") &&
               sessionSource.Contains("s_anchorMenuOpen = _anchorMenuOpen") &&
               toolButtonSource.Contains("_anchorMenuOpen = !_anchorMenuOpen") &&
               toolButtonSource.Contains("else\n                {\n                    _tool = tool;\n                }") &&
               !toolButtonSource.Contains("_anchorMenuOpen = false") &&
               !sessionSourceNormalized.Contains("_viewModeMenuOpen = false;\n            ApplyEditorViewMode();") &&
               sessionSource.Contains("DecorationEditorViewMode.Mixed") &&
               sessionSource.Contains("DecorationEditorViewMode.Wireframe") &&
               sessionSource.Contains("DecorationEditorViewMode.DecorationOnly") &&
               sessionSource.Contains("DecorationEditorViewMode.Mass") &&
               sessionSource.Contains("DecorationEditorViewMode.Drag") &&
               sessionSource.Contains("DecorationEditorViewMode.Cost") &&
               sessionSource.Contains("DecorationEditorViewMode.Surface") &&
               sessionSource.Contains("DecorationEditorViewMode.Important") &&
               viewControllerSource.Contains("RunMimicView") &&
               viewControllerSource.Contains("ShowForceVisualisations") &&
               viewControllerSource.Contains("SetSpecialView") &&
               viewControllerSource.Contains("SpecialBuildView.Weight") &&
               viewControllerSource.Contains("SpecialBuildView.Mimic"),
            "Decoration Edit Mode supports ESU/native view modes for mixed, wireframe, deco-only, mass, drag, cost, surface, important, and normal editing.");

        Assert(!sessionSource.Contains("MaxSearchRows") &&
               !sessionSource.Contains("Showing first 70") &&
               sessionSource.Contains("MeshPreviewGridRowHeight = 90f") &&
               sessionSource.Contains("MeshPreviewGridColumns = 4") &&
               sessionSource.Contains("MeshPreviewGridCardWidth = 112f") &&
               sessionSource.Contains("MeshPreviewGridCardHeight = 86f") &&
               sessionSource.Contains("CompactText(entry.Name, 18)") &&
               sessionSource.Contains("DrawMeshListRows(rows, listRect.height, mouseInListViewport)") &&
               sessionSource.Contains("DrawMeshPreviewGrid(rows, listRect.height, mouseInListViewport)") &&
               sessionSource.Contains("MaxMeshGridPreviewRendersPerFrame") &&
               sessionSource.Contains("Mathf.CeilToInt((_meshScroll.y + viewportHeight) / MeshPreviewGridRowHeight)") &&
               sessionSource.Contains("EventType.Repaint") &&
               sessionSource.Contains("GetCachedPreview(entry, MeshPreviewGridTexturePixels)") &&
               previewSource.Contains("GetCachedPreview") &&
               !sessionSource.Contains("FloorToInt(_meshScroll.y / MeshPreviewGridRowHeight) - 2") &&
               !sessionSource.Contains("visibleRows = Mathf.CeilToInt(viewportHeight / MeshPreviewGridRowHeight) + 4"),
            "Decoration Edit Mode mesh palette virtualizes the full filtered mesh catalog and lazily renders only visible 4-column 3D grid thumbnails.");

        Assert(sessionSource.Contains("DrawMeshPreviewCard") &&
               previewSource.Contains("RenderTexture") &&
               previewSource.Contains("TryGetUnityMesh") &&
               previewSource.Contains("BuildGeneratedMesh") &&
               previewSource.Contains("SafeMeshBase") &&
               sessionSource.Contains("_hoveredMesh = null") &&
               sessionSource.Contains("bool mouseInListViewport = listRect.Contains(Event.current.mousePosition)") &&
               sessionSource.Contains("mouseInListViewport && row.Contains(Event.current.mousePosition)") &&
               sessionSource.Contains("mouseInListViewport && card.Contains(Event.current.mousePosition)") &&
               !sessionSource.Contains("DrawHoveredMeshPreview") &&
               !sessionSource.Contains("DrawPaletteDetails(detailsHeight)") &&
               !sessionSource.Contains("PaletteDetailTab") &&
               !sessionSource.Contains("BeginScrollView(_inspectorScroll, GUILayout.Height"),
            "Decoration Edit Mode uses a clipped foreground RenderTexture mesh preview path and keeps Inspector details out of mesh-row hover hit testing.");

        Assert(sessionSource.Contains("StartMeshPlacement") &&
               sessionSource.Contains("PlaceSelectedMeshAtPointer") &&
               sessionSource.Contains("DrawPlacementGhost") &&
               sessionSource.Contains("UpdatePlacementGhost") &&
               sessionSource.Contains("new GameObject(\"ESU Decoration Placement Ghost\")") &&
               sessionSource.Contains("ShadowCastingMode.Off") &&
               sessionSource.Contains("DestroyPlacementGhost") &&
               previewSource.Contains("internal Mesh GetMesh") &&
               !sessionSource.Contains("GetHoveredBlockPosition") &&
               pointerProbeSource.Contains("ScreenPointToRay") &&
               pointerProbeSource.Contains("Physics.Raycast") &&
               pointerProbeSource.Contains("GetBlockViaLocalPosition"),
            "Decoration Edit Mode supports KSP-style mouse-ray mesh placement with a runtime mesh ghost anchored to the pointed craft block, not the build cursor.");

        Assert(sessionSource.Contains("TryPickAnchorHandle") &&
               sessionSource.Contains("TryUpdateAnchorDrag") &&
               sessionSource.Contains("TryPreviewAnchorShift") &&
               sessionSource.Contains("ApplyAnchorShift") &&
               sessionSource.Contains("TryAutoFollowAnchor") &&
               sessionSource.Contains("TryFindAnchorFollowTarget") &&
               sessionSource.Contains("AnchorFollowMinimumDistance") &&
               sessionSource.Contains("AnchorFollowMaximumSearchRadius") &&
               sessionSource.Contains("Follow: on") &&
               sessionSource.Contains("Anchor follow range") &&
               sessionSource.Contains("DrawAnchorGizmo") &&
               sessionSource.Contains("DrawBlockWireframe") &&
               sessionSource.Contains("oldCenter - ToVector3(newTether)") &&
               sessionSource.Contains("GetBlockViaLocalPosition"),
            "Decoration Edit Mode has a viewport anchor gizmo and optional anchor-follow retethering that snap to real tether blocks while preserving visual position.");

        Assert(sessionSource.Contains("ToolButton(DecorationEditorTool.Move") &&
               sessionSource.IndexOf("ToolButton(DecorationEditorTool.Move", StringComparison.Ordinal) <
               sessionSource.IndexOf("ToolButton(DecorationEditorTool.Rotate", StringComparison.Ordinal) &&
               sessionSource.IndexOf("ToolButton(DecorationEditorTool.Rotate", StringComparison.Ordinal) <
               sessionSource.IndexOf("ToolButton(DecorationEditorTool.Scale", StringComparison.Ordinal) &&
               sessionSource.Contains("OrientationToggleButton") &&
               sessionSource.Contains("DecorationTransformOrientation") &&
               sessionSource.Contains("s_transformOrientation") &&
               sessionSource.Contains("ActiveTransformAxisVector") &&
               sessionSource.Contains("_rotateStartQuaternion * Quaternion.AngleAxis") &&
               sessionSource.Contains("TryUpdateRotate") &&
               sessionSource.Contains("CommitRotateEdit") &&
               sessionSource.Contains("DrawRotateGizmo") &&
               sessionSource.Contains("TryUpdateScale") &&
               sessionSource.Contains("CommitScaleEdit") &&
               sessionSource.Contains("DrawScaleGizmo"),
            "Decoration Edit Mode implements move, rotate, and scale toolbar tools in Blender-style transform order.");

        Assert(sessionSource.Contains("DecorationEditAxis.Free") &&
               sessionSource.Contains("TryPickMoveHandle") &&
               sessionSource.Contains("PrepareFreeDragFrame") &&
               sessionSource.Contains("_freeDragCameraRight") &&
               sessionSource.Contains("_freeDragMetresPerPixel") &&
               sessionSource.Contains("TryPickRotateRing") &&
               sessionSource.Contains("DistanceToSegment(mouse, previous, projected)") &&
               sessionSource.Contains("Vector2.SignedAngle"),
            "Decoration Edit Mode keeps XYZ move vectors while adding center freeform movement and projected-ring rotate picking.");

        Assert(sessionSource.Contains("_showTetherPins") &&
               sessionSource.Contains("_collapsedConstructs") &&
               sessionSource.Contains("ToggleConstructCollapse") &&
               sessionSource.Contains("ForDecoration(") &&
               sessionSource.Contains("depth)") &&
               sessionSource.Contains("DrawAnchorContext") &&
               sessionSource.Contains("DrawAnchorContext(anchorRect.height)") &&
               sessionSource.Contains("SelectedAnchorDecorations") &&
               sessionSource.Contains("DrawSelectedAnchorConnections") &&
               sessionSource.Contains("_tool != DecorationEditorTool.Anchor"),
            "Decoration Edit Mode outliner defaults to construct-grouped decoration rows with optional tether pins, collapsible constructs, right-panel selected-anchor context, anchor connection lines, and anchor-mode click selection.");

        Assert(historySource.Contains("DecorationEditHistory") &&
               historySource.Contains("DecorationSnapshotCommand") &&
               historySource.Contains("DecorationCreateCommand") &&
               transactionSource.Contains("DecorationEditTransactionSet") &&
               transactionSource.Contains("MarkCreated") &&
               transactionSource.Contains("TrackEdit") &&
               sessionSource.Contains("UndoEdit") &&
               sessionSource.Contains("RedoEdit") &&
               sessionSource.Contains("Ctrl+Shift+Z") &&
               historySource.Contains("IDecorationEditCommand command = _undo.Peek();") &&
               historySource.Contains("IDecorationEditCommand command = _redo.Peek();") &&
               historySourceNormalized.Contains("if (!command.Undo(session))\n                return false;\n\n            _undo.Pop();") &&
               historySourceNormalized.Contains("if (!command.Redo(session))\n                return false;\n\n            _redo.Pop();"),
            "Decoration Edit Mode has session-scoped undo/redo history, preserves failed history entries, and transaction rollback for snapshots and created decorations.");

        Assert(sessionSource.Contains("HashSet<Decoration> _selection") &&
                sessionSource.Contains("MaxOutlinerDrawRows") &&
                sessionSource.Contains("DecorationOutlinerRowKind") &&
                sessionSource.Contains("GroupBy(decoration => FormatTether") &&
                !sessionSource.Contains("before changing selection"),
            "Decoration Edit Mode stores selection in a future-multi-select shape, builds a virtualized outliner, and allows selection changes while dirty.");

        Assert(sessionSource.Contains("SerializationForecastCalculator.Calculate") &&
               sessionSource.Contains("FlexibleFloatParser.TryParse") &&
               sessionSource.Contains("Mathf.Clamp(color, 0, 31)") &&
               sessionSource.Contains("ApplyScaleFromInspector") &&
               sessionSource.Contains("BuildMaterialCatalog") &&
               sessionSource.Contains("SetSelectedMaterial") &&
               sessionSource.Contains("MaterialOverrideButtonLabel") &&
               sessionSource.Contains("MaterialCountText(materials.Count)") &&
               sessionSource.Contains("total materials") &&
               sessionSource.Contains("\"Material override: \" + CompactText(MaterialDisplayName(materialOverride), 46)") &&
               sessionSource.Contains("hasMaterialOverride") &&
               sessionSourceNormalized.Contains("GUILayout.Label(\n                    MaterialOverrideButtonLabel(materialOverride)") &&
               sessionSourceNormalized.Contains("GUILayout.Button(\n                    \"Clear\"") &&
               sessionSource.Contains("DrawBottomTransformEditors") &&
               sessionSource.Contains("DrawBottomVectorEditor") &&
               sessionSource.Contains("DrawBottomVectorComponent") &&
               sessionSource.Contains("Mode: Deco | Tab to Build when clean") &&
               sessionSource.Contains("GUILayout.Label(\"Mode: Deco | Tab to Build when clean\", DecorationEditorTheme.Body)") &&
               sessionSource.Contains("const float width = 386f") &&
               sessionSource.Contains("const float fieldWidth = 72f") &&
               sessionSource.Contains("float x = row.xMax - totalWidth") &&
               sessionSource.Contains("RefreshLiveTransformFields") &&
               sessionSourceNormalized.Contains("HandleSceneInput();\n            RefreshLiveTransformFields();") &&
               sessionSource.Contains("Mathf.Clamp(Screen.height * 0.105f, 88f, 112f)") &&
               !inspectorSource.Contains("DrawVectorEditor(\"Position\"") &&
               !inspectorSource.Contains("DrawVectorEditor(\"Rotation\"") &&
               !inspectorSource.Contains("DrawVectorEditor(\"Scale\"") &&
               sessionSource.Contains("Paint color") &&
               sessionSource.IndexOf("DrawColorEditor()", StringComparison.Ordinal) <
               sessionSource.IndexOf("LabelRow(\"Owner\"", StringComparison.Ordinal) &&
               sessionSource.IndexOf("DrawMaterialEditor()", StringComparison.Ordinal) <
               sessionSource.IndexOf("LabelRow(\"Owner\"", StringComparison.Ordinal) &&
               sessionSourceNormalized.IndexOf("\"Position\",\n                _positionText", StringComparison.Ordinal) <
               sessionSourceNormalized.IndexOf("\"Rotation\",\n                _orientationText", StringComparison.Ordinal) &&
               sessionSourceNormalized.IndexOf("\"Rotation\",\n                _orientationText", StringComparison.Ordinal) <
               sessionSourceNormalized.IndexOf("\"Scale\",\n                _scaleText", StringComparison.Ordinal),
            "Decoration Edit Mode inspector uses color/material controls, while the bottom panel hosts live Position/Rotation/Scale transform editing.");

        Assert(!sessionSource.Contains("color = Color.white;") &&
               !sessionSource.Contains("VectorLines.i.Current") &&
               overlaySource.Contains("Hidden/Internal-Colored") &&
               overlaySource.Contains("CompareFunction.Always") &&
               overlaySource.Contains("\"_ZWrite\", 0") &&
               overlaySource.Contains("GL.QUADS") &&
               overlaySource.Contains("ScreenToWorldPoint") &&
               sessionSource.Contains("float width = _dragAxis == axis ? 4.5f : 3f") &&
               sessionSource.Contains("float width = _anchorDragAxis == axis && _anchorDragSign == sign ? 4f : 2.5f"),
            "Decoration Edit Mode transform and anchor axes use ESU unlit overlay drawing with stable RGB hues across native view modes.");

        Assert(inputScopeSource.Contains("TipDisplayer") &&
               inputScopeSource.Contains("SetTip") &&
               inputScopeSource.Contains("!DecorationEditorInputScope.Active &&") &&
               inputScopeSource.Contains("!SmartBuildInputScope.Active") &&
               inputScopeSource.Contains("InfoStore") &&
               inputScopeSource.Contains("Colored with paint"),
            "ESU modal build tools suppress hovered-block paint/tool tips while active.");
        Assert(technicalLogSource.Contains("EsuBuildModeInputGate") &&
               technicalLogSource.Contains("Ctrl+Shift+B") &&
               inGameTestPlanSource.Contains("Ctrl+Shift+B") &&
               inGameTestPlanSource.Contains("remains open after the first frame"),
            "Smart Builder/build-mode input-gate fix is documented and covered by in-game smoke tests.");

        string inputStateSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuInputState.cs"));
        Assert(!registrationSource.Contains("ChatGUI.Instance") &&
               !inputStateSource.Contains("ChatGUI.Instance") &&
               inputStateSource.Contains("AccessTools.Field(typeof(ChatGUI), \"_instance\")"),
            "ESU hotkey guards inspect an existing ChatGUI instance without constructing ChatGUI/FtdKeyMap during boot.");

        Assert(serializationHudSource.Contains("DecorationEditorIconCatalog.GetRuntimeIcon(icon)") &&
               serializationHudSource.Contains("\"count\"") &&
               serializationHudSource.Contains("\"mesh\"") &&
               serializationHudSource.Contains("\"risk\""),
            "Serialization HUD rows use runtime icons outside Decoration Edit Mode without generated fallback tiles.");

        string nativeUiDoc = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "DECORATION_EDITOR_NATIVE_UI.md"));
        Assert(nativeUiDoc.Contains("StreamingAssets/Mods/UI/Ui Elements") &&
               nativeUiDoc.Contains("editButton") &&
               nativeUiDoc.Contains("ESU-owned fallback icons") &&
               nativeUiDoc.Contains("does not package copied FTD texture files"),
            "Decoration Edit Mode native UI and FTD icon catalog are documented.");

        string[] copiedTextureExtensions = { ".png", ".jpg", ".jpeg", ".texture", ".uielements" };
        bool copiedIconAsset = Directory
            .EnumerateFiles(Path.Combine(root, "EndlessShapesUnlimited"), "*", SearchOption.AllDirectories)
            .Any(path => copiedTextureExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
        Assert(!copiedIconAsset,
            "Decoration Edit Mode packages no copied FTD icon texture or UI-element assets.");
    }

    private static void VerifySmartBlockBuilder()
    {
        var line = new SmartBuildVolume(
            construct: null,
            origin: new Vector3i(0, 0, 0),
            axisU: SmartBuildAxis.X,
            axisV: SmartBuildAxis.Y,
            axisN: SmartBuildAxis.Z,
            lengthU: 10,
            lengthV: 1,
            thickness: 1);
        SmartBuildPlan beamLine = SmartBuildPlanner.BuildPlan(
            line,
            SmartBlockFamily.ForTests(1, 2, 3, 4),
            _ => false);
        Assert(beamLine.CanCommit &&
               beamLine.Placements.Select(placement => placement.Length)
                   .SequenceEqual(new[] { 4, 4, 2 }),
            "Smart Block Builder packs a 10-cell beam line as 4+4+2 when legal variants exist.");

        var sheet = new SmartBuildVolume(
            construct: null,
            origin: new Vector3i(0, 0, 0),
            axisU: SmartBuildAxis.X,
            axisV: SmartBuildAxis.Y,
            axisN: SmartBuildAxis.Z,
            lengthU: 10,
            lengthV: 5,
            thickness: 1);
        SmartBuildPlan sheetPlan = SmartBuildPlanner.BuildPlan(
            sheet,
            SmartBlockFamily.ForTests(1, 2, 3, 4),
            _ => false);
        Vector3i[] covered = sheetPlan.Placements
            .SelectMany(placement => placement.CoveredCells())
            .ToArray();
        Assert(sheetPlan.CanCommit &&
               sheetPlan.CoveredCellCount == 50 &&
               covered.Distinct().Count() == 50,
            "Smart Block Builder covers a 10x5 sheet exactly once per target cell.");

        SmartBuildPlan singles = SmartBuildPlanner.BuildPlan(
            line,
            SmartBlockFamily.ForTests(1),
            _ => false);
        Assert(singles.CanCommit && singles.Placements.Count == 10 &&
               singles.Placements.All(placement => placement.Length == 1),
            "Smart Block Builder falls back to repeated 1x1x1 placements when no variants exist.");

        SmartBuildPlan unsupported = SmartBuildPlanner.BuildPlan(
            line,
            SmartBlockFamily.Unsupported("unsupported test item", "unsupported multi-cell seed"),
            _ => false);
        Assert(!unsupported.CanCommit &&
               unsupported.FailureReason.Contains("unsupported multi-cell seed"),
            "Smart Block Builder refuses unsupported multi-cell selected items with a clear reason.");

        Vector3i occupiedCell = new Vector3i(2, 0, 0);
        SmartBuildPlan blocked = SmartBuildPlanner.BuildPlan(
            line,
            SmartBlockFamily.ForTests(1),
            cell => cell.Equals(occupiedCell));
        SmartBuildPlan skipped = SmartBuildPlanner.BuildPlan(
            line,
            SmartBlockFamily.ForTests(1),
            cell => cell.Equals(occupiedCell),
            new SmartBuildPlannerOptions
            {
                SkipOccupiedCells = true
            });
        Assert(!blocked.CanCommit &&
               skipped.CanCommit &&
               skipped.SkippedCells.Count == 1 &&
               skipped.CoveredCellCount == 9,
            "Smart Block Builder can either fail on occupied cells or skip them according to planner options.");

        SmartBuildDraft draft = SmartBuildDraft.CreateSeed(
            construct: null,
            origin: new Vector3i(3, 4, 5),
            drawPlane: SmartBuildDrawPlane.Camera);
        SmartBuildVolume seedVolume = draft.ToVolume();
        Assert(seedVolume.Origin.Equals(new Vector3i(3, 4, 5)) &&
               seedVolume.CellCount == 1 &&
               seedVolume.AxisU == SmartBuildAxis.X &&
               seedVolume.AxisV == SmartBuildAxis.Y &&
               seedVolume.AxisN == SmartBuildAxis.Z,
            "Smart Block Builder can create a 1x1x1 runtime draft without an existing block-face drag.");

        draft.MoveBy(new Vector3i(2, -1, 0));
        draft.ResizeFromHandle(DecorationEditAxis.X, sign: 1, delta: 4);
        draft.ResizeFromHandle(DecorationEditAxis.Y, sign: -1, delta: -2);
        SmartBuildVolume editedVolume = draft.ToVolume();
        Assert(editedVolume.Origin.Equals(new Vector3i(5, 1, 5)) &&
               editedVolume.LengthU == 5 &&
               editedVolume.LengthV == 3 &&
               editedVolume.Thickness == 1,
            "Smart Block Builder runtime drafts move and scale by whole focused-grid cells before commit.");

        SmartBuildPlan overCap = SmartBuildPlanner.BuildPlan(
            line,
            SmartBlockFamily.ForTests(1),
            _ => false,
            new SmartBuildPlannerOptions
            {
                HardPlacementCap = 9
            });
        Assert(!overCap.CanCommit &&
               overCap.FailureReason.Contains("hard cap"),
            "Smart Block Builder blocks commits above the placement hard cap.");

        ConstructorInfo placeBlockConstructor = AccessTools.Constructor(
            typeof(PlaceBlockCommand),
            new[]
            {
                typeof(AllConstruct),
                typeof(Vector3i),
                typeof(Quaternion),
                typeof(ItemDefinition),
                typeof(int),
                typeof(MirrorInfo)
            });
        PropertyInfo buildingWith = typeof(cBuild).GetProperty("BuildingWith");
        PropertyInfo selectedItem = buildingWith?.PropertyType.GetProperty("Item");
        Assert(placeBlockConstructor != null &&
               buildingWith != null &&
               selectedItem?.PropertyType == typeof(ItemDefinition),
            "Smart Block Builder integration targets resolve: selected build item and PlaceBlockCommand constructor.");

        string root = FindRepositoryRoot();
        string profileSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "SerializationHudProfile.cs"));
        string pluginSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "Plugin.cs"));
        string registrationSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildModeRegistration.cs"));
        string behaviourSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildModeBehaviour.cs"));
        string decorationBehaviourSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditModeBehaviour.cs"));
        string buildModeInputGateSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuBuildModeInputGate.cs"));
        string sessionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildSession.cs"));
        string draftSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildDraft.cs"));
        string smartInputScopeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildInputScope.cs"));
        string overlaySource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditorOverlay.cs"));
        string selectionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildSelectionResolver.cs"));
        string catalogSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBlockFamilyCatalog.cs"));
        string committerSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildCommitter.cs"));
        string inputScopeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditorInputScope.cs"));
        string vanillaInputBridgeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuVanillaInputBridge.cs"));
        string technicalLogSource = File.ReadAllText(Path.Combine(
            root,
            "TECHNICAL_LOG.md"));
        string inGameTestPlanSource = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "IN_GAME_TEST_PLAN.md"));
        Assert(profileSource.Contains("ToggleSmartBuildMode") &&
               profileSource.Contains("Q(Key.Control, Key.Shift, Key.B)") &&
               profileSource.Contains("SwitchEsuBuildMode") &&
               profileSource.Contains("Q(Key.Tab)") &&
               pluginSource.Contains("SmartBuildModeRegistration.Register") &&
               registrationSource.Contains("DecorationEditModeRegistration.Active") &&
               registrationSource.Contains("CanOpenFromModeSwitch") &&
               registrationSource.Contains("ignoreDecorationEditMode") &&
               registrationSource.Contains("TrySwitchToDecorationEdit") &&
               behaviourSource.Contains("ReadSwitchModeKeyDown") &&
               behaviourSource.Contains("CanOpenFromModeSwitch") &&
               behaviourSource.Contains("DecorationEditModeRegistration.CanOpenFromModeSwitch") &&
               behaviourSource.Contains("ConsumeSmartBuildToggleDown") &&
               behaviourSource.Contains("ConsumeSwitchModeDown") &&
               decorationBehaviourSource.Contains("ConsumeDecorationEditToggleDown") &&
               decorationBehaviourSource.Contains("ConsumeSwitchModeDown") &&
               buildModeInputGateSource.Contains("_switchModeRequiresRelease") &&
               buildModeInputGateSource.Contains("_decorationEditToggleRequiresRelease") &&
               buildModeInputGateSource.Contains("_smartBuildToggleRequiresRelease") &&
               buildModeInputGateSource.Contains("Time.frameCount") &&
               buildModeInputGateSource.Contains("IsDecorationEditToggleDefaultHeld") &&
               buildModeInputGateSource.Contains("IsSmartBuildToggleDefaultHeld"),
            "Smart Block Builder registers at startup, defaults to Ctrl+Shift+B, rejects direct opens over Decoration Edit Mode, and shares one-press input gates for Tab/Ctrl+D/Ctrl+Shift+B handoffs.");
        Assert(selectionSource.Contains("build.BuildingWith?.Item") &&
               catalogSource.Contains("3cc75979-18ac-46c4-9a5b-25b327d99410") &&
               catalogSource.Contains("ModificationComponentContainerItem") &&
               committerSource.Contains("PlaceBlockCommand") &&
               committerSource.Contains("MirrorInfo.none"),
            "Smart Block Builder resolves the selected FTD block, knows Beamification armor families, and commits vanilla block commands.");
        Assert(inputScopeSource.Contains("SmartBuildInputScope.SuppressBuildHud") &&
               inputScopeSource.Contains("!SmartBuildInputScope.Active") &&
               vanillaInputBridgeSource.Contains("cBuild.ToggleFreeze") &&
               vanillaInputBridgeSource.Contains("KeyInputsFtd.Freeze") &&
               pluginSource.Contains("ResolveBuildFreezeTarget"),
            "Smart Block Builder shares the modal build input guard and ESU has a one-frame vanilla freeze fallback for Caps Lock while ESU owns the HUD.");
        Assert(draftSource.Contains("SmartBuildDraft") &&
               draftSource.Contains("SmartBuildDrawPlane") &&
               draftSource.Contains("SmartBuildOccupancyMode") &&
               sessionSource.Contains("TrySeedFromDrawPlane") &&
               sessionSource.Contains("TrySeedFromPointedBlock") &&
               sessionSource.Contains("CreatePreviewAtSeed") &&
               sessionSource.Contains("CanPlaceDraftOrigin") &&
               sessionSource.Contains("_tool = SmartBuildTool.Scale") &&
               sessionSource.Contains("Smart Builder origin is occupied. Pick an empty cell.") &&
               sessionSource.Contains("PlanTouchesExistingConstruct") &&
               sessionSource.Contains("The Smart Builder preview must touch an existing block before Apply.") &&
               committerSource.Contains("TryOrderPlacementsForCommit") &&
               committerSource.Contains("PlacementTouchesConstructOrPlacedCells") &&
               committerSource.Contains("remaining planned blocks are disconnected") &&
               sessionSource.Contains("SmartBuildOccupancyMode.SkipOccupied") &&
               sessionSource.Contains("Input.GetMouseButtonDown(2)") &&
               !behaviourSource.Contains("AllGameControlsEnabled") &&
               sessionSource.Contains("DecorationEditorTheme") &&
               sessionSource.Contains("DecorationEditorOverlay.Quad") &&
               overlaySource.Contains("OverlayQuad"),
            "Smart Block Builder uses a runtime draft, rejects occupied draw origins, switches Draw to Scale, commits connected placements first, tolerates middle-mouse cursor input, and draws native-styled translucent voxel previews.");
        Assert(smartInputScopeSource.Contains("ClaimCameraInputForFrames") &&
               smartInputScopeSource.Contains("ClaimMouseWheelInputForFrames") &&
               smartInputScopeSource.Contains("GuiDisplayBase.MouseWheelInUse.Now()") &&
               smartInputScopeSource.Contains("SuppressCameraInput() =>") &&
               !smartInputScopeSource.Contains("MouseOverUi ||") &&
               !sessionSource.Contains("if (_draft != null)\r\n                SmartBuildInputScope.ClaimBuildInputForFrames") &&
               sessionSource.Contains("ShouldConsumeGuiEvent(Event.current)") &&
               sessionSource.Contains("SwitchToDecorationEditRequested") &&
               sessionSource.Contains("CanSwitchToDecorationEdit"),
            "Smart Block Builder leaves camera/WASD live while idle and only suppresses camera input for handle drags or panel scrolls.");
        Assert(technicalLogSource.Contains("SmartBuildDraft") &&
               technicalLogSource.Contains("Middle mouse may") &&
               technicalLogSource.Contains("show the FTD cursor without closing Smart Builder") &&
               inGameTestPlanSource.Contains("click empty space") &&
               inGameTestPlanSource.Contains("middle mouse shows the FTD cursor without closing"),
            "Smart Block Builder editable-preview workflow is documented for implementation notes and in-game smoke testing.");
        Assert(!registrationSource.Contains("ChatGUI.Instance"),
            "Smart Block Builder hotkey/open guards do not construct ChatGUI during boot.");
    }

    private static void VerifyBeamificationBundle()
    {
        string root = FindRepositoryRoot();
        string beamification = Path.Combine(root, "tools", "Beamification");
        string licensePath = Path.Combine(root, "LICENSES", "FtD_Beamification-MIT.txt");
        string mainPath = Path.Combine(beamification, "__main__.py");
        string technicalDoc = Path.Combine(root, "docs", "BEAMIFICATION_TECHNICAL.md");

        Assert(File.Exists(mainPath) &&
               File.Exists(Path.Combine(beamification, "requirements.txt")) &&
               File.Exists(Path.Combine(beamification, "src", "beamification.py")) &&
               File.Exists(Path.Combine(beamification, "src", "blueprint.py")) &&
               File.Exists(Path.Combine(beamification, "src", "s_field.py")) &&
               File.Exists(Path.Combine(beamification, "src", "make_result.py")),
            "FtD Beamification source files are bundled as optional tools.");

        string license = File.ReadAllText(licensePath);
        Assert(license.Contains("MIT License") &&
               license.Contains("Copyright (c) 2025 Delta Epsilon"),
            "Delta Epsilon's FtD Beamification MIT notice is retained.");

        string main = File.ReadAllText(mainPath);
        string readme = File.ReadAllText(Path.Combine(beamification, "README.md"));
        string build = File.ReadAllText(Path.Combine(root, "build.ps1"));
        Assert(main.Contains("debeamify = args.procedure == \"debeamify\"") &&
               readme.Contains("a0aaa63010c460563909cc8eb73f2c0aac2bf5ea") &&
               readme.Contains("DeltaEpsilon / Delta Epsilon / DeltaEpsilon7787"),
            "The bundled Beamification copy documents provenance and keeps ESU's CLI debeamify fix.");
        Assert(build.Contains("tools\\Beamification") &&
               build.Contains("'Tools'"),
            "Release packaging includes the Beamification tool folder.");

        string rootNotice = File.ReadAllText(Path.Combine(root, "THIRD_PARTY_NOTICES.md"));
        string packageNotice = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "THIRD_PARTY_NOTICES.md"));
        string doc = File.ReadAllText(technicalDoc);
        Assert(rootNotice.Contains("DeltaEpsilon / Delta Epsilon / DeltaEpsilon7787") &&
               packageNotice.Contains("DeltaEpsilon / Delta Epsilon / DeltaEpsilon7787") &&
               rootNotice.Contains("Wengh / Weng Haoyu") &&
               packageNotice.Contains("Wengh / Weng Haoyu") &&
               rootNotice.Contains("BuildingTools source files") &&
               packageNotice.Contains("BuildingTools source files") &&
               doc.Contains("mixed-integer") &&
               doc.Contains("Tools/Beamification"),
            "Beamification credits, BuildingTools reference attribution, and technical documentation are present.");
    }

    private static string GetSingleStringLiteral(MethodInfo method)
    {
        byte[] body = method?.GetMethodBody()?.GetILAsByteArray();
        if (body == null || body.Length < 5 || body[0] != 0x72)
            return null;
        int token = BitConverter.ToInt32(body, 1);
        return method.Module.ResolveString(token);
    }

    private static void VerifyPackageIdentityAndAssets()
    {
        string root = FindRepositoryRoot();
        string package = Path.Combine(root, "EndlessShapesUnlimited");
        string manifest = File.ReadAllText(Path.Combine(package, "plugin.json"));
        Assert(manifest.Contains("\"name\": \"EndlessShapes Unlimited\"") &&
               manifest.Contains("\"version\": \"1.0.0\"") &&
               manifest.Contains("EndlessShapesUnlimited.dll") &&
               manifest.Contains("\"DecoLimitLifter\"") &&
               manifest.Contains("\"EndlessShapes2\""),
            "Package manifest has the combined identity and standalone-mod conflicts.");

        string item = File.ReadAllText(Path.Combine(
            package,
            "Items",
            "DecorationBuilder_232087b.item"));
        string characterItem = File.ReadAllText(Path.Combine(
            package,
            "Character Items",
            "Deco_19cf3dd.charitem"));
        Assert(item.Contains("\"ClassName\": \"DecorationBuilder\"") &&
               item.Contains("232087b9-90de-4eb8-9680-ccb876edfe4f"),
            "Decoration Builder class binding and asset GUID are preserved.");
        Assert(characterItem.Contains("\"ClassName\": \"DecorationTetherMove\"") &&
               characterItem.Contains("19cf3dd2-86fc-4b97-9e69-23e34cc3f198"),
            "Tether tool class binding and asset GUID are preserved.");
        Assert(File.Exists(Path.Combine(package, "Assets", "StereoscopicHologram.obj")) &&
               File.Exists(Path.Combine(package, "Meshes", "StereoscopicHologram_057bd9b.mesh")),
            "EndlessShapes runtime mesh assets are packaged.");

        string sourceDirectory = Path.Combine(package, "Source", "EndlessShapes2");
        string builderSource = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "DecorationBuilder.cs"));
        string dataSource = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "DecorationBuilderData.cs"));
        string tetherSource = File.ReadAllText(Path.Combine(
            sourceDirectory,
            "DecorationTetherMove.cs"));
        Assert(builderSource.Contains("namespace EndlessShapes2") &&
               builderSource.Contains("public class DecorationBuilder") &&
               dataSource.Contains("public class DecorationBuilderData") &&
               tetherSource.Contains("public class DecorationTetherMove"),
            "Legacy EndlessShapes2 public type declarations remain available.");
    }

    private static int[][] Face(params int[] indexes) =>
        indexes.Select(index => new[] { index, -1 }).ToArray();

    private static List<Vector3> EllipseVertices()
    {
        float[] values = { 0f, 0.382683f, 0.707107f, 0.923880f, 1f };
        return new List<Vector3>
        {
            new Vector3(0f, values[0], values[4]),
            new Vector3(0f, values[1], values[3]),
            new Vector3(0f, values[2], values[2]),
            new Vector3(0f, values[3], values[1]),
            new Vector3(0f, values[4], -values[0]),
            new Vector3(0f, values[3], -values[1]),
            new Vector3(0f, values[2], -values[2]),
            new Vector3(0f, values[1], -values[3]),
            new Vector3(0f, -values[0], -values[4]),
            new Vector3(0f, -values[1], -values[3]),
            new Vector3(0f, -values[2], -values[2]),
            new Vector3(0f, -values[3], -values[1]),
            new Vector3(0f, -values[4], values[0]),
            new Vector3(0f, -values[3], values[1]),
            new Vector3(0f, -values[2], values[2]),
            new Vector3(0f, -values[1], values[3])
        };
    }

    private static void ExpectGeometryError(
        List<Vector3> vertices,
        int[][] face,
        int sourceLine,
        string messageFragment,
        string description)
    {
        try
        {
            PolygonDataControl.PolygonClassify(
                new List<PolygonData>(),
                new List<int[][]> { face },
                new List<int[]>(),
                vertices,
                null,
                new[] { sourceLine },
                Array.Empty<int>(),
                100);
        }
        catch (InvalidDataException exception)
        {
            Assert(exception.Message.Contains("OBJ line " + sourceLine) &&
                   exception.Message.IndexOf(messageFragment, StringComparison.OrdinalIgnoreCase) >= 0,
                description);
            return;
        }

        throw new InvalidOperationException("Expected geometry rejection: " + description);
    }

    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[24];
        byte[] signature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        Array.Copy(signature, bytes, signature.Length);
        bytes[11] = 13;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static byte[] JpegHeader(int width, int height)
    {
        return new byte[]
        {
            0xff, 0xd8,
            0xff, 0xc0,
            0x00, 0x07,
            0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width
        };
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "EndlessShapesUnlimited", "plugin.json")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static SuperSaver NewSaver(uint headerCount, uint dataBytes)
    {
        var saver = new SuperSaver();
        SetHeaderCount(saver, headerCount);
        saver._datasWrittenSorted = dataBytes;

        int headerBytes = checked((int)(headerCount * 7U));
        if (saver.Header == null || saver.Header.Length < headerBytes)
            saver.Header = new byte[headerBytes];
        if (saver.DataSorted == null || saver.DataSorted.Length < dataBytes)
            saver.DataSorted = new byte[checked((int)dataBytes)];
        return saver;
    }

    private static byte[] SerialiseFixture(
        uint headerCount,
        uint dataBytes,
        ulong objectId,
        byte objectIdBytes)
    {
        SuperSaver saver = NewSaver(headerCount, dataBytes);
        if (headerCount > 0U)
            ByteConversion.ConvertIn(saver.Header, 0U, 3, 123U);
        int bytesToWrite = checked((int)dataBytes);
        for (int i = 0; i < bytesToWrite; i++)
            saver.DataSorted[i] = (byte)(i + 1);

        SuperSerialisationLayout layout =
            SuperSerialisationLayout.Create(headerCount, dataBytes, objectIdBytes);
        var bytes = new byte[layout.TotalBytes];
        uint cursor = 0U;
        saver.Serialise(bytes, ref cursor, objectId, objectIdBytes);
        if (cursor != (uint)bytes.Length)
            throw new InvalidOperationException("Fixture serializer did not fill the expected byte count.");
        return bytes;
    }

    private static Blueprint NewBlueprintForUsageTest() =>
        (Blueprint)FormatterServices.GetUninitializedObject(typeof(Blueprint));

    private static void SetHeaderCount(SuperSaver saver, uint value) =>
        _headerCountProperty.SetValue(saver, value, null);

    private static void Expect<TException>(Action action, string description)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            Pass(description);
            return;
        }

        throw new InvalidOperationException("Expected " + typeof(TException).Name + ": " + description);
    }

    private static string ExtractMethodSource(string source, string methodName)
    {
        string needle = " " + methodName + "(";
        int signature = -1;
        int search = 0;
        while (search < source.Length)
        {
            int candidate = source.IndexOf(needle, search, StringComparison.Ordinal);
            if (candidate < 0)
                break;

            int lineStart = source.LastIndexOf('\n', Math.Max(0, candidate - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            string prefix = source.Substring(lineStart, candidate - lineStart);
            if (prefix.Contains("private ") ||
                prefix.Contains("internal ") ||
                prefix.Contains("public ") ||
                prefix.Contains("protected "))
            {
                signature = candidate;
                break;
            }

            search = candidate + needle.Length;
        }

        if (signature < 0)
            return string.Empty;

        int open = source.IndexOf('{', signature);
        if (open < 0)
            return string.Empty;

        int depth = 0;
        for (int index = open; index < source.Length; index++)
        {
            char current = source[index];
            if (current == '{')
                depth++;
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                    return source.Substring(signature, index - signature + 1);
            }
        }

        return source.Substring(signature);
    }

    private static void Assert(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("Verification failed: " + description);
        Pass(description);
    }

    private static void Pass(string description)
    {
        _passed++;
        Console.WriteLine("PASS: " + description);
    }
}
