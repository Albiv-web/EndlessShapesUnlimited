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
using Assets.Scripts.Persistence;
using BrilliantSkies.Core.FilesAndFolders;
using BrilliantSkies.Core.JsonPlus.Converters;
using BrilliantSkies.Core.Serialisation.Bytes;
using BrilliantSkies.Core.Types;
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
using Newtonsoft.Json;
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
            VerifyWorkshopUpdateNotifier();
            VerifySerializationHudTargets();
            VerifySerializationTelemetryAggregation();
            VerifyBlueprintSerializationUsageAnalyzer();
            VerifyBlueprintSerializationUsageSampleFiles();
            VerifySerializationForecasting();
            VerifySerializationTelemetryScopes();
            VerifySerializationHudProfiles();
            VerifyVanillaCompatibilityMode();
            VerifyBlueprintJsonStreamingSave();
            VerifyFastBlueprintLoading();
            VerifyDecorationEditModeMvp();
            VerifyEsuRuntimeConsole();
            VerifyPointerFlushPlacement();
            VerifySurfaceDecorationBuilder();
            VerifySmartBlockBuilder();
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
        Assert(Plugin.ResolveBlueprintFileJsonSaveTarget() != null &&
               AccessTools.Method(
                   typeof(BlueprintFile_Save_BlueprintJsonStreaming_Patch),
                   "Prefix") != null &&
               Plugin.ResolveBlueprintFileManagerFactoryTarget() != null &&
               AccessTools.Method(
                   typeof(FileManagerMaker_CreateBlueprintFileModelSaver_BlueprintJsonStreaming_Patch),
                   "Postfix") != null,
            "Blueprint JSON streaming patch targets and patch methods resolve in the verifier harness.");
        Assert(Plugin.ResolveFastBlueprintFileModelLoadTarget() != null &&
               AccessTools.Method(
                   typeof(BlueprintFile_Load_FastLoad_Patch),
                   "Prefix") != null &&
               AccessTools.Method(
                   typeof(ConstructExtraInfo_DataArray_FastLoad_Patch),
                   "Prefix") != null,
            "Fast blueprint load patch methods resolve in the verifier harness.");
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
        string quotedRelativePath = Path.Combine("Models With Spaces", "ship.obj");
        string normalized = FilePathInput.Normalize(
            $"  \"{quotedRelativePath}\"  ");
        Assert(normalized == quotedRelativePath,
            "Pasted file paths preserve spaces while trimming whitespace and matching quotes.");

        string privateProfilePath = Path.Combine(
            "Example Profile",
            "Downloads",
            "ship.obj");
        string missing = FilePathInput.MissingFileMessage(
            "OBJ",
            privateProfilePath);
        Assert(missing.Contains("ship.obj") && !missing.Contains("Example Profile"),
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

    private static void VerifyWorkshopUpdateNotifier()
    {
        Assert(WorkshopUpdateNotifier.TryParseLatestVersionForVerification(
                   "Intro\r\nMod latest version 1.2.3\r\nOther",
                   out Version parsed) &&
               parsed == new Version(1, 2, 3),
            "Workshop update notifier parses the exact latest-version line prefix.");
        Assert(WorkshopUpdateNotifier.TryParseLatestVersionForVerification(
                   "[b]Mod latest version 1.2.4[/b]",
                   out Version bbCodeParsed) &&
               bbCodeParsed == new Version(1, 2, 4),
            "Workshop update notifier parses the BBCode-wrapped latest-version line prefix.");
        Assert(WorkshopUpdateNotifier.IsWorkshopVersionNewerForVerification(
                   new Version(1, 0, 5),
                   "Mod latest version 1.0.6",
                   out Version newer) &&
               newer == new Version(1, 0, 6),
            "Workshop update notifier detects a newer remote version.");
        Assert(!WorkshopUpdateNotifier.IsWorkshopVersionNewerForVerification(
                   new Version(1, 0, 5),
                   "Mod latest version 1.0.5",
                   out _),
            "Workshop update notifier does not flag the installed version as stale when versions match.");
        Assert(!WorkshopUpdateNotifier.IsWorkshopVersionNewerForVerification(
                   new Version(1, 0, 5),
                   "Mod latest version 1.0.4",
                   out _),
            "Workshop update notifier ignores older Workshop versions.");
        Assert(!WorkshopUpdateNotifier.TryParseLatestVersionForVerification(
                   "Mod latest version newest",
                   out _) &&
               !WorkshopUpdateNotifier.TryParseLatestVersionForVerification(
                   "No version line here",
                   out _),
            "Workshop update notifier silently ignores malformed or missing Workshop version lines.");

        string root = FindRepositoryRoot();
        string modProject = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EndlessShapesUnlimited.csproj"));
        string verifierProject = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "EndlessShapesUnlimited.Verification",
            "EndlessShapesUnlimited.Verification.csproj"));
        string pluginSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "Plugin.cs"));
        string notifierSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "WorkshopUpdateNotifier.cs"));
        string alertTextSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuAlertText.cs"));
        string changelogSource = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        string releaseChannelsSource = File.ReadAllText(Path.Combine(root, "RELEASE_CHANNELS.md"));
        string steamReadmeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "README.md"));

        Assert(modProject.Contains("<Reference Include=\"Steamworks\">") &&
               modProject.Contains("Steamworks.dll") &&
               modProject.Contains("<Private>false</Private>") &&
               verifierProject.Contains("<Reference Include=\"Steamworks\">") &&
               verifierProject.Contains("Steamworks.dll") &&
               verifierProject.Contains("<Private>false</Private>"),
            "The mod and verifier reference Steamworks.dll without copying it into the runtime package.");
        Assert(pluginSource.Contains("WorkshopUpdateNotifier.Start(name, version)"),
            "Plugin startup starts the Workshop update notifier after normal startup succeeds.");
        Assert(alertTextSource.Contains("HudCyan = \"#0DE6FF\"") &&
               alertTextSource.Contains("\"<color=\" + HudCyan + \">\"") &&
               pluginSource.Contains("EsuAlertText.HudColorize($\"{name}  v{version.ToString(3)}  Active!\")") &&
               notifierSource.Contains("EsuAlertText.HudColorize(_modName)") &&
               notifierSource.Contains("EsuAlertText.HudColorize(\"New version released! v\""),
            "Main-menu ESU alert rows use the same cyan accent color as the ESU HUD.");
        Assert(notifierSource.Contains("RequestUGCDetails") &&
               notifierSource.Contains("CallResult<SteamUGCRequestUGCDetailsResult_t>") &&
               notifierSource.Contains("LatestVersionPrefix = \"Mod latest version \"") &&
               notifierSource.Contains("StripWorkshopMarkup(line).Trim()") &&
               notifierSource.Contains("MaximumRequests = 5") &&
               notifierSource.Contains("RequestTimeoutSeconds") &&
               notifierSource.Contains("HasRequestTimedOut") &&
               notifierSource.Contains("ScheduleRetry(cancelSteamCall: true)") &&
               notifierSource.Contains("result.m_details.m_eResult != EResult.k_EResultOK") &&
               notifierSource.Contains("string.IsNullOrWhiteSpace(result.m_details.m_rgchDescription)") &&
               notifierSource.Contains("_steamCall?.Cancel()") &&
               notifierSource.Contains("ModProblems.AddModProblem(") &&
               notifierSource.Contains("\"New version released! v\"") &&
               notifierSource.Contains("false);") &&
               notifierSource.Contains("RefreshActiveGuis"),
            "Workshop update notifier queries Steam UGC, retries transient failed/empty responses with a timeout, writes a non-error ModProblems row, and refreshes active GUI screens.");
        Assert(changelogSource.Contains("Steam Workshop update notifier") &&
               changelogSource.Contains("[b]Mod latest version X.Y.Z[/b]") &&
               releaseChannelsSource.Contains("[b]Mod latest version X.Y.Z[/b]") &&
               releaseChannelsSource.Contains("[b]Mod latest version 1.0.7[/b]") &&
               steamReadmeSource.Contains("[b]Mod latest version 1.0.7[/b]"),
            "Changelog and release-channel workflow document the Steam Workshop update version line.");
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
        string folder = Environment.GetEnvironmentVariable("ESU_BLUEPRINT_SAMPLE_DIR") ??
                        Path.Combine("ExternalSamples", "Constructables");
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
        Assert(data.EnforceVanillaCompatibility,
            "Vanilla compatibility mode is enabled by default for a new profile.");
        Assert(!data.StreamLargeBlueprintJsonSaves,
            "Large blueprint JSON streaming saves are disabled by default for a new profile.");
        Assert(data.FastBlueprintLoadTier == FastBlueprintLoadTier.Off &&
               !data.FastBlueprintLoadDiagnostics &&
               !data.FastBlueprintLoadSmallBlueprintTesting &&
               !data.FastBlueprintLoadForceV2BlockData &&
               !data.FastBlueprintLoadBlockCountRouting &&
               data.FastBlueprintLoadUnsafeProbeMode == FastBlueprintLoadUnsafeProbeMode.Off,
            "Experimental fast blueprint loading defaults to vanilla/off for a new profile.");
        Assert(data.EsuEditorAutoScale &&
               Math.Abs(data.EsuEditorScale - 1f) < 0.001f &&
               Math.Abs(data.DecorationMoveSnap - 0.05f) < 0.001f &&
               Math.Abs(data.DecorationRotateSnapDegrees - 5f) < 0.001f &&
               Math.Abs(data.DecorationScaleSnap - 0.05f) < 0.001f &&
               data.SmartBuildMoveStepCells == 1 &&
               Math.Abs(data.SmartBuildRotateSnapDegrees - 90f) < 0.001f &&
               data.SmartBuildScaleStepCells == 1 &&
               EsuHudLayout.ScaleForScreen(1366, 768, autoScale: true, manualScale: 1f) < 1f &&
               Math.Abs(EsuHudLayout.ScaleForScreen(1920, 1080, autoScale: true, manualScale: 1f) - 1f) < 0.001f &&
               Math.Abs(EsuHudLayout.ScaleForScreen(1366, 768, autoScale: false, manualScale: 1f) - 1f) < 0.001f &&
               Math.Abs(EsuHudLayout.ClampManualScale(0.01f) - 0.10f) < 0.001f &&
               Math.Abs(EsuHudLayout.ClampManualScale(3f) - 2f) < 0.001f &&
               Math.Abs(EsuHudLayout.ScaleForScreen(1920, 1080, autoScale: false, manualScale: 0.10f) - 0.10f) < 0.001f &&
               Math.Abs(EsuHudLayout.ScaleForScreen(1920, 1080, autoScale: false, manualScale: 2f) - 2f) < 0.001f &&
               Math.Abs(EsuHudLayout.ClampManualScale(float.NaN) - 1f) < 0.001f,
            "ESU editor HUD scaling defaults to automatic laptop-friendly sizing with a 10%-200% manual multiplier.");

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
               profileSource.Contains("Q(Key.Shift, Key.F8)") &&
               profileSource.Contains("EnforceVanillaCompatibility") &&
               profileSource.Contains("StreamLargeBlueprintJsonSaves") &&
               profileSource.Contains("EsuEditorAutoScale") &&
               profileSource.Contains("EsuEditorScale") &&
               profileSource.Contains("DecorationMoveSnap") &&
               profileSource.Contains("DecorationRotateSnapDegrees") &&
               profileSource.Contains("DecorationScaleSnap") &&
               profileSource.Contains("SmartBuildMoveStepCells") &&
               profileSource.Contains("SmartBuildRotateSnapDegrees") &&
               profileSource.Contains("SmartBuildScaleStepCells") &&
               profileSource.Contains("FastBlueprintLoadTier") &&
               profileSource.Contains("FastBlueprintLoadDiagnostics") &&
               profileSource.Contains("FastBlueprintLoadSmallBlueprintTesting") &&
               profileSource.Contains("FastBlueprintLoadForceV2BlockData") &&
               profileSource.Contains("FastBlueprintLoadBlockCountRouting") &&
               profileSource.Contains("FastBlueprintLoadUnsafeProbeMode") &&
               profileSource.Contains("SkipV3SyncRegistration") &&
               profileSource.Contains("SkipV3StatusRegistration") &&
               profileSource.Contains("SkipStage2ModuleExternalLinkup") &&
               profileSource.Contains("SkipV3ColliderLinkup") &&
               profileSource.Contains("SkipV3ShellLinkup") &&
               profileSource.Contains("SkipV3SkinCalc"),
            "The configurable serialization HUD toggle defaults to F8, exact measurement defaults to Shift+F8, and ESU editor/blueprint save/load/transform snap settings are profiled.");
        string optionsSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "SerializationHudOptionsScreen.cs"));
        Assert(optionsSource.Contains("Vanilla compatibility mode") &&
               optionsSource.Contains("ESU extended decoration data") &&
               optionsSource.Contains("Blueprint saving") &&
               optionsSource.Contains("Stream large blueprint JSON saves") &&
               optionsSource.Contains("64 MiB") &&
               optionsSource.Contains("Blueprint loading") &&
               optionsSource.Contains("Fast load tier") &&
               optionsSource.Contains("Fast load diagnostics") &&
               optionsSource.Contains("Apply fast load to small blueprints") &&
               optionsSource.Contains("Force V2 block-data fast path") &&
               optionsSource.Contains("Apply V3 to block-count-heavy blueprints") &&
               optionsSource.Contains("Unsafe probe mode") &&
               optionsSource.Contains("can create invalid loaded constructs") &&
               optionsSource.Contains("do_not_save=true") &&
               optionsSource.Contains("UnsafeProbeCycle") &&
               optionsSource.Contains("var blueprintLoading = CreateTableSegment(2, 4);"),
            "The ESU options screen exposes vanilla compatibility mode and the streamed large-blueprint JSON saving and opt-in huge-craft loading controls without clipping Blueprint loading rows.");
        Assert(optionsSource.Contains("SubjectiveDisplay<SerializationHudProfile.ProfileData>.Quick") &&
               optionsSource.Contains("FastBlueprintLoadTierStatus(profile)") &&
               optionsSource.Contains("TriggerScreenRebuild()") &&
               SerializationHudOptionsScreen.FastBlueprintLoadTierStatus(
                   new SerializationHudProfile.ProfileData
                   {
                       FastBlueprintLoadTier = FastBlueprintLoadTier.V2
                   }).Contains("V2 - parallel predecode"),
            "The ESU options screen updates the visible fast-load tier immediately after cycling it.");
        Assert(SerializationHudOptionsScreen.FastBlueprintLoadUnsafeProbeStatus(
                   new SerializationHudProfile.ProfileData
                   {
                       FastBlueprintLoadUnsafeProbeMode = FastBlueprintLoadUnsafeProbeMode.SkipV3SyncRegistration
                   }).Contains("Skip V3 sync registration") &&
               SerializationHudOptionsScreen.FastBlueprintLoadUnsafeProbeStatus(
                   new SerializationHudProfile.ProfileData
                   {
                       FastBlueprintLoadUnsafeProbeMode = FastBlueprintLoadUnsafeProbeMode.SkipV3StatusRegistration
                   }).Contains("Skip V3 status registration") &&
               SerializationHudOptionsScreen.FastBlueprintLoadUnsafeProbeStatus(
                   new SerializationHudProfile.ProfileData
                   {
                       FastBlueprintLoadUnsafeProbeMode = FastBlueprintLoadUnsafeProbeMode.SkipStage2ModuleExternalLinkup
                   }).Contains("Skip Stage2 module linkup") &&
               SerializationHudOptionsScreen.FastBlueprintLoadUnsafeProbeStatus(
                   new SerializationHudProfile.ProfileData
                   {
                       FastBlueprintLoadUnsafeProbeMode = FastBlueprintLoadUnsafeProbeMode.SkipV3ColliderLinkup
                   }).Contains("Skip V3 collider linkup") &&
               SerializationHudOptionsScreen.FastBlueprintLoadUnsafeProbeStatus(
                   new SerializationHudProfile.ProfileData
                   {
                       FastBlueprintLoadUnsafeProbeMode = FastBlueprintLoadUnsafeProbeMode.SkipV3ShellLinkup
                   }).Contains("Skip V3 shell linkup") &&
               SerializationHudOptionsScreen.FastBlueprintLoadUnsafeProbeStatus(
                   new SerializationHudProfile.ProfileData
                   {
                       FastBlueprintLoadUnsafeProbeMode = FastBlueprintLoadUnsafeProbeMode.SkipV3SkinCalc
                   }).Contains("Skip V3 skin calculation"),
            "The ESU options screen displays the active unsafe fast-load probe mode immediately.");
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
               rendererSource.Contains("Wire bytes:") &&
               rendererSource.Contains("VanillaCompatibilityGuard.FormatVanillaLoadStatus") &&
               rendererSource.Contains("VanillaCompatibilityGuard.FormatVanillaEditorStatus") &&
               rendererSource.Contains("Largest stream:") &&
               rendererSource.Contains("Save buffer:") &&
               rendererSource.Contains("Vanilla OK") &&
               rendererSource.Contains("10 MB") &&
               rendererSource.Contains("GetRuntimeIcon") &&
               rendererSource.Contains("DrawFittedHudText") &&
               rendererSource.Contains("FittedHudFontSize") &&
               rendererSource.Contains("HudBaseFontSize") &&
               rendererSource.Contains("wordWrap = false") &&
               rendererSource.Contains("TextClipping.Clip"),
            "Serialization HUD shows split aggregate/stream buffer rows with runtime icons and screen-fitted no-wrap text.");
        Assert(SerializationHudRenderer.FormatSaveBuffer(BlueprintSerializationUsage.Empty) == "unknown" &&
               SerializationHudRenderer.FormatHeaderLimit(34UL * 1024UL) == "64 KiB legacy" &&
               SerializationHudRenderer.FormatHeaderLimit(70_000UL) == "4 MiB ESU" &&
               SerializationHudRenderer.FormatDataLimit(540UL * 1024UL) == "6.25 MiB legacy" &&
               SerializationHudRenderer.FormatDataLimit(7_000_000UL) == "64 MiB ESU",
            "Serialization HUD labels vanilla legacy limits and ESU sentinel ceilings explicitly.");
    }

    private static void VerifyVanillaCompatibilityMode()
    {
        var vanillaUsage = new BlueprintSerializationUsage(
            128UL,
            256UL,
            384UL,
            7UL,
            4UL,
            7UL,
            4UL,
            1,
            0,
            0,
            384UL,
            384UL,
            0UL,
            0UL,
            0UL,
            0UL,
            0);
        var sentinelUsage = new BlueprintSerializationUsage(
            128UL,
            256UL,
            384UL,
            65_541UL,
            4UL,
            65_541UL,
            4UL,
            1,
            1,
            0,
            384UL,
            384UL,
            0UL,
            0UL,
            0UL,
            0UL,
            0);
        var modBufferUsage = new BlueprintSerializationUsage(
            (ulong)DecoLimits.VanillaSaveBufferBytes + 1UL,
            0UL,
            (ulong)DecoLimits.VanillaSaveBufferBytes + 1UL,
            7UL,
            4UL,
            7UL,
            4UL,
            1,
            0,
            0,
            (ulong)DecoLimits.VanillaSaveBufferBytes + 1UL,
            (ulong)DecoLimits.VanillaSaveBufferBytes + 1UL,
            0UL,
            0UL,
            0UL,
            0UL,
            0);
        Assert(!VanillaCompatibilityGuard.RequiresExtendedFormat(vanillaUsage) &&
               VanillaCompatibilityGuard.RequiresExtendedFormat(sentinelUsage) &&
               VanillaCompatibilityGuard.RequiresExtendedFormat(modBufferUsage),
            "Vanilla compatibility guard allows legacy/vanilla output and rejects sentinel or ESU-buffer output.");

        var withinLimit = new DecorationUsageSnapshot(
            new[] { new DecorationManagerUsage(null, VanillaCompatibilityGuard.VanillaDecorationLimit) },
            VanillaCompatibilityGuard.VanillaDecorationLimit,
            VanillaCompatibilityGuard.VanillaDecorationLimit);
        var overLimit = new DecorationUsageSnapshot(
            new[] { new DecorationManagerUsage(null, VanillaCompatibilityGuard.VanillaDecorationLimit + 1) },
            VanillaCompatibilityGuard.VanillaDecorationLimit + 1L,
            VanillaCompatibilityGuard.VanillaDecorationLimit + 1);
        Assert(!VanillaCompatibilityGuard.TryFindOverVanillaDecorationLimit(withinLimit, out _) &&
               VanillaCompatibilityGuard.TryFindOverVanillaDecorationLimit(overLimit, out DecorationManagerUsage overManager) &&
               overManager.Count == VanillaCompatibilityGuard.VanillaDecorationLimit + 1,
            "Vanilla compatibility guard enforces the 5,000 decoration cap per manager.");
        Assert(VanillaCompatibilityGuard.ClassifyVanillaLoadCompatibility(withinLimit, vanillaUsage) ==
                   VanillaLoadCompatibilityKind.VanillaCompatible &&
               VanillaCompatibilityGuard.ClassifyVanillaLoadCompatibility(overLimit, vanillaUsage) ==
                   VanillaLoadCompatibilityKind.VanillaCompatible &&
               VanillaCompatibilityGuard.ClassifyVanillaLoadCompatibility(withinLimit, sentinelUsage) ==
                   VanillaLoadCompatibilityKind.SentinelFormat &&
               VanillaCompatibilityGuard.ClassifyVanillaLoadCompatibility(withinLimit, modBufferUsage) ==
                   VanillaLoadCompatibilityKind.ModBuffer,
            "Vanilla load compatibility is classified from wire/buffer requirements, not the 5,000 decoration editor cap.");
        Assert(VanillaCompatibilityGuard.ClassifyVanillaEditorCompatibility(withinLimit) ==
                   VanillaEditorCompatibilityKind.WithinLimit &&
               VanillaCompatibilityGuard.ClassifyVanillaEditorCompatibility(overLimit) ==
                   VanillaEditorCompatibilityKind.TooManyDecorations,
            "The vanilla 5,000 decoration cap is classified as an editor/tool limit separate from load compatibility.");

        string root = FindRepositoryRoot();
        string guardSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "VanillaCompatibilityGuard.cs"));
        string telemetrySource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "SerializationTelemetryPatches.cs"));
        string sessionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditSession.cs"));
        string builderSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EndlessShapes2",
            "DecorationBuilder.cs"));
        string pluginSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "Plugin.cs"));
        string overlaySource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuHudNotificationOverlayRegistration.cs"));
        string layoutSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "ExtendedSerialization",
            "SuperSerialisationLayout.cs"));

        Assert(guardSource.Contains("VanillaDecorationLimit = 5_000") &&
               guardSource.Contains("CreationBlockedMessage") &&
               guardSource.Contains("SaveExtendedFormatBlockedMessage") &&
               guardSource.Contains("SentinelContainerCount > 0") &&
               guardSource.Contains("usage.RequiresModBuffer") &&
               guardSource.Contains("ClassifyVanillaLoadCompatibility") &&
               guardSource.Contains("ClassifyVanillaEditorCompatibility") &&
               guardSource.Contains("FormatVanillaLoadStatus") &&
               guardSource.Contains("FormatVanillaEditorStatus") &&
               guardSource.Contains("Vanilla edit limit: over 5k") &&
               guardSource.Contains("Vanilla Compatibility Mode is OFF; save allowed") &&
               guardSource.Contains("BeginSuppression") &&
               guardSource.Contains("AllConstructDecorations_NewDecoration_VanillaCompatibility_Patch") &&
               guardSource.Contains("TryAllowDecorationCreation") &&
               guardSource.Contains("EnsureBlueprintSaveAllowed") &&
               guardSource.Contains("EsuHudNotifications.ShowSystem") &&
               guardSource.Contains("EsuHudNotificationKind.Error"),
            "Vanilla compatibility guard centralizes the 5,000 editor cap, vanilla-load classification, mode-off warnings, blocked-save popups, load/cleanup suppression, and vanilla NewDecoration patch.");
        Assert(telemetrySource.Contains("EnsureBlueprintSaveAllowed(constructable, __result)") &&
               telemetrySource.Contains("BeginSuppression(\"blueprint load\")") &&
               telemetrySource.Contains("BlueprintLoadTelemetryState") &&
               telemetrySource.Contains("WarnLoadedBlueprintIfNeeded"),
            "Blueprint conversion telemetry blocks incompatible saves but allows and warns on incompatible loads.");
        Assert(sessionSource.Contains("plans.Count") &&
               sessionSource.Contains("_surfacePlan.DecorationCount") &&
               sessionSource.Contains("_generatorPlan.DecorationCount") &&
               sessionSource.Contains("Redo failed: \" + compatibilityMessage") &&
               builderSource.Contains("_polygonDataList.Count") &&
               builderSource.Contains("BeginSuppression(\"decoration builder cleanup\")"),
            "ESU mesh placement, Surface Builder, Path/Circle generator, redo recreation, OBJ builder, and cleanup scopes use the vanilla compatibility guard.");
        bool verifiesVanillaPatch =
            pluginSource.Contains("VerifyExactPatch(\r\n                ResolveVanillaDecorationCreationTarget()") ||
            pluginSource.Contains("VerifyExactPatch(\n                ResolveVanillaDecorationCreationTarget()");
        Assert(pluginSource.Contains("ResolveVanillaDecorationCreationTarget") &&
               pluginSource.Contains("AllConstructDecorations_NewDecoration_VanillaCompatibility_Patch") &&
               verifiesVanillaPatch,
            "Plugin startup verifies the vanilla decoration creation guard patch.");
        Assert(pluginSource.Contains("EsuHudNotificationOverlayRegistration.Register") &&
               pluginSource.Contains("EsuHudNotificationOverlayRegistration.Unregister") &&
               pluginSource.Contains("TrackRollback(EsuHudNotificationOverlayRegistration.Unregister)") &&
               overlaySource.Contains("DontDestroyOnLoad") &&
               overlaySource.Contains("OnGUI") &&
               overlaySource.Contains("EsuHudNotifications.DrawToolbarSlot") &&
               overlaySource.Contains("EsuHudNotifications.DrawExpandedPopup") &&
               overlaySource.Contains("EsuConsoleWindow.Draw") &&
               overlaySource.Contains("DecorationEditorInputScope.Active") &&
               overlaySource.Contains("SmartBuildInputScope.Active") &&
               overlaySource.Contains("EsuRuntimeLog.Exception"),
            "Plugin startup registers a rollback-safe global ESU notification overlay that draws outside ESU editor modes.");
        Assert(layoutSource.Contains("headerLength <= ushort.MaxValue && dataBytes <= MaximumLegacyDataBytes") &&
               layoutSource.Contains("legacy ? SuperContainerFormat.Legacy : SuperContainerFormat.Sentinel"),
            "Serializer legacy/sentinel selection remains the original byte-size calculation.");
    }

    private static void VerifyBlueprintJsonStreamingSave()
    {
        BlueprintFileModel model = NewBlueprintFileModelForJsonStreamingTest();
        JsonConverter[] verificationConverters =
        {
            new VerificationVector3Converter(),
            new VerificationVector4Converter(),
            new VerificationQuaternionConverter(),
            new VerificationColorConverter()
        };
        string vanilla = JsonConvert.SerializeObject(
            model,
            Formatting.None,
            verificationConverters);
        string streamed = BlueprintJsonStreamingSave.SerializeToStringForVerification(
            model,
            Formatting.None,
            PreserveReferencesHandling.None,
            verificationConverters);
        Assert(streamed == vanilla,
            "Streamed blueprint JSON output is byte-equivalent to vanilla JsonConvert for representative models.");

        long measured = BlueprintJsonStreamingSave.MeasureJsonBytesForVerification(
            model,
            Formatting.None,
            PreserveReferencesHandling.None,
            verificationConverters);
        Assert(measured == Encoding.UTF8.GetByteCount(vanilla) &&
               BlueprintJsonStreamingSave.StreamingThresholdBytes == 64L * 1024L * 1024L,
            "Blueprint JSON streaming preflight counts UTF-8 bytes and keeps the 64 MiB threshold fixed.");

        Assert(!BlueprintJsonStreamingSave.ShouldStreamForVerification(
                   model,
                   Formatting.None,
                   PreserveReferencesHandling.None,
                   thresholdBytes: 1L,
                   enabled: false,
                   out _,
                   converters: verificationConverters),
            "Disabled large-blueprint JSON streaming delegates to the vanilla save path.");
        Assert(!BlueprintJsonStreamingSave.ShouldStreamForVerification(
                   model,
                   Formatting.None,
                   PreserveReferencesHandling.None,
                   thresholdBytes: measured + 1L,
                   enabled: true,
                   out _,
                   converters: verificationConverters),
            "Enabled large-blueprint JSON streaming delegates to vanilla below the threshold.");
        Assert(BlueprintJsonStreamingSave.ShouldStreamForVerification(
                   model,
                   Formatting.None,
                   PreserveReferencesHandling.None,
                   thresholdBytes: measured,
                   enabled: true,
                   out long estimatedBytes,
                   converters: verificationConverters) &&
               estimatedBytes == measured,
            "Enabled large-blueprint JSON streaming routes to the streamed writer at or above the threshold.");

        string directory = Path.Combine(
            Path.GetTempPath(),
            "esu-blueprint-json-streaming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "craft.blueprint");
            File.WriteAllText(path, "old blueprint", Encoding.UTF8);
            BlueprintJsonStreamingSave.StreamSaveForVerification(
                path,
                model,
                Formatting.None,
                PreserveReferencesHandling.None,
                verificationConverters);
            Assert(File.ReadAllText(path, Encoding.UTF8) == vanilla &&
                   File.ReadAllText(path + "_backup", Encoding.UTF8) == "old blueprint",
                "Streamed blueprint JSON save atomically replaces an existing file and preserves the normal backup.");

            File.WriteAllText(path, "old after failure", Encoding.UTF8);
            File.Delete(path + "_backup");
            Directory.CreateDirectory(path + "_backup");
            bool failed = false;
            try
            {
                BlueprintJsonStreamingSave.StreamSaveForVerification(
                    path,
                    model,
                    Formatting.None,
                    PreserveReferencesHandling.None,
                    verificationConverters);
            }
            catch
            {
                failed = true;
            }

            Assert(failed && File.ReadAllText(path, Encoding.UTF8) == "old after failure",
                "Failed streamed blueprint JSON replacement leaves the original blueprint untouched.");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void VerifyFastBlueprintLoading()
    {
        Assert(FastBlueprintLoadRouter.LargeBlueprintLoadThresholdBytes == 64L * 1024L * 1024L &&
               !FastBlueprintLoadRouter.ShouldRouteForVerification(
                   FastBlueprintLoadTier.Off,
                   FastBlueprintLoadTier.V1,
                   FastBlueprintLoadRouter.LargeBlueprintLoadThresholdBytes,
                   smallBlueprintTesting: true) &&
               !FastBlueprintLoadRouter.ShouldRouteForVerification(
                   FastBlueprintLoadTier.V1,
                   FastBlueprintLoadTier.V1,
                   FastBlueprintLoadRouter.LargeBlueprintLoadThresholdBytes - 1,
                   smallBlueprintTesting: false) &&
               FastBlueprintLoadRouter.ShouldRouteForVerification(
                   FastBlueprintLoadTier.V1,
                   FastBlueprintLoadTier.V1,
                   FastBlueprintLoadRouter.LargeBlueprintLoadThresholdBytes,
                   smallBlueprintTesting: false) &&
               FastBlueprintLoadRouter.ShouldRouteForVerification(
                   FastBlueprintLoadTier.V3,
                   FastBlueprintLoadTier.V2,
                   1,
                   smallBlueprintTesting: true),
            "Fast blueprint load routing stays off by default, honors the 64 MiB large-blueprint gate, and treats tiers cumulatively.");

        Assert(SerializationHudOptionsScreen.FastBlueprintLoadTierLabel(FastBlueprintLoadTier.Off).Contains("vanilla") &&
               SerializationHudOptionsScreen.FastBlueprintLoadTierLabel(FastBlueprintLoadTier.V1).Contains("streamed JSON") &&
               SerializationHudOptionsScreen.FastBlueprintLoadTierLabel(FastBlueprintLoadTier.V2).Contains("parallel predecode") &&
               SerializationHudOptionsScreen.FastBlueprintLoadTierLabel(FastBlueprintLoadTier.V3).Contains("huge-craft bulk"),
            "Fast blueprint load tier labels describe Off, V1, V2, and V3.");

        Assert(!FastBlueprintLoadRouter.ShouldRouteV2BlockDataForVerification(
                   17_606L,
                   253,
                   forceV2: false,
                   smallBlueprintTesting: false,
                   diagnostics: true,
                   out string tinyReason) &&
               tinyReason == "tiny-block-data" &&
               FastBlueprintLoadRouter.ShouldRouteV2BlockDataForVerification(
                   FastBlueprintLoadRouter.V2BlockDataPayloadThresholdBytes,
                   1,
                   forceV2: false,
                   smallBlueprintTesting: false,
                   diagnostics: false,
                   out string payloadReason) &&
               payloadReason == "large-block-data-payload" &&
               FastBlueprintLoadRouter.ShouldRouteV2BlockDataForVerification(
                   1L,
                   FastBlueprintLoadRouter.V2BlockDataRecordThreshold,
                   forceV2: false,
                   smallBlueprintTesting: false,
                   diagnostics: false,
                   out string recordReason) &&
               recordReason == "many-block-data-records" &&
               FastBlueprintLoadRouter.ShouldRouteV2BlockDataForVerification(
                   1L,
                   1,
                   forceV2: true,
                   smallBlueprintTesting: false,
                   diagnostics: false,
                   out string forceReason) &&
               forceReason == "forced-v2-block-data" &&
               !FastBlueprintLoadRouter.ShouldRouteV2BlockDataForVerification(
                   1L,
                   1,
                   forceV2: false,
                   smallBlueprintTesting: true,
                   diagnostics: true,
                   out string diagnosticReason) &&
               diagnosticReason == "tiny-block-data",
            "V2 block-data routing skips tiny payloads and routes only large, many-record, or forced payloads.");

        Assert(FastBlueprintLoadRouter.ShouldProbeV3MetadataForVerification(
                   FastBlueprintLoadTier.V3,
                   15L * 1024L * 1024L,
                   smallBlueprintTesting: false,
                   blockCountRouting: true) &&
               !FastBlueprintLoadRouter.ShouldProbeV3MetadataForVerification(
                   FastBlueprintLoadTier.V3,
                   15L * 1024L * 1024L,
                   smallBlueprintTesting: false,
                   blockCountRouting: false) &&
               !FastBlueprintLoadRouter.ShouldRouteV3ForVerification(
                   FastBlueprintLoadTier.V3,
                   15L * 1024L * 1024L,
                   savedTotalBlocks: 659_851,
                   blockIdsCount: 659_851,
                   smallBlueprintTesting: false,
                   blockCountRouting: false,
                   out string disabledBlockCountReason) &&
               disabledBlockCountReason == "block-count-routing-disabled" &&
               FastBlueprintLoadRouter.ShouldRouteV3ForVerification(
                   FastBlueprintLoadTier.V3,
                   15L * 1024L * 1024L,
                   savedTotalBlocks: 659_851,
                   blockIdsCount: 659_851,
                   smallBlueprintTesting: false,
                   blockCountRouting: true,
                   out string blockCountReason) &&
               blockCountReason == "large-block-count" &&
               !FastBlueprintLoadRouter.ShouldRouteV3ForVerification(
                   FastBlueprintLoadTier.V3,
                   2L * 1024L * 1024L,
                   savedTotalBlocks: 38_508,
                   blockIdsCount: 20_706,
                   smallBlueprintTesting: false,
                   blockCountRouting: true,
                   out string smallBlockCountReason) &&
               smallBlockCountReason == "below-v3-block-count-threshold",
            "V3 block-count routing is disabled by default and only enables medium-file block-count-heavy blueprints through its explicit setting.");

        Assert(!FastBlueprintLoadRouter.UnsafeProbeActiveForVerification(
                   FastBlueprintLoadUnsafeProbeMode.SkipV3SyncRegistration,
                   diagnostics: false,
                   tier: FastBlueprintLoadTier.V3) &&
               !FastBlueprintLoadRouter.UnsafeProbeActiveForVerification(
                   FastBlueprintLoadUnsafeProbeMode.SkipV3SyncRegistration,
                   diagnostics: true,
                   tier: FastBlueprintLoadTier.V2) &&
               FastBlueprintLoadRouter.UnsafeProbeActiveForVerification(
                   FastBlueprintLoadUnsafeProbeMode.SkipV3SyncRegistration,
                   diagnostics: true,
                   tier: FastBlueprintLoadTier.V3) &&
               FastBlueprintLoadRouter.UnsafeProbeActiveForVerification(
                   FastBlueprintLoadUnsafeProbeMode.SkipV3StatusRegistration,
                   diagnostics: true,
                   tier: FastBlueprintLoadTier.V3) &&
               FastBlueprintLoadRouter.UnsafeProbeActiveForVerification(
                   FastBlueprintLoadUnsafeProbeMode.SkipStage2ModuleExternalLinkup,
                   diagnostics: true,
                   tier: FastBlueprintLoadTier.V3) &&
               FastBlueprintLoadRouter.UnsafeProbeActiveForVerification(
                   FastBlueprintLoadUnsafeProbeMode.SkipV3ColliderLinkup,
                   diagnostics: true,
                   tier: FastBlueprintLoadTier.V3) &&
               FastBlueprintLoadRouter.UnsafeProbeActiveForVerification(
                   FastBlueprintLoadUnsafeProbeMode.SkipV3ShellLinkup,
                   diagnostics: true,
                   tier: FastBlueprintLoadTier.V3) &&
               FastBlueprintLoadRouter.UnsafeProbeActiveForVerification(
                   FastBlueprintLoadUnsafeProbeMode.SkipV3SkinCalc,
                   diagnostics: true,
                   tier: FastBlueprintLoadTier.V3),
            "Unsafe fast-load probes only activate when diagnostics are enabled and V3 is selected.");

        Assert(FastBlueprintLoadRouter.InitialiseStage2SubphaseForVerification(
                   "InitialiseBlocksInConstructable",
                   "IAllBasics") == "Stage2 InitialiseBlocksInConstructable" &&
               FastBlueprintLoadRouter.InitialiseStage2SubphaseForVerification(
                   "HookUpPackages",
                   "AllConstructExtraTypes`3") == "Stage2 package hookup" &&
               FastBlueprintLoadRouter.InitialiseStage2SubphaseForVerification(
                   "set_InitialisationState",
                   "AllConstruct") == "Stage2 init-state commit",
            "Stage2 subphase timer matching covers block initialization, package hookup, and init-state commit calls.");

        string tracePath = FastBlueprintLoadRouter.BuildTracePathForVerification(
            Path.Combine("profile-root"),
            "bad:name/../ship.blueprint",
            new DateTime(2026, 7, 2, 12, 34, 56, DateTimeKind.Utc),
            "ab/cd");
        Assert(tracePath.Contains(Path.Combine("EndlessShapesUnlimited", "Logs")) &&
               tracePath.EndsWith(
                   Path.Combine(
                       "EndlessShapesUnlimited",
                       "Logs",
                       "FastBlueprintLoad-20260702-123456-bad_name_._ship.blueprint-ab_cd.log"),
                   StringComparison.Ordinal) &&
               FastBlueprintLoadRouter.SanitizeTraceFileNameForVerification("bad:name/../ship.blueprint") ==
                   "bad_name_._ship.blueprint",
            "Fast blueprint load trace path generation sanitizes names and stays under EndlessShapesUnlimited/Logs.");

        string offDecision = FastBlueprintLoadRouter.FormatRouteDecisionForVerification(
            FastBlueprintLoadTier.Off,
            diagnostics: true,
            smallBlueprintTesting: false,
            fileBytes: FastBlueprintLoadRouter.LargeBlueprintLoadThresholdBytes,
            routed: false,
            reason: "tier-off");
        string v3Decision = FastBlueprintLoadRouter.FormatRouteDecisionForVerification(
            FastBlueprintLoadTier.V3,
            diagnostics: true,
            smallBlueprintTesting: true,
            fileBytes: 1L,
            routed: true,
            reason: "routed-v1-json-then-v3-safe-bulk");
        Assert(offDecision.Contains("tier=\"Off\"") &&
               offDecision.Contains("routed=false") &&
               offDecision.Contains("reason=\"tier-off\"") &&
               v3Decision.Contains("tier=\"V3\"") &&
               v3Decision.Contains("small_testing=true") &&
               v3Decision.Contains("routed=true") &&
               v3Decision.Contains("reason=\"routed-v1-json-then-v3-safe-bulk\""),
            "Fast blueprint load route-decision formatting covers disabled, routed, large-file, and small-blueprint override cases.");

        Assert(FastBlueprintLoadRouter.V3PreflightTargetsAvailableForVerification(),
            "V3 safe-bulk verification records the current FtD Block.BlockStateChanged metadata token without forcing fragile runtime reflection.");
        Assert(Plugin.ResolvePartStatusRegisterCheckableBlockTarget() != null &&
               Plugin.ResolvePartStatusUnregisterCheckableBlockTarget() != null,
            "V3 status registration preflight resolves the FtD IStatusHandler checkable-block methods.");

        BlockStateChangeType[] v3CapturedTypes =
        {
            BlockStateChangeType.Initiated,
            BlockStateChangeType.InitiatedNewPlacement,
            BlockStateChangeType.InitiatedInUnrepairedState
        };
        BlockStateChangeType[] v3SkippedTypes =
        {
            BlockStateChangeType.Loaded,
            BlockStateChangeType.FullyLoadedAndInitiated,
            BlockStateChangeType.DamageReceived,
            BlockStateChangeType.RemovedFromDesign,
            BlockStateChangeType.Repaired,
            BlockStateChangeType.Killed
        };
        Assert(v3CapturedTypes.All(type => FastBlueprintLoadRouter.V3CaptureDecisionForVerification(type).Capture) &&
               v3SkippedTypes.All(type => !FastBlueprintLoadRouter.V3CaptureDecisionForVerification(type).Capture),
            "V3 safe-bulk capture only suppresses initial positive load state changes and leaves loaded/damaged/removed/repaired/killed changes vanilla.");

        JsonConverter[] verificationConverters =
        {
            new VerificationVector3Converter(),
            new VerificationVector4Converter(),
            new VerificationQuaternionConverter(),
            new VerificationColorConverter()
        };
        BlueprintFileModel model = NewBlueprintFileModelForJsonStreamingTest();
        model.Blueprint = null;
        string vanilla = JsonConvert.SerializeObject(
            model,
            Formatting.None,
            verificationConverters);
        BlueprintFileModel streamed =
            FastBlueprintLoadRouter.LoadBlueprintFileModelFromJsonForVerification(
                vanilla,
                PreserveReferencesHandling.None,
                verificationConverters);
        Assert(streamed != null &&
               streamed.Name == model.Name &&
               streamed.SavedTotalBlockCount == model.SavedTotalBlockCount &&
               streamed.Blueprint == null &&
               streamed.ItemDictionary.Count == model.ItemDictionary.Count,
            "V1 streamed blueprint JSON loading reconstructs representative BlueprintFileModel envelope data.");

        byte[] first = SerialiseFixture(1U, 4U, 7UL, 3);
        byte[] second = SerialiseFixture(1U, 8U, 2UL, 3);
        byte[] blockData = first.Concat(second).ToArray();
        FastBlueprintBlockDataRecord[] records =
            FastBlueprintLoadRouter.ScanBlockDataForVerification(blockData);
        int[] decodedIds =
            FastBlueprintLoadRouter.PredecodeBlockIdsForVerification(blockData);
        Assert(records.Length == 2 &&
               records[0].Ordinal == 0 &&
               records[0].BlockIndex == 7 &&
               records[0].Start == 0U &&
               records[0].End == (uint)first.Length &&
               records[1].Ordinal == 1 &&
               records[1].BlockIndex == 2 &&
               records[1].Start == (uint)first.Length &&
               records[1].End == (uint)blockData.Length &&
               decodedIds.SequenceEqual(new[] { 7, 2 }),
            "V2 block-data scanning and parallel predecode preserve block order, block ids, and byte boundaries.");

        byte[] firstDistinct = SerialiseFixture(1U, 4U, 7UL, 3, 321U, 10);
        byte[] secondDistinct = SerialiseFixture(1U, 8U, 2UL, 3, 654U, 40);
        SuperLoader[] retainedLoaders =
            FastBlueprintLoadRouter.PredecodeBlockLoadersForVerification(
                firstDistinct.Concat(secondDistinct).ToArray());
        Assert(retainedLoaders.Length == 2 &&
               !ReferenceEquals(retainedLoaders[0].Header, retainedLoaders[1].Header) &&
               !ReferenceEquals(retainedLoaders[0].DataSorted, retainedLoaders[1].DataSorted) &&
               ByteConversion.ConvertOut(retainedLoaders[0].Header, 0U, 3) == 321U &&
               ByteConversion.ConvertOut(retainedLoaders[1].Header, 0U, 3) == 654U &&
               retainedLoaders[0].DataSorted[0] == 11 &&
               retainedLoaders[0].DataSorted[3] == 14 &&
               retainedLoaders[1].DataSorted[0] == 41 &&
               retainedLoaders[1].DataSorted[7] == 48,
            "V2 block-data predecode retains independent loader buffers until serial apply.");

        string pluginSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "EndlessShapesUnlimited",
            "Source",
            "Plugin.cs"));
        string routerSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "EndlessShapesUnlimited",
            "Source",
            "Patches",
            "FastBlueprintLoadRouter.cs"));
        string traceSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "EndlessShapesUnlimited",
            "Source",
            "Patches",
            "FastBlueprintLoadTrace.cs"));
        string telemetrySource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "SerializationTelemetryPatches.cs"));
        string fastLoadDeveloperGuide = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "FAST_BLUEPRINT_LOADING_DEVELOPER_GUIDE.md"));
        string fastLoadReview = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "FAST_BLUEPRINT_LOADING_TECHNICAL_REVIEW.md"));
        string steamReadmeSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "EndlessShapesUnlimited",
            "README.md"));
        string changelogSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "CHANGELOG.md"));
        Assert(pluginSource.Contains("ResolveFastBlueprintFileModelLoadTarget") &&
               pluginSource.Contains("ResolveConstructExtraInfoDataArrayTarget") &&
               pluginSource.Contains("ResolveConstructExtraInfoProvideInfoToBlocksTarget") &&
               pluginSource.Contains("ResolveConstructExtraInfoDoubleArrayTarget") &&
               pluginSource.Contains("ResolveConstructExtraInfoUpgradeConstructTarget") &&
               pluginSource.Contains("ResolveBlockBlockStateChangedTarget") &&
               pluginSource.Contains("InstallOptionalV3BlockStatePatch") &&
               pluginSource.Contains("InstallOptionalStage2ModuleExternalLinkupPatch") &&
               pluginSource.Contains("ResolveAllConstructInitialiseStage2Target") &&
               pluginSource.Contains("ResolveStage2ModuleExternalLinkupTarget") &&
               pluginSource.Contains("ResolvePartStatusRegisterCheckableBlockTarget") &&
               pluginSource.Contains("ResolvePartStatusUnregisterCheckableBlockTarget") &&
               pluginSource.Contains("InstallOptionalV3DColliderInternalTimingPatch") &&
               routerSource.Contains("BlueprintFile_Load_FastLoad_Patch") &&
               routerSource.Contains("ConstructExtraInfo_DataArray_FastLoad_Patch") &&
               routerSource.Contains("ConstructExtraInfo_ProvideInfoToBlocks_V3BulkLoad_Patch") &&
               routerSource.Contains("ConstructExtraInfo_DoubleArray_FastLoadDiagnostics_Patch") &&
               routerSource.Contains("ConstructExtraInfo_UpgradeConstruct_FastLoadDiagnostics_Patch") &&
               routerSource.Contains("Block_BlockStateChanged_V3BulkLoad_Patch") &&
               routerSource.Contains("AllConstruct_Stage2ModuleExternalLinkup_FastLoadDiagnostics_Patch") &&
               routerSource.Contains("InstallOptionalStage2ModuleCallsitePatch") &&
               !pluginSource.Contains("InstallOptionalStage2ModuleCallsitePatch") &&
               routerSource.Contains("InstallOptionalStage2ModuleExternalLinkupPatch") &&
               !routerSource.Contains("[HarmonyPatch]\r\n    internal static class AllConstruct_Stage2ModuleExternalLinkup_FastLoadDiagnostics_Patch") &&
               !routerSource.Contains("[HarmonyPatch]\n    internal static class AllConstruct_Stage2ModuleExternalLinkup_FastLoadDiagnostics_Patch") &&
               routerSource.Contains("AddInitialiseStage2SubphaseTimers") &&
               routerSource.Contains("AddStage2ModuleExternalLinkupCallsiteTimers") &&
               routerSource.Contains("BrilliantSkies.Common.Modules.AllConstructExtraTypes`3") &&
               routerSource.Contains("FindLoadedTypeByFullName") &&
               routerSource.Contains("ShouldUseStage2ModuleCallsiteDiagnostics") &&
               routerSource.Contains("CallStage2ModuleDictionaryLinkupWithDiagnostics") &&
               routerSource.Contains("CallStage2ModuleExternalLinkupWithDiagnostics") &&
               routerSource.Contains("OpCodes.Brfalse_S") &&
               routerSource.Contains("AddLabelUntyped") &&
               routerSource.Contains("MoveMetadataUntyped") &&
               routerSource.Contains("stage2-module-callsite-loop-start") &&
               routerSource.Contains("stage2-module-callsite-loop-complete") &&
               routerSource.Contains("stage2-module-callsite-loop-fallback") &&
               routerSource.Contains("stage2-module-callsite-linkup") &&
               routerSource.Contains("v3d-collider-linkup") &&
               routerSource.Contains("v3d-shell-linkup") &&
               routerSource.Contains("v3d-skin-calc-linkup") &&
               routerSource.Contains("v3d-collider-target-discovery") &&
               routerSource.Contains("v3d-collider-subphase") &&
               routerSource.Contains("v3d-collider-subphase-unsupported") &&
               routerSource.Contains("v3d-collider-callsite-fallback") &&
               routerSource.Contains("InstallOptionalV3DColliderInternalTimingPatch") &&
               routerSource.Contains("ConstructableColliderCommon_InternalTiming_Patch") &&
               routerSource.Contains("BeginV3DColliderInternalTiming") &&
               routerSource.Contains("EndV3DColliderInternalTiming") &&
               routerSource.Contains("FlushV3DColliderInternalTimingParent") &&
               routerSource.Contains("v3d-collider-subphase-accounting") &&
               routerSource.Contains("V3DColliderInternalTimingMethodNames") &&
               routerSource.Contains("call_count") &&
               routerSource.Contains("max_single_call_ms") &&
               routerSource.Contains("parent_elapsed_ms") &&
               routerSource.Contains("known_subphase_elapsed_ms") &&
               routerSource.Contains("unaccounted_elapsed_ms") &&
               routerSource.Contains("target-not-resolved") &&
               routerSource.Contains("EnableCollidersForPositions") &&
               routerSource.Contains("MarkCollidersForDisabling") &&
               routerSource.Contains("DisableMarkedColliders") &&
               routerSource.Contains("EnableIntersectingColliders") &&
               routerSource.Contains("UpdateCollidersForTerrain") &&
               routerSource.Contains("ProcessOutOfDateColliders") &&
               routerSource.Contains("SwapEnabledColliderLists") &&
               routerSource.Contains("OnColliderChange") &&
               routerSource.Contains("internal_timing_installed") &&
               routerSource.Contains("internal_timing_target_count") &&
               routerSource.Contains("v3d-shell-subphase") &&
               routerSource.Contains("v3d-skin-calc-subphase") &&
               routerSource.Contains("v3d-unsafe-skip") &&
               routerSource.Contains("v3d-target-unsupported") &&
               routerSource.Contains("ConstructableColliderCommonTypeName") &&
               routerSource.Contains("FindColliderCandidateMethods") &&
               routerSource.Contains("collection_count_before") &&
               routerSource.Contains("collection_count_after") &&
               routerSource.Contains("unsupported_reason") &&
               routerSource.Contains("diagnostics_enabled") &&
               routerSource.Contains("runtime_type") &&
               routerSource.Contains("declaring_type") &&
               routerSource.Contains("ConstructableColliders") &&
               routerSource.Contains("AllConstructShell") &&
               routerSource.Contains("MainConstructSkinCalc") &&
               routerSource.Contains("InvokeStage2ModuleDictionaryLinkup(moduleDictionary)") &&
               routerSource.Contains("stage2-module-linkup") &&
               routerSource.Contains("IAllConstructModule") &&
               routerSource.Contains("SkipStage2ModuleExternalLinkupForDiagnostics &&") &&
               routerSource.Contains("_stage2ModuleExternalLinkupPatchInstalled") &&
               routerSource.Contains("BlockBlockStateChangedMetadataToken = 0x06000D86") &&
               routerSource.Contains("InstallOptionalV3BlockStatePatch") &&
               routerSource.Contains("new SuperLoader(useStaticArrays: false)") &&
               routerSource.Contains("V2BlockDataPayloadThresholdBytes") &&
               routerSource.Contains("V2BlockDataRecordThreshold") &&
               routerSource.Contains("V3BlockCountThreshold") &&
               routerSource.Contains("v3-metadata-route") &&
               routerSource.Contains("v2-skip") &&
               routerSource.Contains("routed-v1-json-then-v3-safe-bulk") &&
               routerSource.Contains("v3-preflight") &&
               routerSource.Contains("v3-scope-start") &&
               routerSource.Contains("v3-presize") &&
               routerSource.Contains("v3-status-bulk-preflight") &&
               routerSource.Contains("v3-status-bulk-complete") &&
               routerSource.Contains("v3-status-bulk-fallback") &&
               routerSource.Contains("v3-capture-summary") &&
               routerSource.Contains("missing_targets") &&
               routerSource.Contains("IStatusHandler.RegisterCheckableBlock(ICheckState)") &&
               routerSource.Contains("BrilliantSkies.Common.StatusChecking.IStatusHandler") &&
               routerSource.Contains("BrilliantSkies.Common.StatusChecking.ICheckState") &&
               routerSource.Contains("BrilliantSkies.Common.StatusChecking.StatusHandler") &&
               routerSource.Contains("AddNestedStatusHandlerSnapshots") &&
               routerSource.Contains("v3-flush-start") &&
               routerSource.Contains("v3-flush-complete") &&
               routerSource.Contains("v3-flush-registry") &&
               routerSource.Contains("\"v3.flush.\" + name") &&
               routerSource.Contains("\"syncRegistration\"") &&
               routerSource.Contains("\"statusSkipped\"") &&
               routerSource.Contains("SkipV3StatusRegistration") &&
               routerSource.Contains("SkipStage2ModuleExternalLinkup") &&
               routerSource.Contains("SkipV3ColliderLinkupForDiagnostics") &&
               routerSource.Contains("SkipV3ShellLinkupForDiagnostics") &&
               routerSource.Contains("SkipV3SkinCalcForDiagnostics") &&
               routerSource.Contains("correctness_valid\", !unsafeActive") &&
               routerSource.Contains("unsafe_probe_active") &&
               routerSource.Contains("correctness_valid") &&
               routerSource.Contains("do_not_save") &&
               routerSource.Contains("correctness_invalid") &&
               routerSource.Contains("v3-flush-failed") &&
               routerSource.Contains("v3-fallback") &&
               routerSource.Contains("FlushV3BeforeBlockData") &&
               routerSource.Contains("ConditionalWeakTable<Blueprint, FastBlueprintLoadTrace>") &&
               routerSource.Contains("BeginBlueprintConversionTrace") &&
               routerSource.Contains("CompleteBlueprintConversionTrace") &&
               routerSource.Contains("FailBlueprintConversionTrace") &&
               routerSource.Contains("BlueprintTraceName") &&
               routerSource.Contains("standalone-route-decision") &&
               routerSource.Contains("conversion_file_bytes_known") &&
               routerSource.Contains("conversion_path_file_bytes") &&
               !routerSource.Contains("Pair(\"file_bytes\", -1L)") &&
               routerSource.Contains("v3_bulk_context_expected") &&
               routerSource.Contains("v3_bulk_context_active") &&
               routerSource.Contains("capture_flush_rows_expected") &&
               routerSource.Contains("standalone-blueprint-not-v3-routed") &&
               routerSource.Contains("block_data_record_count") &&
               routerSource.Contains("v2_skipped") &&
               routerSource.Contains("standalone_trace") &&
               traceSource.Contains("FileHeartbeatInterval = TimeSpan.FromSeconds(30)") &&
               traceSource.Contains("AdvLoggerHeartbeatInterval = TimeSpan.FromMinutes(5)") &&
               traceSource.Contains("CraftLoadStart") &&
               traceSource.Contains("craft-load-start") &&
               traceSource.Contains("CraftLoadComplete") &&
               traceSource.Contains("craft-load-complete") &&
               traceSource.Contains("CraftLoadFailed") &&
               traceSource.Contains("craft-load-failed") &&
               traceSource.Contains("total_elapsed_ms") &&
               traceSource.Contains("FastBlueprintLoadProgressStream") &&
               traceSource.Contains("_trace?.Heartbeat") &&
               traceSource.Contains("ProfileRootDir") &&
               traceSource.Contains("EndlessShapesUnlimited") &&
               traceSource.Contains("Logs") &&
               fastLoadDeveloperGuide.Contains("recommended opt-in mode") &&
               fastLoadDeveloperGuide.Contains("3.8 million block") &&
               fastLoadDeveloperGuide.Contains("2.5 hours") &&
               fastLoadDeveloperGuide.Contains("21 minutes") &&
               fastLoadReview.Contains("optimization push is paused") &&
               steamReadmeSource.Contains("extremely high block-count craft") &&
               changelogSource.Contains("3.8 million block craft") &&
               telemetrySource.Contains("BeginBlueprintConversionTrace(blueprint)") &&
               telemetrySource.Contains("CompleteFastLoadTrace") &&
               telemetrySource.Contains("FailFastLoadTrace(__exception)"),
            "Fast blueprint load startup verifies V1/V2 patch targets, V3 safe-bulk hooks, shareable trace files, heartbeat progress, end-to-end load timers, and conversion trace bridging.");
    }

    private static BlueprintFileModel NewBlueprintFileModelForJsonStreamingTest()
    {
        Blueprint blueprint = NewBlueprintForUsageTest();
        blueprint.BlockIds = new[] { 1, 2, 3 };
        blueprint.BLP = Array.Empty<Vector3>();
        blueprint.BLR = new[] { 0, 1, 2 };
        blueprint.BCI = new[] { 0, 0, 1 };
        blueprint.BlockData = new byte[] { 1, 2, 3, 4, 5, 6 };
        blueprint.VehicleData = new byte[] { 7, 8, 9 };
        blueprint.SCs = new List<Blueprint>();

        return new BlueprintFileModel
        {
            Name = "stream-test",
            Version = 1,
            SavedTotalBlockCount = 3,
            SavedMaterialCost = 12.5f,
            ContainedMaterialCost = 1.5f,
            ItemDictionary = new Dictionary<int, Guid>
            {
                { 1, Guid.Parse("11111111-1111-1111-1111-111111111111") },
                { 2, Guid.Parse("22222222-2222-2222-2222-222222222222") }
            },
            Blueprint = blueprint
        };
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

        Assert(EsuSymmetry.MirrorCell(new Vector3i(3, 4, 5), DecorationEditAxis.X, 1)
                   .Equals(new Vector3i(-1, 4, 5)) &&
               EsuSymmetry.MirrorCell(new Vector3i(3, 4, 5), DecorationEditAxis.Y, 2)
                   .Equals(new Vector3i(3, 0, 5)) &&
               Math.Abs(EsuSymmetry.MirrorVector(new Vector3(3.25f, 4.5f, 5.75f), DecorationEditAxis.Z, 3).z - 0.25f) < 0.001f,
            "ESU symmetry mirrors cells and decoration centers across X/Y/Z grid planes.");

        var transformMirror = new EsuSymmetry.SymmetryVariant(new[] { DecorationEditAxis.X });
        Vector3 sourceScale = new Vector3(2f, 3f, 4f);
        Vector3 mirroredScale = transformMirror.MirrorScale(sourceScale);
        Vector3 mirroredDirection = transformMirror.MirrorDirection(new Vector3(3f, 4f, 5f));
        Assert(Math.Abs(mirroredScale.x + sourceScale.x) < 0.0001f &&
               Math.Abs(mirroredScale.y - sourceScale.y) < 0.0001f &&
               Math.Abs(mirroredScale.z - sourceScale.z) < 0.0001f &&
               VectorApproximately(mirroredDirection, new Vector3(-3f, 4f, 5f), 0.0001f),
            "ESU symmetry mirrors decoration directions and signed scale.");

        EsuSymmetry.Clear();
        EsuSymmetry.SetPlaneForTests(DecorationEditAxis.X, 1);
        EsuSymmetry.SetPlaneForTests(DecorationEditAxis.Y, 2);
        EsuSymmetry.SetPlaneForTests(DecorationEditAxis.Z, 3);
        Vector3i[] mirroredCells = EsuSymmetry.Variants()
            .Select(variant => variant.Mirror(new Vector3i(3, 4, 5)))
            .ToArray();
        Vector3i[] dedupedPlaneCells = EsuSymmetry.MirrorCells(new[] { new Vector3i(1, 2, 3) })
            .ToArray();
        Assert(mirroredCells.Length == 8 &&
               mirroredCells.Select(EsuSymmetry.CellKey).Distinct().Count() == 8 &&
               dedupedPlaneCells.Length == 1,
            "ESU symmetry combines active axes into 2/4/8 variants and dedupes cells that lie on every active plane.");
        EsuSymmetry.Clear();

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
        string smartBuildBehaviourSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildModeBehaviour.cs"));
        string modeSwitchHandoffSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuModeSwitchHandoff.cs"));
        string escapeCloseGuardSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuEscapeCloseGuard.cs"));
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
        string focusGuardSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuInputFocusGuard.cs"));
        string symmetrySource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuSymmetry.cs"));
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
        string notificationSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuHudNotifications.cs"));
        string serializationHudSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "SerializationHudRenderer.cs"));
        string inGameTestPlanSource = ReadDocumentationText(root, "docs", "IN_GAME_TEST_PLAN.md");
        string builderUiSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EndlessShapes2",
            "ES2_UI",
            "DBUI_BasicSettingTab.cs"));
        string sessionSourceNormalized = sessionSource.Replace("\r\n", "\n");
        string inputScopeSourceNormalized = inputScopeSource.Replace("\r\n", "\n");
        string inspectorSource = ExtractMethodSource(sessionSource, "DrawInspector");
        string drawEditorShellSource = ExtractMethodSource(sessionSource, "DrawEditorShell").Replace("\r\n", "\n");
        string handleSceneInputSource = ExtractMethodSource(sessionSource, "HandleSceneInput").Replace("\r\n", "\n");
        string drawMeshPreviewGridSource = ExtractMethodSource(sessionSource, "DrawMeshPreviewGrid").Replace("\r\n", "\n");
        string meshPreviewGridLayoutSource = ExtractMethodSource(sessionSource, "MeshPreviewGridLayoutFor").Replace("\r\n", "\n");
        string handleEditorKeybindsSource = ExtractMethodSource(sessionSource, "HandleEditorKeybinds").Replace("\r\n", "\n");
        string readEditorKeyDownSource = ExtractMethodSource(sessionSource, "ReadEditorKeyDown").Replace("\r\n", "\n");
        string setSelectedColorSource = ExtractMethodSource(sessionSource, "SetSelectedColor").Replace("\r\n", "\n");
        string setSelectedDecorationsColorSource = ExtractMethodSource(sessionSource, "SetSelectedDecorationsColor").Replace("\r\n", "\n");
        string addNearestToSelectionSource = ExtractMethodSource(sessionSource, "AddNearestToSelection").Replace("\r\n", "\n");
        string tryApplySymmetryFollowSource = ExtractMethodSource(sessionSource, "TryApplySymmetryFollow").Replace("\r\n", "\n");
        string tryApplyDecorationSymmetryTransformSource = ExtractMethodSource(sessionSource, "TryApplyDecorationSymmetryTransform").Replace("\r\n", "\n");
        int boxSelectRightClickIndex = handleSceneInputSource.IndexOf(
            "_tool == DecorationEditorTool.Select &&\n                _selectionMode == DecorationSelectionMode.Box",
            StringComparison.Ordinal);
        int decorationContextMenuIndex = handleSceneInputSource.IndexOf(
            "TryOpenDecorationContextMenu()",
            StringComparison.Ordinal);
        string boxSelectRightClickPath = boxSelectRightClickIndex >= 0 &&
                                         decorationContextMenuIndex > boxSelectRightClickIndex
            ? handleSceneInputSource.Substring(
                boxSelectRightClickIndex,
                decorationContextMenuIndex - boxSelectRightClickIndex)
            : string.Empty;
        int previousSelectionIndex = addNearestToSelectionSource.IndexOf(
            "var previousSelection = _selection.ToList()",
            StringComparison.Ordinal);
        int additiveSetPrimaryIndex = addNearestToSelectionSource.IndexOf(
            "SetPrimarySelection(hit.Decoration, hit.Construct)",
            StringComparison.Ordinal);
        int restorePreviousSelectionIndex = addNearestToSelectionSource.IndexOf(
            "previousSelection.Count",
            StringComparison.Ordinal);
        int addClickedSelectionIndex = addNearestToSelectionSource.LastIndexOf(
            "_selection.Add(hit.Decoration)",
            StringComparison.Ordinal);
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
               registrationSource.Contains("modeSwitch: true") &&
               registrationSource.Contains("CanSwitchEsuModes") &&
               registrationSource.Contains("Close Smart Block Builder before opening Decoration Edit Mode.") &&
               buildModeInputGateSource.Contains("_decorationEditToggleRequiresRelease") &&
               buildModeInputGateSource.Contains("ReadDecorationEditToggleDown") &&
               buildModeInputGateSource.Contains("ReadDecorationEditToggleHeld") &&
               buildModeInputGateSource.Contains("ReadProfileKey(") &&
               buildModeInputGateSource.Contains("KeyInputEventType.Held") &&
               buildModeInputGateSource.Contains("return SerializationHudKeyMap.Instance.Bool(input, eventType);") &&
               buildModeInputGateSource.Contains("fallback != null && fallback()") &&
               !buildModeInputGateSource.Contains("return toggleDown ||") &&
               !buildModeInputGateSource.Contains("return switchDown ||") &&
               handleEditorKeybindsSource.Contains("SerializationHudKeyInput.UndoDecorationEdit") &&
               handleEditorKeybindsSource.Contains("SerializationHudKeyInput.RedoDecorationEdit") &&
               !handleEditorKeybindsSource.Contains("Input.GetKeyDown(KeyCode.Z)") &&
               !handleEditorKeybindsSource.Contains("Input.GetKeyDown(KeyCode.Y)") &&
               readEditorKeyDownSource.Contains("return SerializationHudKeyMap.Instance.Bool(") &&
               readEditorKeyDownSource.Contains("fallback != null && fallback()"),
            "Decoration Edit Mode has repository-profile keybinds, rejects direct opens over Smart Builder, and uses shared profiled input gates with defaults only as profile-failure fallbacks.");
        Assert(!sessionSource.Contains("SetGameControlOptions(") &&
                focusGuardSource.Contains("internal static class EsuInputFocusGuard") &&
                !focusGuardSource.Contains("SetGameControlOptions(") &&
                !focusGuardSource.Contains("StaticOptionsManager.SetMouseCursorActive") &&
                !focusGuardSource.Contains("MouseCursorModeChangeSource") &&
                !focusGuardSource.Contains("_playerWantsMouseLookInUi") &&
                !focusGuardSource.Contains("PlayerWantsMouseLookInUi") &&
                !focusGuardSource.Contains("AccessTools.Method(typeof(UserInput), \"SetCursor\")") &&
                !focusGuardSource.Contains("InvokeSetCursor") &&
                !focusGuardSource.Contains("PostExitRepairFrames") &&
                !focusGuardSource.Contains("PostExitRepairActive") &&
                focusGuardSource.Contains("TickPostExitRepair") &&
                !focusGuardSource.Contains("gameDisableKeys: true") &&
                !focusGuardSource.Contains("gameShowMouseCursor: false") &&
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
               inputScopeSource.Contains("EsuInputFocusGuard.BeginEditor") &&
               inputScopeSource.Contains("EsuInputFocusGuard.EndEditor") &&
               inputScopeSource.Contains("EsuEscapeCloseGuard.Active") &&
               sessionSource.Contains("DecorationEditorInputScope.End();") &&
               !inputScopeSource.Contains("SuppressBuildInput() => _active") &&
               inputScopeSource.Contains("ControlHeldWhileActive") &&
               inputScopeSource.Contains("DecoLimitLifter.EsuInputState.IsControlHeld()") &&
               inputScopeSource.Contains("ControlHeldWhileActive ||") &&
               !inputScopeSourceNormalized.Contains("ControlHeldWhileActive ||\n            OwnsCameraInputThisFrame") &&
                inputScopeSource.Contains("HudBuildCommands") &&
                inputScopeSource.Contains("DrawInsideBuildMode") &&
                inputScopeSource.Contains("cBuild") &&
                inputScopeSource.Contains("BuildCameraMode"),
            "Decoration Edit Mode uses a no-op cursor focus guard, scoped build-input/Ctrl claims, safe decoration wireframe, public decoration enumeration/creation APIs, native HUD/input suppression, and transaction rollback.");
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
        Assert(sessionSource.Contains("DecorationEditorInputScope.SetMouseOverEditorUi(IsMouseOverEditorUi(_lastMouseGui))") &&
               sessionSource.Contains("private bool IsMouseOverEditorUi(Vector2 mouse)") &&
               sessionSource.Contains("EsuHudNotifications.ContainsMouse(mouse)") &&
               sessionSource.Contains("DecorationEditorInputScope.ClaimMouseWheelInputForFrames();"),
            "Decoration Edit Mode treats the shared notification/log overlay as ESU-owned UI before camera zoom handles mouse wheel input.");

        var definitions = DecorationEditorIconCatalog.Definitions;
        string iconCatalogSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditorIconCatalog.cs"));
        Assert(definitions.Any(icon => icon.Key == "move" &&
                                       icon.Guid == new Guid("68419445-57e1-41ac-89c9-7683976ddcff")) &&
               definitions.Any(icon => icon.Key == "select" &&
                                       icon.Guid == new Guid("f417ee2c-2aa4-4fb2-ab9f-de4c59b94e45")) &&
               definitions.Any(icon => icon.Key == "boxSelect" && icon.Guid == Guid.Empty) &&
               definitions.Any(icon => icon.Key == "close" && icon.Guid == Guid.Empty) &&
               definitions.Any(icon => icon.Key == "symmetryX" && icon.Guid == Guid.Empty) &&
               definitions.Any(icon => icon.Key == "outliner" && icon.Guid == Guid.Empty) &&
               definitions.Any(icon => icon.Key == "cube" && icon.Guid == Guid.Empty) &&
               iconCatalogSource.Contains("Texture2D texture = CreateFallback(definition);") &&
               iconCatalogSource.Contains("DrawIcon(texture, definition.Key, definition.FallbackGlyph, accent);") &&
               iconCatalogSource.Contains("DrawAxes(texture);") &&
               iconCatalogSource.Contains("DrawRotateGizmoIcon(texture);") &&
               iconCatalogSource.Contains("DrawBoxSelect(texture, color);") &&
               iconCatalogSource.Contains("DrawSymmetryPlaneIcon(texture, DecorationEditAxis.X);") &&
               iconCatalogSource.Contains("DrawPaintBucket(texture, color);") &&
               iconCatalogSource.Contains("DrawFloppy(texture, color);") &&
               iconCatalogSource.Contains("DrawTrash(texture, color);") &&
               !iconCatalogSource.Contains("DrawBoxLetter") &&
               !iconCatalogSource.Contains("TryFindRuntimeTexture(definition) ??") &&
               iconCatalogSource.Contains("internal static Texture2D GetRuntimeIcon(string key)") &&
               iconCatalogSource.Contains("Texture2D texture = TryFindRuntimeTexture(definition);"),
            "Decoration Edit Mode icon catalog records FTD runtime icon GUIDs and ESU-owned fallback concepts, while editor buttons use deterministic ESU icons instead of loose global texture matches.");

        string themeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditorTheme.cs"));
        string layoutSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuHudLayout.cs"));
        string transformSnapSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuTransformSnapSettings.cs"));
        string decorationScaleBoundsSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationScaleBounds.cs"));
        string decorationEditSnapshotSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditSnapshot.cs"));
        string mimicAndDecorationCommonDataSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EndlessShapes2",
            "ES2_Polygon",
            "MimicAndDecorationCommonData.cs"));
        string toolbarAttentionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuToolbarAttention.cs"));
        string smartBuildSessionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildSession.cs"));
        string smartBuildSessionSourceNormalized = smartBuildSessionSource.Replace("\r\n", "\n");
        string smartInputScopeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildInputScope.cs"));
        string optionsSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SerializationHud",
            "SerializationHudOptionsScreen.cs"));
        string pluginSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "Plugin.cs"));
        string tooltipSuppressorSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationTooltipSuppressor.cs"));
        string cursorTooltipSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuCursorTooltip.cs"));
        Assert(themeSource.Contains("PanelColor") &&
               themeSource.Contains("ToolButton") &&
               themeSource.Contains("Ensure(EsuHudLayout.CurrentScale)") &&
               themeSource.Contains("EsuHudLayout.FontSize") &&
               !sessionSource.Contains("DimTextureFor(_viewMode)") &&
               !sessionSource.Contains("GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height)") &&
               themeSource.Contains("DecorationEditorViewMode") &&
               themeSource.Contains("FTD") == false,
            "Decoration Edit Mode has an ESU-owned native-style theme layer without copied FTD texture data or a full-screen dark overlay.");

        Assert(layoutSource.Contains("ReferenceWidth = 1920f") &&
               layoutSource.Contains("MinAutoScale = 0.72f") &&
               layoutSource.Contains("MinManualScale = 0.10f") &&
               layoutSource.Contains("MaxManualScale = 2f") &&
               layoutSource.Contains("MinEffectiveScale = 0.10f") &&
               layoutSource.Contains("MaxEffectiveScale = 2f") &&
               layoutSource.Contains("ToolbarNotificationBaseWidth = 250f") &&
               layoutSource.Contains("ToolbarRightControlsBaseWidth = 504f") &&
               layoutSource.Contains("PanelInnerRect") &&
               layoutSource.Contains("ToolbarRect(float baseHeight)") &&
               layoutSource.Contains("internal struct ToolbarBudget") &&
               layoutSource.Contains("internal struct CenteredToolbarFrame") &&
               layoutSource.Contains("CalculateToolbarBudget(float toolbarWidth, float scale)") &&
               layoutSource.Contains("CalculateCenteredToolbarFrame(Rect innerRect") &&
               layoutSource.Contains("TotalWidth =>") &&
               layoutSource.Contains("ToolbarLeftRailWidth") &&
               layoutSource.Contains("ToolbarNotificationWidth") &&
               layoutSource.Contains("ToolbarRightControlsWidth") &&
               layoutSource.Contains("ScaleForScreen") &&
               layoutSource.Contains("ClampPanel") &&
               layoutSource.Contains("ResizeGripRect") &&
               layoutSource.Contains("DrawStackDividerGrip") &&
               layoutSource.Contains("RequestReset") &&
               optionsSource.Contains("Automatic editor scaling") &&
               optionsSource.Contains("Editor scale {0:0.00}x") &&
               optionsSource.Contains("Reset ESU editor layout"),
            "ESU editor HUD layout has shared automatic scaling, centered toolbar frames, profile options, reset, panel clamping, and resize grip helpers.");
        Assert(ToolbarBudgetsStayInsideAvailableWidth(),
            "ESU toolbar rail budget always fits inside the available toolbar width across laptop, desktop, and 200% scale cases.");

        Assert(cursorTooltipSource.Contains("internal static class EsuCursorTooltip") &&
               cursorTooltipSource.Contains("HoverDelaySeconds = 1f") &&
               cursorTooltipSource.Contains("GUI.tooltip") &&
               cursorTooltipSource.Contains("RegisterLast") &&
               cursorTooltipSource.Contains("Register(Rect rect") &&
               cursorTooltipSource.Contains("CursorOffsetXBase") &&
               cursorTooltipSource.Contains("CursorOffsetYBase") &&
               cursorTooltipSource.Contains("Screen.width") &&
               cursorTooltipSource.Contains("Screen.height") &&
               cursorTooltipSource.Contains("Mathf.Clamp(rect.x") &&
               cursorTooltipSource.Contains("Mathf.Clamp(rect.y") &&
               cursorTooltipSource.Contains("EventType.ScrollWheel") &&
               cursorTooltipSource.Contains("current.type != EventType.Repaint") &&
               cursorTooltipSource.Contains("Input.GetMouseButton(0)") &&
               !cursorTooltipSource.Contains("InfoStore") &&
               !cursorTooltipSource.Contains("AdvLogger") &&
               sessionSource.Contains("EsuCursorTooltip.BeginFrame(Event.current.mousePosition, TooltipInputSuppressed())") &&
               sessionSource.Contains("EsuCursorTooltip.Draw()") &&
               smartBuildSessionSource.Contains("EsuCursorTooltip.BeginFrame(Event.current.mousePosition, TooltipInputSuppressed())") &&
               smartBuildSessionSource.Contains("EsuCursorTooltip.Draw()") &&
               smartBuildSessionSource.Contains("SmartGUILayoutButton") &&
               smartBuildSessionSource.Contains("SmartGUIButton") &&
               smartBuildSessionSource.Contains("EsuCursorTooltip.RegisterLast(content?.tooltip)") &&
               smartBuildSessionSource.Contains("EsuCursorTooltip.Register(rect, content?.tooltip)") &&
               sessionSource.Contains("Filter meshes by name or GUID.") &&
               sessionSource.Contains("Show the mesh palette as a virtualized 3D thumbnail grid.") &&
               sessionSource.Contains("Select this decoration.") &&
               sessionSource.Contains("Hide the inspector panel.") &&
               notificationSource.Contains("Open the ESU runtime log.") &&
               notificationSource.Contains("Show the full notification text.") &&
               smartBuildSessionSource.Contains("Drag to resize the Smart Builder panel.") &&
               smartBuildSessionSource.Contains("Previous Smart Builder material.") &&
               smartBuildSessionSource.Contains("Toggle occupied-cell handling.") &&
               smartBuildSessionSource.Contains("Show Smart Builder shapes as rotating item thumbnails."),
            "ESU cursor tooltips use a 1-second ESU-owned delayed overlay, clamp near the cursor, reset on editing input, and are wired through Deco, Surface, Smart Builder, notifications, and common panel controls.");

        Assert(sessionSource.Contains("DrawEditorShell") &&
               sessionSource.Contains("DrawTopToolbar") &&
               sessionSource.Contains("EsuHudLayout.PanelInnerRect(toolbarRect)") &&
               sessionSource.Contains("EsuHudLayout.PanelInnerRect(bottomRect)") &&
               sessionSource.Contains("EsuHudLayout.CalculateCenteredToolbarFrame(toolbarRect)") &&
               sessionSource.Contains("EsuHudLayout.ToolbarBudget budget = frame.Budget") &&
               sessionSource.Contains("GUILayout.BeginHorizontal(GUILayout.Width(budget.LeftRailWidth))") &&
               sessionSource.Contains("DrawAnchorMenu") &&
               sessionSource.Contains("AnchorMenuRect") &&
               sessionSource.Contains("_anchorMenuOpen") &&
               sessionSource.Contains("DrawOutliner") &&
               sessionSource.Contains("DrawInspector") &&
               sessionSource.Contains("DrawMeshPalette") &&
               sessionSource.Contains("DrawLeftPanelStack") &&
               sessionSource.Contains("DrawInspectorPanel") &&
               sessionSource.Contains("DrawOutlinerPanel") &&
               sessionSource.Contains("DrawAnchorPanel") &&
               sessionSource.Contains("DrawCompactIconHeader(\"Mesh Palette\", \"mesh\")") &&
               sessionSource.Contains("DrawCompactIconHeader(titleRect, \"Outliner\", \"outliner\")") &&
               sessionSource.Contains("DrawInspectorHeader(headerRect)") &&
               sessionSource.Contains("private void DrawInspectorHeader(Rect rect)") &&
               sessionSource.Contains("ShouldSkipLeftPanelDrag(current.mousePosition, meshRect)") &&
               sessionSource.Contains("InspectorHeaderRect(leftPanelRect, localToPanel: false)") &&
               sessionSource.Contains("InspectorHeaderRect(rect, localToPanel: true)") &&
               sessionSourceNormalized.Contains("current.type == EventType.MouseDown)\n                _draggingMeshPalette = false") &&
               sessionSource.Contains("resizing &&") &&
               sessionSource.Contains("!grip.Contains(current.mousePosition)") &&
               sessionSource.Contains("DrawCompactIconHeader(titleRect, \"Inspector\", \"settings\")") &&
               !sessionSource.Contains("new GUIContent(\" Mesh Palette\", DecorationEditorIconCatalog.Get(\"mesh\"))") &&
               !sessionSource.Contains("new GUIContent(\" Outliner\", DecorationEditorIconCatalog.Get(\"outliner\"))") &&
               !sessionSource.Contains("new GUIContent(\" Inspector\", DecorationEditorIconCatalog.Get(\"settings\"))") &&
               !sessionSource.Contains("GUILayout.Label(\"Inspector\", DecorationEditorTheme.SubHeader") &&
               sessionSource.Contains("IsLeftPanelStackVisible") &&
               sessionSource.Contains("IsRightPanelStackVisible") &&
               sessionSource.Contains("DrawBottomPanel") &&
               sessionSource.Contains("DrawBottomPanel(bottomInner.width, bottomInner.height)") &&
               sessionSource.Contains("private void DrawBottomPanel(float width, float height)") &&
               sessionSource.Contains("private void DrawBottomHeader(Rect rect)") &&
               sessionSource.Contains("DrawBottomHeader(new Rect") &&
               !sessionSource.Contains("DrawPaletteDetails") &&
               sessionSource.Contains("SplitVerticalStack") &&
               sessionSource.Contains("LeftStackDefaultBottomRatio = 0.34f") &&
               sessionSource.Contains("RightStackDefaultBottomRatio = 0.38f") &&
               sessionSource.Contains("s_leftStackBottomRatio") &&
               sessionSource.Contains("s_rightStackBottomRatio") &&
               sessionSource.Contains("StackDividerKind") &&
               sessionSource.Contains("HandleStackDividerDrag") &&
               sessionSource.Contains("DrawStackDividerGrip") &&
               sessionSource.Contains("ClampStackBottomRatioFromHeight") &&
               sessionSource.Contains("startBottomHeight - deltaY") &&
               sessionSource.Contains("DrawAnchorContext(inner.height)") &&
               sessionSource.Contains("DrawCompactAnchorHeader") &&
               sessionSource.Contains("GUILayoutUtility.GetRect(1f, EsuHudLayout.Scale(22f)") &&
               sessionSource.Contains("EsuHudLayout.Scale(16f)") &&
               !sessionSource.Contains("new GUIContent(\" Selected anchor\", DecorationEditorIconCatalog.Get(\"anchor\"))") &&
               sessionSource.Contains("float countHeight = Mathf.Min(EsuHudLayout.Scale(18f)") &&
               sessionSource.Contains("Rect countRect") &&
               sessionSource.Contains("GUI.Label(countRect, MeshCountText(rows.Count)") &&
               sessionSource.Contains("total meshes") &&
               sessionSource.Contains("search: ") &&
               sessionSource.Contains("s_showInspectorPanel") &&
               sessionSource.Contains("s_showAnchorPanel") &&
               sessionSource.Contains("GUILayout.BeginArea(contentRect)") &&
               sessionSource.Contains("DrawInspector(contentRect.height)") &&
               sessionSource.Contains("GUI.Button(hideRect, new GUIContent(\"Hide\", \"Hide the inspector panel.\"), DecorationEditorTheme.Button)") &&
               sessionSource.Contains("_showInspectorPanel = false") &&
               sessionSource.Contains("_showOutlinerPanel = false") &&
               sessionSource.Contains("_showAnchorPanel = false") &&
               sessionSource.Contains("pinsWidth + refreshWidth + hideWidth") &&
               sessionSource.Contains("DrawCompactIconHeader(titleRect, \"Selected anchor\", \"anchor\")") &&
               sessionSource.Contains("PanelToggle(\"settings\", \"Insp\", ref _showInspectorPanel") &&
               sessionSource.Contains("PanelToggle(\"anchor\", \"Anch\", ref _showAnchorPanel") &&
               !sessionSource.Contains("InspectorPanelButton") &&
               !sessionSource.Contains("AnchorPanelButton") &&
               sessionSource.Contains("Rect listRect") &&
               sessionSource.Contains("GUI.BeginGroup(listRect)") &&
               sessionSource.Contains("if (listRect.height > 1f)") &&
               sessionSource.Contains("Mathf.Max(0f, listBottom - countRect.yMax)") &&
               sessionSource.Contains("GUILayout.Height(SurfaceDraftListHeight(contentRect.height))") &&
               sessionSource.Contains("SurfaceSettingsShelfHeight(inner.height, actionHeight, gap)") &&
               !sessionSource.Contains("float listHeight = Mathf.Max(EsuHudLayout.Scale(120f)") &&
               !sessionSource.Contains("float listHeight = Mathf.Max(EsuHudLayout.Scale(110f)") &&
               !sessionSource.Contains("float scrollHeight = Mathf.Max(EsuHudLayout.Scale(72f)") &&
               !sessionSource.Contains("float scrollHeight = Mathf.Max(EsuHudLayout.Scale(58f)") &&
               sessionSource.Contains("alwaysShowHorizontal: false") &&
               sessionSource.Contains("CompactText") &&
               sessionSource.Contains("480f, 1050f") &&
               sessionSource.Contains("s_rightPanelRect") &&
               sessionSource.Contains("ToolbarBottomLimit") &&
               sessionSource.Contains("MaxMeshPaletteHeight") &&
               sessionSource.Contains("HandlePanelResize") &&
               sessionSource.Contains("EsuHudLayout.DrawResizeGrip(meshRect") &&
               sessionSource.Contains("EsuHudLayout.DrawResizeGrip(rightRect") &&
               !themeSource.Contains("DecorationEditorPaletteTab") &&
               !sessionSource.Contains("PaletteDetailTab") &&
               !sessionSource.Contains("DecorationEditorPaletteTab.Anchor") &&
               !sessionSource.Contains("DecorationEditorBottomTab") &&
               sessionSource.Contains("PanelToggle(\"mesh\"") &&
               sessionSource.Contains("PanelToggle(\"outliner\"") &&
               sessionSource.Contains("PanelToggle(\"settings\"") &&
               sessionSource.Contains("PanelToggle(\"anchor\"") &&
               sessionSource.Contains("PanelToggle(\"build\", \"Surf\", ref _showSurfacePanel") &&
               sessionSource.Contains("PanelToggle(\"settings\", \"Tools\", ref _showSurfaceToolsPanel") &&
                 sessionSource.Contains("EsuHudNotifications.DrawToolbarSlot(") &&
                 sessionSource.Contains("toolbarScreenOrigin") &&
               sessionSource.Contains("CurrentModeToolbarLabel") &&
               sessionSource.Contains("ESU mode: Surface Builder.") &&
               sessionSource.Contains("ESU mode: Decoration Edit.") &&
               sessionSource.Contains("GUILayout.BeginHorizontal(GUILayout.Width(budget.RightControlsWidth))") &&
               sessionSource.Contains("DrawOutliner(inner.width, inner.height)") &&
               sessionSource.Contains("private void DrawOutliner(float width, float height)") &&
               sessionSource.Contains("GUILayoutUtility.GetRect(1f, rowHeight") &&
               sessionSource.Contains("GUI.Button(rowRect, GUIContent.none, style)") &&
               sessionSource.Contains("DrawOutlinerRow(rows, i)") &&
               sessionSource.Contains("SelectOutlinerDecorationRow") &&
               sessionSource.Contains("IsShiftHeld()") &&
               sessionSource.Contains("bool control = IsControlHeld();") &&
               sessionSource.Contains("SelectOutlinerDecorationRange") &&
               sessionSource.Contains("ToggleOutlinerDecorationSelection") &&
               sessionSource.Contains("_outlinerRangeAnchor") &&
               sessionSource.Contains("ReferenceEquals(rows[anchorIndex].Construct, row.Construct)") &&
               sessionSource.Contains("!ReferenceEquals(row.Construct, clicked.Construct)") &&
               sessionSource.Contains("OutlinerRowTextStyle") &&
               sessionSource.Contains("CompactHeaderTextStyle") &&
               smartBuildSessionSource.Contains("private const float ToolbarHeight = 54f") &&
               smartBuildSessionSource.Contains("_toolbarRect = EsuHudLayout.ToolbarRect(ToolbarHeight)") &&
               smartBuildSessionSource.Contains("EsuHudLayout.CalculateCenteredToolbarFrame(inner)") &&
                smartBuildSessionSource.Contains("EsuHudNotifications.DrawToolbarSlot(") &&
                smartBuildSessionSource.Contains("new Vector2(_toolbarRect.x + frame.Rect.x") &&
                smartBuildSessionSource.Contains("ESU mode: Smart Builder.") &&
                smartBuildSessionSource.Contains("GUILayout.BeginHorizontal(GUILayout.Width(budget.RightControlsWidth))") &&
                smartBuildSessionSource.Contains("ToolbarPanelToggle(\"settings\", \"Info\", ref _showLeftPanel") &&
                smartBuildSessionSource.Contains("ToolbarPanelToggle(\"build\", \"Shapes\", ref _showRightPanel") &&
                smartBuildSessionSource.Contains("DrawSmartSectionHeader(\"Scene\", ref _showSceneSection") &&
                !smartBuildSessionSource.Contains("DrawPieceActionToolbar(budget.RightControlsWidth)") &&
                !smartBuildSessionSource.Contains("private void ShapeQuickButton(") &&
                !smartBuildSessionSource.Contains("private void SlopeQuickButton(int length)") &&
                smartBuildSessionSource.Contains("private void YawQuickButton()") &&
                smartBuildSessionSource.Contains("private void FlipQuickButton()") &&
                smartBuildSessionSource.Contains("ShouldShowPieceActionButtons(rightControlsWidth)") &&
               !smartBuildSessionSource.Contains("ToolbarReservedSlot("),
            "Decoration Edit Mode renders the native shell with a first-slot mode switch, compact toolbar, stable shared notification/right-control rail, independent Mesh/Inspector/Outliner/Anchor panels, bottom status strip, and panel toggles.");

        Assert(!sessionSource.Contains("Mesh/Add") &&
               !sessionSource.Contains("Add here") &&
               !sessionSource.Contains("Add deco here") &&
               !sessionSource.Contains("AddDecorationHere") &&
               !sessionSource.Contains("DrawSelectionControls") &&
               !sessionSource.Contains("ToolButton(DecorationEditorTool.Mesh"),
            "Decoration Edit Mode removed the dedicated Mesh/Add toolbar button and Add Here button; mesh placement starts from palette selection.");

        Assert(sessionSource.Contains("DrawViewModeMenu") &&
               sessionSource.Contains("DrawAnchorMenu()") &&
               sessionSource.Contains("SelectViewMode") &&
               sessionSource.Contains("toolbarRect.xMax - width") &&
               sessionSource.Contains("ViewModeButtonsPerRow") &&
               sessionSource.Contains("ViewModePreferredWidth") &&
               sessionSource.Contains("ViewModeButtonRect") &&
               sessionSource.Contains("Screen.width - margin * 2f") &&
               sessionSource.Contains("Mathf.CeilToInt(ViewModeMenuButtonCount / (float)ViewModeButtonsPerRow(width))") &&
               sessionSourceNormalized.Contains("GUI.Button(\n                    rect,") &&
               !sessionSource.Contains("GUILayout.Width(54f)") &&
               sessionSource.Contains("private static bool s_anchorMenuOpen") &&
               sessionSource.Contains("private bool _anchorMenuOpen = s_anchorMenuOpen") &&
               sessionSource.Contains("s_anchorMenuOpen = _anchorMenuOpen") &&
               sessionSource.Contains("AnchorMenuButton") &&
               sessionSource.Contains("Anchor settings are in the bottom status panel.") &&
               sessionSource.Contains("DrawBottomAnchorSettingsButton") &&
               sessionSource.Contains("_anchorSettingsButtonScreenRect") &&
               sessionSourceNormalized.Contains("SetActiveTool(DecorationEditorTool.Anchor);\n                _anchorMenuOpen = false") &&
               sessionSource.Contains("_anchorMenuOpen = !_anchorMenuOpen") &&
               !toolButtonSource.Contains("DecorationEditorTool.Anchor") &&
               !toolButtonSource.Contains("_anchorMenuOpen = !_anchorMenuOpen") &&
               toolButtonSource.Contains("SetActiveTool(tool);") &&
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

        int viewMenuDraw = drawEditorShellSource.LastIndexOf("DrawViewModeMenu(toolbarRect)", StringComparison.Ordinal);
        Assert(viewMenuDraw > drawEditorShellSource.IndexOf("DrawRightPanel(rightRect)", StringComparison.Ordinal) &&
               viewMenuDraw > drawEditorShellSource.IndexOf("DrawAnchorMenu()", StringComparison.Ordinal) &&
               viewMenuDraw > drawEditorShellSource.IndexOf("DrawMeshPreviewCard()", StringComparison.Ordinal) &&
               viewMenuDraw > drawEditorShellSource.IndexOf("DrawSurfaceContextMenu()", StringComparison.Ordinal) &&
               viewMenuDraw > drawEditorShellSource.IndexOf("EsuConsoleWindow.Draw()", StringComparison.Ordinal) &&
               viewMenuDraw < drawEditorShellSource.IndexOf("EsuCursorTooltip.Draw()", StringComparison.Ordinal),
            "Decoration Edit Mode renders the View mode menu in the foreground overlay pass above panels and console.");

        int decorationRightClick = handleSceneInputSource.IndexOf(
            "if (Input.GetMouseButtonDown(1))",
            handleSceneInputSource.IndexOf("if (_boxSelecting)", StringComparison.Ordinal),
            StringComparison.Ordinal);
        int decorationLeftClick = handleSceneInputSource.IndexOf(
            "if (!Input.GetMouseButtonDown(0))",
            decorationRightClick,
            StringComparison.Ordinal);
        Assert(decorationRightClick >= 0 &&
               decorationLeftClick > decorationRightClick &&
               handleSceneInputSource.IndexOf(
                   "TryOpenDecorationContextMenu()",
                   decorationRightClick,
                   StringComparison.Ordinal) > decorationRightClick &&
               drawEditorShellSource.IndexOf("DrawDecorationContextMenu()", StringComparison.Ordinal) >
               drawEditorShellSource.IndexOf("DrawSurfaceContextMenu()", StringComparison.Ordinal) &&
               sessionSource.Contains("IsDecorationContextMenuAt(mouse)") &&
               sessionSource.Contains("DuplicateSelectedDecoration") &&
               sessionSource.Contains("DeleteSelectedDecoration") &&
               sessionSource.Contains("RestoreDeletedDecorationsForCancel") &&
               sessionSource.Contains("TryUndoDeletedDecoration") &&
               sessionSource.Contains("TryRedoDeletedDecoration") &&
               sessionSource.Contains("_deletedDecorations.Count > 0") &&
               historySource.Contains("DecorationDeleteCommand") &&
               historySource.Contains("DecorationDeleteBatchCommand"),
            "Decoration Edit Mode right-click opens a foreground decoration context menu with duplicate/delete actions and undo/cancel-safe delete tracking.");

        string drawDecorationContextMenuSource = ExtractMethodSource(sessionSource, "DrawDecorationContextMenu").Replace("\r\n", "\n");
        string drawDecorationContextButtonSource = ExtractMethodSource(sessionSource, "DrawDecorationContextButton").Replace("\r\n", "\n");
        string consumeDecorationContextEventSource = ExtractMethodSource(sessionSource, "ShouldConsumeDecorationContextEvent").Replace("\r\n", "\n");
        Assert(drawDecorationContextMenuSource.Contains("DecorationContextAction action = DecorationContextAction.None") &&
               drawDecorationContextMenuSource.Contains("DrawDecorationContextButton") &&
               drawDecorationContextButtonSource.Contains("GUI.Button") &&
               drawDecorationContextMenuSource.Contains("ShouldConsumeDecorationContextEvent(current)") &&
               drawDecorationContextMenuSource.Contains("ExecuteDecorationContextAction(action)") &&
               !drawDecorationContextMenuSource.Contains("GUILayout.BeginArea") &&
               !drawDecorationContextMenuSource.Contains("GUILayout.Button") &&
               !drawDecorationContextButtonSource.Contains("GUILayout.BeginArea") &&
               !drawDecorationContextButtonSource.Contains("GUILayout.Button") &&
               consumeDecorationContextEventSource.Contains("EventType.MouseDown") &&
               consumeDecorationContextEventSource.Contains("EventType.MouseUp") &&
               consumeDecorationContextEventSource.Contains("EventType.MouseDrag") &&
               consumeDecorationContextEventSource.Contains("EventType.ScrollWheel") &&
               consumeDecorationContextEventSource.Contains("EventType.ContextClick") &&
               !consumeDecorationContextEventSource.Contains("EventType.Layout") &&
               !consumeDecorationContextEventSource.Contains("EventType.Repaint") &&
               !consumeDecorationContextEventSource.Contains("EventType.MouseMove"),
            "Decoration Edit Mode cursor menu uses fixed immediate-mode controls and consumes only real input events, avoiding hover-time IMGUI layout corruption.");

        Assert(!sessionSource.Contains("MaxSearchRows") &&
               !sessionSource.Contains("Showing first 70") &&
               sessionSource.Contains("MeshPreviewGridMinCardWidth = 112f") &&
               sessionSource.Contains("MeshPreviewGridMinCardHeight = 86f") &&
               sessionSource.Contains("MeshPreviewGridMaxCardHeight = 124f") &&
               sessionSource.Contains("MeshPreviewGridCardAspect = 0.74f") &&
               drawMeshPreviewGridSource.Contains("MeshPreviewGridLayoutFor(viewportWidth)") &&
               drawMeshPreviewGridSource.Contains("layout.Columns") &&
               drawMeshPreviewGridSource.Contains("layout.CardWidth") &&
               drawMeshPreviewGridSource.Contains("layout.PreviewHeight") &&
               drawMeshPreviewGridSource.Contains("GUILayoutUtility.GetRect") &&
               meshPreviewGridLayoutSource.Contains("cardWidth * MeshPreviewGridCardAspect") &&
               meshPreviewGridLayoutSource.Contains("viewportWidth - outerPadding * 2f") &&
               meshPreviewGridLayoutSource.Contains("(available - gap * Mathf.Max(0, columns - 1)) / columns") &&
               sessionSource.Contains("DrawMeshListRows(rows, listRect.height, mouseInListViewport)") &&
               sessionSource.Contains("DrawMeshPreviewGrid(rows, listRect.width, listRect.height, mouseInListViewport)") &&
               drawMeshPreviewGridSource.Contains("Mathf.CeilToInt((_meshScroll.y + viewportHeight) / layout.RowHeight)") &&
               sessionSource.Contains("EventType.Repaint") &&
               sessionSource.Contains("bool canRenderVisiblePreview") &&
               sessionSource.Contains("? _previewRenderer?.GetPreview(entry, previewPixels, _previewSpin)") &&
               sessionSource.Contains(": _previewRenderer?.GetCachedPreview(entry, previewPixels)") &&
               previewSource.Contains("GetCachedPreview") &&
               !sessionSource.Contains("MeshPreviewGridRowHeight =") &&
               !sessionSource.Contains("MeshPreviewGridColumns =") &&
               !sessionSource.Contains("MeshPreviewGridCardWidth =") &&
               !sessionSource.Contains("MeshPreviewGridCardHeight =") &&
               !sessionSource.Contains("FloorToInt(_meshScroll.y / MeshPreviewGridRowHeight) - 2") &&
               !sessionSource.Contains("visibleRows = Mathf.CeilToInt(viewportHeight / MeshPreviewGridRowHeight) + 4"),
            "Decoration Edit Mode mesh palette virtualizes the full filtered mesh catalog and rotates every visible adaptive-column 3D grid thumbnail.");

        Assert(sessionSource.Contains("DrawMeshPreviewCard") &&
               previewSource.Contains("RenderTexture") &&
               previewSource.Contains("TryGetUnityMesh") &&
               previewSource.Contains("BuildGeneratedMesh") &&
               previewSource.Contains("SafeMeshBase") &&
               sessionSource.Contains("_hoveredMesh = null") &&
               sessionSource.Contains("_hoveredMeshHint = null") &&
               sessionSource.Contains("DrawGeneratorMeshRows") &&
               sessionSource.Contains("_hoveredMeshHint = \"Click: use for generated Path/Circle decorations.\"") &&
               sessionSource.Contains("bool mouseInListViewport = listRect.Contains(Event.current.mousePosition)") &&
               sessionSource.Contains("mouseInListViewport && row.Contains(Event.current.mousePosition)") &&
               drawMeshPreviewGridSource.Contains("mouseInListViewport") &&
               drawMeshPreviewGridSource.Contains("card.Contains(Event.current.mousePosition)") &&
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
               sessionSource.Contains("\"ESU Decoration Symmetry Placement Ghost\"") &&
               sessionSource.Contains("UpdateSymmetryPlacementGhosts") &&
               sessionSource.Contains("DrawSymmetryPlacementGhostHints") &&
               ExtractMethodSource(sessionSource, "DrawPlacementGhost").Contains("DrawSymmetryPlacementGhostHints(color)") &&
               sessionSource.Contains("HideSymmetryPlacementGhosts") &&
               sessionSource.Contains("ShadowCastingMode.Off") &&
               sessionSource.Contains("DestroyPlacementGhost") &&
               previewSource.Contains("internal Mesh GetMesh") &&
               !sessionSource.Contains("GetHoveredBlockPosition") &&
               pointerProbeSource.Contains("ScreenPointToRay") &&
               pointerProbeSource.Contains("Physics.RaycastAll") &&
               pointerProbeSource.Contains("Physics.RaycastAll(ray, limits.EndDistance)") &&
               pointerProbeSource.Contains("physicsHit.distance < limits.StartDistance") &&
               pointerProbeSource.Contains("limits.EndDistance - startDistance") &&
               pointerProbeSource.Contains("MeshPlacementCraftBubbleRadius = 100f") &&
               pointerProbeSource.Contains("internal static ProbeOptions MeshPlacement") &&
               pointerProbeSource.Contains("LimitToCraftBubble") &&
               pointerProbeSource.Contains("TryGetCraftBubbleRayLimits") &&
               sessionSource.Contains("TryProbe(DecorationPointerProbe.ProbeOptions.MeshPlacement") &&
               pointerProbeSource.Contains("Array.Sort(physicsHits") &&
               pointerProbeSource.Contains("reportedWorld") &&
               pointerProbeSource.Contains("sampleWorld") &&
               pointerProbeSource.Contains("RefineBoundary") &&
               pointerProbeSource.Contains("AnchorResolutionMode.Strict") &&
               pointerProbeSource.Contains("AnchorResolutionMode.Nearest") &&
               pointerProbeSource.Contains("new DecorationPointerHit(construct, anchor, reportedLocal, reportedWorld") &&
               !pointerProbeSource.Contains("new DecorationPointerHit(construct, anchor, local, world") &&
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
               sessionSource.Contains("ToggleAnchorFollow") &&
               sessionSource.Contains("DrawBottomAnchorFollowToggle") &&
               sessionSource.Contains("Anchor follow: on") &&
               sessionSource.Contains("Anchor follow range") &&
               sessionSource.Contains("DrawSymmetryAnchorGizmoHints") &&
               ExtractMethodSource(sessionSource, "DrawSymmetryAnchorGizmoHints").Contains("variant.Mirror(_anchorPreviewTether)") &&
               sessionSource.Contains("AnchorMenuPreferredWidth") &&
               sessionSource.Contains("AnchorMenuNeedsPresetWrap") &&
               sessionSource.Contains("DrawAnchorFollowDistanceButton") &&
               sessionSource.Contains("Mathf.Clamp(AnchorMenuPreferredWidth(), minWidth, maxWidth)") &&
               sessionSource.Contains("Screen.width - margin * 2f") &&
               sessionSource.Contains("GUI.TextField") &&
               sessionSource.Contains("if (wrapsPresets)") &&
               sessionSource.Contains("DrawAnchorFollowDistanceButton(new Rect(x, presetY") &&
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
               sessionSource.Contains("DrawAnchorPanel") &&
               sessionSource.Contains("PanelToggle(\"anchor\", \"Anch\", ref _showAnchorPanel") &&
               sessionSource.Contains("SelectedAnchorDecorations") &&
               sessionSource.Contains("DrawSelectedAnchorConnections") &&
               sessionSource.Contains("_anchorRangeAnchor") &&
               sessionSource.Contains("SelectAnchorDecorationRow(rows, index, decoration)") &&
               sessionSource.Contains("TryFindAnchorDecorationIndex(rows, _anchorRangeAnchor, out int anchorIndex)") &&
               sessionSource.Contains("SelectAnchorDecorationRange(rows, anchorIndex, clickedIndex, clicked, additive: control)") &&
               sessionSource.Contains("ToggleAnchorDecorationSelection(clicked)") &&
               sessionSource.Contains("Ctrl toggles one row; Shift selects a range.") &&
               sessionSource.Contains("_tool != DecorationEditorTool.Anchor"),
            "Decoration Edit Mode outliner defaults to construct-grouped decoration rows with optional tether pins, collapsible constructs, independent selected-anchor context, anchor connection lines, and anchor-mode click selection.");

        Assert(historySource.Contains("DecorationEditHistory") &&
               historySource.Contains("DecorationSnapshotCommand") &&
               historySource.Contains("DecorationSnapshotBatchCommand") &&
               historySource.Contains("DecorationCreateCommand") &&
               historySource.Contains("DecorationDeleteCommand") &&
               historySource.Contains("DecorationCreateBatchCommand") &&
               transactionSource.Contains("DecorationEditTransactionSet") &&
               transactionSource.Contains("MarkCreated") &&
               transactionSource.Contains("TrackEdit") &&
               sessionSource.Contains("TryRestoreHistorySnapshots") &&
               sessionSource.Contains("RollbackFailedDecorationDelete(deletedPlans)") &&
               ExtractMethodSource(sessionSource, "RollbackFailedDecorationDelete").Contains("TryUndoDeletedDecoration(") &&
               sessionSource.Contains("already deleted mirrors were restored") &&
               sessionSource.Contains("UndoEdit") &&
               sessionSource.Contains("RedoEdit") &&
               sessionSource.Contains("Ctrl+Shift+Z") &&
               historySource.Contains("IDecorationEditCommand command = _undo.Peek();") &&
               historySource.Contains("IDecorationEditCommand command = _redo.Peek();") &&
               historySourceNormalized.Contains("if (!command.Undo(session))\n                return false;\n\n            _undo.Pop();") &&
               historySourceNormalized.Contains("if (!command.Redo(session))\n                return false;\n\n            _redo.Pop();"),
            "Decoration Edit Mode has session-scoped undo/redo history, preserves failed history entries, and transaction rollback for snapshots and created decorations.");

        Assert(symmetrySource.Contains("internal static class EsuSymmetry") &&
               symmetrySource.Contains("PendingAxis") &&
               symmetrySource.Contains("TryPlacePending") &&
               symmetrySource.Contains("MirrorCell") &&
               symmetrySource.Contains("MirrorVector") &&
               symmetrySource.Contains("MirrorDirection") &&
               symmetrySource.Contains("MirrorScale") &&
               symmetrySource.Contains("MirrorEuler") &&
               symmetrySource.Contains("Quaternion.LookRotation") &&
               symmetrySource.Contains("AxisSign(DecorationEditAxis.X)") &&
               sessionSource.Contains("SymmetryButton(DecorationEditAxis.X)") &&
               sessionSource.Contains("SymmetryButton(DecorationEditAxis.Y)") &&
               sessionSource.Contains("SymmetryButton(DecorationEditAxis.Z)") &&
               sessionSource.Contains("TryBuildDecorationPlacementPlans") &&
               sessionSource.Contains("DecorationPlacementTemplate") &&
               sessionSource.Contains("plan.Scaling") &&
               sessionSource.Contains("plan.Orientation") &&
               sessionSource.Contains("TryBuildDecorationDeletePlans") &&
               historySource.Contains("Delete mirrored decorations") &&
               sessionSource.Contains("trackTargetEdits: false") &&
               sessionSource.Contains("DrawSymmetryAnchorGizmoHints") &&
               sessionSource.Contains("DrawSymmetryPlacementGhostHints") &&
               sessionSource.Contains("UpdateSymmetryPlacementGhosts") &&
               sessionSource.Contains("ApplyPlacementGhostTransform") &&
               sessionSource.Contains("Symmetry placement rejected because a mirrored tether block is missing") &&
               sessionSource.Contains("DecorationCreateBatchCommand") &&
               sessionSource.Contains("DecorationDeleteBatchCommand") &&
               sessionSource.Contains("BeginSymmetryFollow") &&
               sessionSource.Contains("TryFindSymmetryCounterpart") &&
               sessionSource.Contains("SelectedAnchorDecorations(expectedAnchor)") &&
               sessionSource.Contains("GetDecorationLocalCenter(decoration)") &&
               sessionSource.Contains("TryApplySymmetryFollow") &&
               sessionSource.Contains("TryApplyDecorationSymmetryTransform") &&
               sessionSource.Contains("RecordSymmetrySnapshotEdit") &&
               sessionSource.Contains("RecordSnapshotEdit(\"Move decoration\", before, symmetryFollow)") &&
               sessionSource.Contains("RecordSnapshotEdit(\"Rotate decoration\", before, symmetryFollow)") &&
               sessionSource.Contains("RecordSnapshotEdit(\"Scale decoration\", before, symmetryFollow)") &&
               sessionSource.Contains("RecordSnapshotEdit(\"Set position\", before, symmetryFollow)") &&
               sessionSource.Contains("RecordSnapshotEdit(\"Set rotation\", before, symmetryFollow)") &&
               sessionSource.Contains("RecordSnapshotEdit(\"Set scale\", before, symmetryFollow)") &&
               sessionSource.Contains("ApplyAnchorShift(shift, before, symmetryFollow)") &&
               sessionSource.Contains("Symmetry follow skipped: ") &&
               sessionSource.Contains("Symmetry follow rejected: ") &&
               sessionSource.Contains("mirrored tether block is missing") &&
               sessionSource.Contains("mirrored offset would exceed +/-10") &&
               decorationBehaviourSource.Contains("EsuSymmetry.Clear"),
            "Decoration Edit Mode exposes shared XYZ symmetry planes, validates mirrored placement batches, and live-follows matched existing mirrored decorations atomically.");

        Assert(tryApplySymmetryFollowSource.Contains("targetScale,\n                    _selected.Color.Us)") &&
               tryApplySymmetryFollowSource.Contains("application.Color") &&
               tryApplyDecorationSymmetryTransformSource.Contains("int color") &&
               tryApplyDecorationSymmetryTransformSource.Contains("decoration.Color.Us = color") &&
               sessionSource.Contains("internal int Color { get; }") &&
               setSelectedColorSource.Contains("BeginSymmetryFollow(before, reportSkipped: true)") &&
               setSelectedColorSource.Contains("TryApplySymmetryFollow(symmetryFollow, reportInvalid: true)") &&
               setSelectedColorSource.Contains("RecordSnapshotEdit(\"Set color\", before, symmetryFollow)"),
            "Decoration Edit Mode symmetry follow carries selected color and applies it to mirrored counterparts.");

        Assert(sessionSource.Contains("HashSet<Decoration> _selection") &&
                sessionSource.Contains("MaxOutlinerDrawRows") &&
                sessionSource.Contains("DecorationOutlinerRowKind") &&
                sessionSource.Contains("GroupBy(decoration => FormatTether") &&
                !sessionSource.Contains("before changing selection"),
            "Decoration Edit Mode stores selection in a future-multi-select shape, builds a virtualized outliner, and allows selection changes while dirty.");

        Assert(themeSource.Contains("DecorationSelectionMode") &&
               themeSource.Contains("Single") &&
               themeSource.Contains("Box") &&
               !sessionSource.Contains("DrawSelectionModeMenu") &&
               sessionSource.Contains("DrawBottomSelectionControls(slots.SelectControls)") &&
               sessionSource.Contains("private void DrawBottomSelectionControls(Rect rect)") &&
               sessionSource.Contains("SetActiveTool(DecorationEditorTool.Select);") &&
               sessionSource.Contains("DecorationSelectionMode.Single") &&
               sessionSource.Contains("DecorationSelectionMode.Box") &&
               sessionSource.Contains("_selectionXray") &&
               sessionSource.Contains("BeginBoxSelection") &&
               sessionSource.Contains("CommitBoxSelectionDrag") &&
               sessionSource.Contains("BoxSelectionRect") &&
               sessionSource.Contains("DrawBoxSelectionMarquee") &&
               sessionSource.Contains("ToolButton(DecorationEditorTool.Select, SelectToolIcon(), SelectToolLabel()") &&
               sessionSource.Contains("private string SelectToolLabel()") &&
               sessionSource.Contains("private string SelectToolIcon()") &&
               sessionSource.Contains("private void ToggleSelectionMode()") &&
               sessionSource.Contains("tool == DecorationEditorTool.Select && _tool == DecorationEditorTool.Select") &&
               sessionSource.Contains("rect.width < BoxSelectionClickThresholdPixels") &&
               sessionSource.Contains("SelectNearest(_boxSelectStartMouse)") &&
               sessionSource.Contains("private void SelectNearest(Vector2 mouse, bool additive = false)") &&
               sessionSource.Contains("TryPromoteSelectedDecorationInMultiSelection(best)") &&
               ExtractMethodSource(sessionSource, "TryPromoteSelectedDecorationInMultiSelection").Contains("_selection.Contains(hit.Decoration)") &&
               ExtractMethodSource(sessionSource, "TryPromoteSelectedDecorationInMultiSelection").Contains("SetPrimarySelection(hit.Decoration, hit.Construct)") &&
               sessionSource.Contains("AddNearestToSelection(best)") &&
               sessionSource.Contains("additive: _selectionMode == DecorationSelectionMode.Single && IsShiftHeld()") &&
               sessionSource.Contains("private void AddNearestToSelection(DecorationHit hit)") &&
               sessionSource.Contains("Selection moved to another construct.") &&
               sessionSource.Contains("Hold Shift and click to add more decorations to the current selection.") &&
               sessionSource.Contains("EnumerateBoxSelectionHits(rect, xray, refresh: false)") &&
               sessionSource.Contains("EnumerateBoxSelectionHits(rect, xray, refresh: true)") &&
               sessionSource.Contains("BoxSelectionPrimaryConstruct") &&
               sessionSource.Contains("ReferenceEquals(hit.Construct, primaryConstruct)") &&
               sessionSource.Contains("if (!xray && !IsDecorationHitVisible(hit, primaryConstruct))") &&
               sessionSource.Contains("CraftBlockOccludesDecorationRay") &&
               sessionSource.Contains("CraftBlockSamplingOccludesDecorationRay") &&
               sessionSource.Contains("OccludingCraftBlockAtWorld") &&
               sessionSource.Contains("Physics.RaycastAll(ray, rayDistance)") &&
               sessionSource.Contains("SameTether(cell, hit.Decoration.TetherPoint.Us)") &&
               sessionSource.Contains("HasBlock(construct, cell)") &&
               sessionSource.Contains("SetPrimarySelection(primary.Decoration, primary.Construct)") &&
               sessionSource.Contains("DrawSelectedSetHints") &&
               sessionSource.Contains("TryGetMultiSelectionPivot") &&
               sessionSource.Contains("TryGetMultiSelectionBoundsPivot") &&
               sessionSource.Contains("TryGetMultiSelectionBounds") &&
               sessionSource.Contains("DecorationGroupPivotMode") &&
               sessionSource.Contains("DecorationGroupPivotMode.BoundsCenter") &&
               sessionSource.Contains("DecorationGroupPivotMode.AverageCenter") &&
               sessionSource.Contains("DecorationGroupPivotMode.SelectedDecoration") &&
               sessionSource.Contains("DecorationGroupPivotMode.SelectedAnchor") &&
               sessionSource.Contains("CycleGroupPivotMode") &&
               sessionSource.Contains("GroupPivotModeLabel") &&
               sessionSource.Contains("\"Pivot: \" + GroupPivotModeLabel(_groupPivotMode)") &&
               ExtractMethodSource(sessionSource, "TryGetMultiSelectionBoundsPivot").Contains("(min + max) * 0.5f") &&
               ExtractMethodSource(sessionSource, "TryCaptureMultiTransformStart").Contains("ResolveGroupPivotFromCapturedSelection") &&
               !ExtractMethodSource(sessionSource, "TryGetMultiSelectionBoundsPivot").Contains("sum / decorations.Count") &&
               sessionSource.Contains("TryGetMultiSelectionAveragePivot") &&
               sessionSource.Contains("TryResolveGroupPivotFromLiveSelection") &&
               sessionSource.Contains("ResolveGroupPivotFromCapturedSelection") &&
               sessionSource.Contains("TryGetSelectedDecorationIndex") &&
               ExtractMethodSource(sessionSource, "ResolveGroupPivotFromCapturedSelection").Contains("sum / centers.Length") &&
               ExtractMethodSource(sessionSource, "ResolveGroupPivotFromCapturedSelection").Contains("return centers[selectedIndex]") &&
               ExtractMethodSource(sessionSource, "ResolveGroupPivotFromCapturedSelection").Contains("return ToVector3(before[selectedIndex].TetherPoint)") &&
               sessionSource.Contains("TryCaptureMultiTransformStart") &&
               sessionSource.Contains("TryPickGroupMoveHandle") &&
               sessionSource.Contains("TryPickGroupRotateRing") &&
               sessionSource.Contains("TryPickGroupUniformScaleHandle") &&
               sessionSource.Contains("TryApplyMultiMove") &&
               sessionSource.Contains("TryApplyMultiRotate") &&
               sessionSource.Contains("TryApplyMultiScale") &&
               sessionSource.Contains("TryApplyMultiUniformScale") &&
               sessionSource.Contains("UniformGroupScaleFactorFromMouseDelta") &&
               sessionSource.Contains("_scaleDragUniformGroup") &&
               sessionSource.Contains("TryPickGroupUniformScaleHandle(multiScalePivot, _lastMouseGui)") &&
               sessionSource.Contains("RecordMultiTransformEdit(_scaleDragUniformGroup ? \"Uniform scale decorations\" : \"Scale decorations\")") &&
               ExtractMethodSource(sessionSource, "TryApplyMultiUniformScale").Contains("_multiTransformPivotStart + offset * factor") &&
               ExtractMethodSource(sessionSource, "TryApplyMultiUniformScale").Contains("_multiTransformBefore[index].Scaling * factor") &&
               sessionSource.Contains("DrawSelectedSetAnchorConnections()") &&
               ExtractMethodSource(sessionSource, "DrawSelectedSetAnchorConnections").Contains("FormatTether(decoration.TetherPoint.Us)") &&
               ExtractMethodSource(sessionSource, "DrawSelectedSetAnchorConnections").Contains("group.Count <= 1") &&
               ExtractMethodSource(sessionSource, "DrawSelectedSetAnchorConnections").Contains("DecorationEditorOverlay.Line(anchorWorld, centerWorld, lineColor") &&
               ExtractMethodSource(sessionSource, "DrawSelectedSetAnchorConnections").Contains("ReferenceEquals(decoration, _selected)") &&
               sessionSource.Contains("TryResolveMultiTransformPlacement") &&
               sessionSource.Contains("TryApplyMultiTransformPlacements") &&
               sessionSource.Contains("RestoreMultiTransformFrame") &&
               sessionSource.Contains("MultiTransformPlacement") &&
               sessionSource.Contains("TryFindAnchorFollowTarget(_selectedConstruct, center, snapshot.TetherPoint") &&
               sessionSource.Contains("decoration.OurManager.ShiftDecoration(decoration, shift)") &&
               sessionSource.Contains("rollback[index]?.TryRestore(decoration)") &&
               sessionSource.Contains("RecordMultiTransformEdit") &&
               sessionSource.Contains("ScaleVectorAlongAxis") &&
               sessionSource.Contains("TryPositioningFromCenter") &&
               sessionSource.Contains("bool pivotEnabled = HasMultiSelectionForTransform();") &&
               sessionSource.Contains("float pivotWidth = EsuHudLayout.Scale(112f);") &&
               sessionSource.Contains("DrawGroupPivotButton(new Rect(x, y, pivotWidth, height), pivotEnabled)") &&
               sessionSource.Contains("private void DrawGroupPivotButton(Rect rect, bool enabled)") &&
               sessionSource.Contains("Select multiple decorations to choose the group transform pivot.") &&
               !sessionSource.Contains("float pivotWidth = showPivot ?") &&
               sessionSource.Contains("ApplyMultiScaleFromInspector") &&
               sessionSource.Contains("TryApplyMultiScaleFactors") &&
               sessionSource.Contains("SetVectorText(_scaleText, HasMultiSelectionForTransform() ? Vector3.one : _selected.Scaling.Us)") &&
               ExtractMethodSource(sessionSource, "DrawBottomTransformEditors").Contains("liveApply: !multiSelection") &&
               ExtractMethodSource(sessionSource, "ApplyMultiScaleFromInspector").Contains("TryApplyMultiUniformScale(factors.x)") &&
               ExtractMethodSource(sessionSource, "ApplyMultiScaleFromInspector").Contains("TryApplyMultiScaleFactors(factors)") &&
               !sessionSource.Contains("DrawGroupScaleControl") &&
               !sessionSource.Contains("ApplyGroupScaleText") &&
               !sessionSource.Contains("_groupScaleText") &&
               sessionSource.Contains("Group scale applied: ") &&
               sessionSource.Contains("DrawMultiSelectionBoundsGuide") &&
               sessionSource.Contains("DrawLocalBoundsWireframe") &&
               sessionSource.Contains("DecorationSnapshotBatchCommand("),
            "Decoration Edit Mode exposes fixed-width Select/Box/X-ray controls, selectable-origin AMUI-style multi-selection move/rotate/axis-scale/uniform-scale, anchor-follow retether staging, and batch history.");

        Assert(handleSceneInputSource.Contains("if (Input.GetMouseButtonDown(1) &&\n                _tool == DecorationEditorTool.Select &&\n                _selectionMode == DecorationSelectionMode.Box)") &&
               decorationContextMenuIndex > boxSelectRightClickIndex &&
               boxSelectRightClickPath.Contains("DecorationEditorInputScope.ClaimBuildInputForFrames();") &&
               boxSelectRightClickPath.Contains("DecorationEditorInputScope.ClaimCameraInputForFrames();") &&
               boxSelectRightClickPath.Contains("CancelBoxSelection();") &&
               boxSelectRightClickPath.Contains("_selectionMode = DecorationSelectionMode.Single;") &&
               boxSelectRightClickPath.Contains("InfoStore.Add(\"Box select disabled.\");") &&
               boxSelectRightClickPath.Contains("return;"),
            "Decoration Edit Mode right-click exits Box selection before opening the decoration context menu.");

        Assert(previousSelectionIndex >= 0 &&
               additiveSetPrimaryIndex > previousSelectionIndex &&
               restorePreviousSelectionIndex > additiveSetPrimaryIndex &&
               addClickedSelectionIndex > restorePreviousSelectionIndex &&
               addNearestToSelectionSource.Contains("_selection.Add(decoration)") &&
               addNearestToSelectionSource.Contains("Selection moved to another construct."),
            "Decoration Edit Mode additive Single selection preserves previous selections after primary promotion.");

        Assert(handleSceneInputSource.Contains("SelectNearest(\n                _lastMouseGui,\n                additive: _selectionMode == DecorationSelectionMode.Single && IsShiftHeld());"),
            "Decoration Edit Mode Shift-click Single selection routes through additive SelectNearest.");

        Assert(modeSwitchHandoffSource.Contains("internal static class EsuModeSwitchHandoff") &&
               modeSwitchHandoffSource.Contains("ConsumeInactiveCleanupFrame") &&
               modeSwitchHandoffSource.Contains("HandoffFrames") &&
               decorationBehaviourSource.Contains("EsuModeSwitchHandoff.Begin()") &&
               decorationBehaviourSource.Contains("preserveSharedHud: true") &&
               decorationBehaviourSource.Contains("EsuModeSwitchHandoff.ConsumeInactiveCleanupFrame()") &&
               smartBuildBehaviourSource.Contains("EsuModeSwitchHandoff.Begin()") &&
               smartBuildBehaviourSource.Contains("preserveSharedHud: true") &&
               smartBuildBehaviourSource.Contains("keepModeSwitchHandoffGui: true") &&
               smartBuildBehaviourSource.Contains("DrawModeSwitchHandoffGui()") &&
               smartBuildBehaviourSource.Contains("ClearModeSwitchHandoffGui()") &&
               smartBuildBehaviourSource.Contains("EsuModeSwitchHandoff.ConsumeInactiveCleanupFrame()") &&
               sessionSource.Contains("End(bool apply, bool notify = true, bool preserveSharedHud = false)") &&
               sessionSourceNormalized.Contains("if (!preserveSharedHud)\n                    DecorationEditorOverlay.Clear();") &&
               smartBuildSessionSource.Contains("End(bool preserveSharedHud = false)") &&
               smartBuildSessionSource.Contains("SuspendForModeSwitchHandoff") &&
               smartBuildSessionSource.Contains("DrawModeSwitchHandoffGui") &&
               smartBuildSessionSource.Contains("DrawGui(interactive: false)") &&
               smartBuildSessionSourceNormalized.Contains("if (!preserveSharedHud)\n                DecorationEditorOverlay.Clear();"),
            "ESU Tab mode switching uses a short handoff guard, preserves shared HUD/overlay state, and draws a passive Smart Builder bridge frame during Decoration Edit <-> Smart Builder handoffs.");

        Assert(escapeCloseGuardSource.Contains("internal static class EsuEscapeCloseGuard") &&
               escapeCloseGuardSource.Contains("s_suppressInputUntilFrame >= Time.frameCount") &&
               escapeCloseGuardSource.Contains("internal static void Arm") &&
               decorationBehaviourSource.Contains("if (Active && Input.GetKeyDown(KeyCode.Escape))") &&
               decorationBehaviourSource.Contains("EsuEscapeCloseGuard.Arm();") &&
               decorationBehaviourSource.Contains("Close(apply: false);") &&
               !decorationBehaviourSource.Contains("_session.HandleEscape()") &&
               !sessionSource.Contains("internal bool HandleEscape()") &&
               smartBuildBehaviourSource.Contains("if (Active && Input.GetKeyDown(KeyCode.Escape))") &&
               smartBuildBehaviourSource.Contains("EsuEscapeCloseGuard.Arm();") &&
               smartBuildBehaviourSource.Contains("Close(\"Escape pressed\")") &&
               !smartBuildSessionSource.Contains("Input.GetKeyDown(KeyCode.Escape)") &&
               inputScopeSource.Contains("EsuEscapeCloseGuard.Active") &&
               smartInputScopeSource.Contains("EsuEscapeCloseGuard.Active"),
            "Escape closes the active ESU editor mode directly and arms a short input guard so vanilla does not open the main menu on the same keypress.");

        Assert(inputScopeSource.Contains("_active && DecoLimitLifter.EsuInputState.AnyEsuNumberShortcutDown()") &&
               smartInputScopeSource.Contains("_active && DecoLimitLifter.EsuInputState.AnyEsuNumberShortcutDown()"),
            "ESU input scopes suppress vanilla build input on 1/2/3 shortcut key-downs while an ESU editor is active.");

        Assert(sessionSource.Contains("TryHandleEsuNumberShortcut()") &&
               sessionSource.Contains("CycleCreateSelectShortcut()") &&
               sessionSource.Contains("CycleSurfaceCreateTool()") &&
               sessionSource.Contains("CycleTransformToolShortcut()") &&
               sessionSource.Contains("CycleCommonViewModeShortcut()") &&
               sessionSource.Contains("SetSurfaceBuilderTool(SurfaceBuilderTool.Draw)") &&
               sessionSource.Contains("SurfaceCreateToolCycle") &&
               sessionSource.Contains("SurfaceExtraTool.PartialCircle") &&
               sessionSource.Contains("SurfaceExtraTool.Cone2D") &&
               sessionSource.Contains("SurfaceExtraTool.Sphere") &&
               sessionSource.Contains("SurfaceExtraTool.PartialSphere") &&
               sessionSource.Contains("SurfaceExtraTool.Cone") &&
               sessionSource.Contains("SurfaceExtraTool.Frustum") &&
               sessionSource.Contains("SetSurfaceExtraTool(SurfaceExtraTool.Path)") &&
               sessionSource.Contains("SetSurfaceExtraTool(SurfaceExtraTool.Circle)") &&
               sessionSource.Contains("SetActiveTool(DecorationEditorTool.Select)") &&
               sessionSource.Contains("_selectionMode = DecorationSelectionMode.Box") &&
               sessionSource.Contains("_selectionMode = DecorationSelectionMode.Single") &&
               sessionSource.Contains("DecorationEditorViewMode.Mixed") &&
               sessionSource.Contains("DecorationEditorViewMode.Wireframe") &&
               sessionSource.Contains("DecorationEditorViewMode.DecorationOnly") &&
               sessionSource.Contains("DecorationEditorViewMode.Normal") &&
               smartBuildSessionSource.Contains("TryHandleEsuNumberShortcut()") &&
               smartBuildSessionSource.Contains("CycleShapeShortcut()") &&
               smartBuildSessionSource.Contains("CycleTransformToolShortcut()") &&
               smartBuildSessionSource.Contains("CyclePreviewShortcut()") &&
               smartBuildSessionSource.Contains("AvailableShapeDescriptors()") &&
               smartBuildSessionSource.Contains("SelectShapeDescriptor(descriptors[(index + 1) % descriptors.Count])") &&
               smartBuildSessionSource.Contains("ArmAddMode();") &&
               smartBuildSessionSource.Contains("SetActiveToolFromShortcut(next)") &&
               smartBuildSessionSource.Contains("SmartBuildPreviewMode.Wireframe") &&
               smartBuildSessionSource.Contains("SmartBuildPreviewMode.Material") &&
               smartBuildSessionSource.Contains("SmartBuildPreviewMode.MaterialOnly"),
            "Decoration Edit Mode, Surface Builder, and Smart Builder bind 1/2/3 to create/select, transform, and display/preview cycles, including Smart Builder material-only preview.");

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
               sessionSourceNormalized.Contains("new GUIContent(\"Clear\", \"Remove the material override.\")") &&
               sessionSource.Contains("DrawBottomTransformEditors") &&
               sessionSource.Contains("DrawBottomSnapControls") &&
               sessionSource.Contains("ApplyDecorationSnapText") &&
               sessionSource.Contains("DecorationMoveSnap => EsuTransformSnapSettings.DecorationMoveSnap") &&
               sessionSource.Contains("DecorationRotateSnapDegrees => EsuTransformSnapSettings.DecorationRotateSnapDegrees") &&
               sessionSource.Contains("DecorationScaleSnap => EsuTransformSnapSettings.DecorationScaleSnap") &&
               sessionSource.Contains("DecorationEditMath.Snap(freeDelta, DecorationMoveSnap)") &&
               sessionSource.Contains("DecorationEditMath.Snap(1f + multiDelta, DecorationScaleSnap)") &&
               transformSnapSource.Contains("internal static class EsuTransformSnapSettings") &&
               transformSnapSource.Contains("DefaultDecorationMoveSnap = 0.05f") &&
               transformSnapSource.Contains("DefaultDecorationRotateSnapDegrees = 5f") &&
               transformSnapSource.Contains("DefaultDecorationScaleSnap = 0.05f") &&
               transformSnapSource.Contains("DecorationMoveMinimum = 0.001f") &&
               transformSnapSource.Contains("DecorationRotateMinimum = 0.1f") &&
               transformSnapSource.Contains("DecorationScaleMinimum = 0.00001f") &&
               transformSnapSource.Contains("DecorationScaleMaximum = float.PositiveInfinity") &&
               transformSnapSource.Contains("value.ToString(\"0.#####\"") &&
               transformSnapSource.Contains("internal static class EsuTransformSnapHud") &&
               transformSnapSource.Contains("out bool commitRequested") &&
               transformSnapSource.Contains("!string.Equals(previousMove, moveText") &&
               sessionSource.Contains("ApplyDecorationSnapText(commitRequested)") &&
               smartBuildSessionSource.Contains("ApplySmartSnapText(commitRequested)") &&
               sessionSource.Contains("DrawBottomVectorEditor") &&
               sessionSource.Contains("DrawBottomVectorComponent") &&
               sessionSource.Contains("Action<Vector3, bool> apply") &&
               sessionSource.Contains("VectorTextChanged(values, previousX, previousY, previousZ)") &&
               sessionSource.Contains("apply(liveParsed, false)") &&
               sessionSource.Contains("apply(parsed, true)") &&
               sessionSource.Contains("private void ApplyPositionFromInspector(Vector3 value, bool syncText)") &&
               !ExtractMethodSource(sessionSource, "ApplyPositionFromInspector").Contains("DecorationEditMath.Snap(value)") &&
               decorationScaleBoundsSource.Contains("internal static class DecorationScaleBounds") &&
               decorationScaleBoundsSource.Contains("AllowExtendedScale(Decoration decoration)") &&
               decorationScaleBoundsSource.Contains("MaxElementValue = float.PositiveInfinity") &&
               decorationScaleBoundsSource.Contains("MinElementValue = float.NegativeInfinity") &&
               decorationScaleBoundsSource.Contains("MaximumMagnitude = float.PositiveInfinity") &&
               decorationEditSnapshotSource.Contains("DecorationScaleBounds.AllowExtendedScale(decoration)") &&
               sessionSource.Contains("DecorationScaleBounds.AllowExtendedScale(_selected)") &&
               sessionSource.Contains("DecorationScaleBounds.AllowExtendedScale(decoration)") &&
               mimicAndDecorationCommonDataSource.Contains("DecorationScaleBounds.AllowExtendedScale(decoration)") &&
               sessionSource.Contains("value.ToString(\"0.#####\"") &&
               sessionSource.Contains("MinimumNonZeroScale = 0.00001f") &&
               sessionSource.Contains("private static bool IsValidScaleComponent(float value)") &&
               sessionSource.Contains("value == 0f || Mathf.Abs(value) >= MinimumNonZeroScale") &&
               sessionSource.Contains("Scale must be finite; each axis must be 0 or at least 0.00001.") &&
               sessionSource.Contains("Group scale factors must be finite, non-negative, and each axis must be 0 or at least 0.00001.") &&
               ExtractMethodSource(sessionSource, "ApplyMultiScaleFromInspector").Contains("RecordMultiTransformEdit(uniform ? \"Uniform scale decorations\" : \"Scale decorations\")") &&
               ExtractMethodSource(sessionSource, "TryApplyMultiScaleFactors").Contains("sourceScale.x * factors.x") &&
               sessionSourceNormalized.Contains("Mathf.Max(\n                    0f,\n                    DecorationEditMath.Snap(1f + multiDelta, DecorationScaleSnap))") &&
               sessionSource.Contains("Mode: Deco | Tab to Surface when clean") &&
               sessionSource.Contains("Mode: Surface | Tab to Build when clean") &&
               sessionSource.Contains("GUI.Label(slots.Title, surface ? \"Surface Builder\" : \"Decoration Edit Mode\"") &&
               sessionSource.Contains("Rect anchorRect") &&
               sessionSource.Contains("DrawBottomSelectionControls(slots.SelectControls)") &&
               sessionSource.Contains("DrawHideOriginalMeshButton(slots.SelectControls)") &&
               sessionSource.Contains("private void DrawHideOriginalMeshButton(Rect rect)") &&
               sessionSource.Contains("new GUIContent(\"Hide mesh\"") &&
               sessionSource.Contains("ToggleSelectedOriginalMeshVisibility") &&
               sessionSource.Contains("_selected.SetHideOriginalMesh(target)") &&
               sessionSource.Contains("_selected.GetHideOriginalMesh()") &&
               sessionSource.Contains("GetOriginalMeshVisibilityTargets") &&
               decorationEditSnapshotSource.Contains("HideOriginalMesh") &&
               sessionSource.Contains("DrawBottomAnchorFollowToggle(slots.AnchorFollow)") &&
               sessionSource.Contains("DrawBottomAnchorSettingsButton(slots.AnchorSettings)") &&
               sessionSource.Contains("DrawBottomTransformEditors(new Rect") &&
               sessionSource.Contains("float edgePadding = EsuHudLayout.Scale(4f)") &&
               sessionSource.Contains("row.width - edgePadding * 2f") &&
               sessionSourceNormalized.Contains("float available = Mathf.Max(\n                1f,") &&
               sessionSource.Contains("gap * 7f") &&
               sessionSource.Contains("float setX = Mathf.Min(x + gap, rect.xMax - setWidth)") &&
               sessionSource.Contains("float axisWidth = EsuHudLayout.Scale(18f)") &&
               sessionSource.Contains("BottomVectorAxisStyle") &&
               sessionSource.Contains("DecorationEditMath.AxisColor(axis)") &&
               sessionSource.Contains("hasSelection ? DecorationEditorTheme.Button : DecorationEditorTheme.DisabledButton") &&
               !sessionSource.Contains("GUI.enabled = previous && hasSelection") &&
               sessionSource.Contains("RefreshLiveTransformFields") &&
               sessionSourceNormalized.Contains("HandleSceneInput();\n            RefreshLiveTransformFields();") &&
               sessionSource.Contains("Mathf.Clamp(Screen.height * 0.13f, EsuHudLayout.Scale(118f), EsuHudLayout.Scale(146f))") &&
               !inspectorSource.Contains("DrawVectorEditor(\"Position\"") &&
               !inspectorSource.Contains("DrawVectorEditor(\"Rotation\"") &&
               !inspectorSource.Contains("DrawVectorEditor(\"Scale\"") &&
               sessionSource.Contains("Paint color") &&
               sessionSource.Contains("_showInspectorColorPicker") &&
               sessionSource.Contains("new GUIContent(_showInspectorColorPicker ? \"Hide list\" : \"Show list\"") &&
               sessionSource.Contains("DrawPaintColorButton(") &&
               ExtractMethodSource(sessionSource, "DrawColorEditor").Contains("SelectedColorMatches(color)") &&
               ExtractMethodSource(sessionSource, "SetSelectedColor").Contains("SetSelectedDecorationsColor(decorations, color)") &&
               ExtractMethodSource(sessionSource, "SetSelectedDecorationsColor").Contains("DecorationSnapshotBatchCommand") &&
               ExtractMethodSource(sessionSource, "SetSelectedDecorationsColor").Contains("decoration.Color.Us = color") &&
               sessionSource.IndexOf("DrawColorEditor()", StringComparison.Ordinal) <
               sessionSource.IndexOf("LabelRow(\"Owner\"", StringComparison.Ordinal) &&
               sessionSource.IndexOf("DrawMaterialEditor()", StringComparison.Ordinal) <
               sessionSource.IndexOf("LabelRow(\"Owner\"", StringComparison.Ordinal) &&
               sessionSourceNormalized.IndexOf("\"Position\",\n                _positionText", StringComparison.Ordinal) <
               sessionSourceNormalized.IndexOf("\"Rotation\",\n                _orientationText", StringComparison.Ordinal) &&
               sessionSourceNormalized.IndexOf("\"Rotation\",\n                _orientationText", StringComparison.Ordinal) <
               sessionSourceNormalized.IndexOf("\"Scale\",\n                _scaleText", StringComparison.Ordinal),
            "Decoration Edit Mode inspector uses color/material controls, while the bottom panel hosts live Position/Rotation/Scale transform editing.");

        Assert(setSelectedDecorationsColorSource.Contains("DecorationSnapshotBatchCommand") &&
               setSelectedDecorationsColorSource.Contains("decoration.Color.Us = color") &&
               setSelectedDecorationsColorSource.Contains("changedDecorations.ToArray()") &&
               setSelectedDecorationsColorSource.Contains("primaryIndex"),
            "Decoration Edit Mode batch color edits still record DecorationSnapshotBatchCommand.");

        Assert(sessionSource.Contains("DrawPaintPalette") &&
               sessionSource.Contains("DrawPaintColorRow") &&
               sessionSource.Contains("DrawPaintColorButton") &&
               sessionSource.Contains("DrawPaintSwatchLayout") &&
               sessionSource.Contains("PaintPreviewColor") &&
               sessionSource.Contains("ColorsRestricted?.GetColor(colorIndex)") &&
               sessionSource.Contains("VisiblePaintPreviewColor") &&
               sessionSource.Contains("PaintDisplayColor") &&
               sessionSource.Contains("PaintColorApplies") &&
               sessionSource.Contains("BlendPaintOverMaterialPreview") &&
               sessionSource.Contains("color.a = ClampVisiblePaintChannel(color.a);") &&
               sessionSource.Contains("case 0: return new Color(0.01f, 0f, 0f, 0f);") &&
               !sessionSource.Contains("Color color = Color.Lerp(materialColor, paintColor, 0.45f);") &&
               sessionSource.Contains("DecodeVisiblePaintRedChannel") &&
               sessionSource.Contains("FallbackPaintPreviewColor") &&
               sessionSource.Contains("PaintNearest") &&
               sessionSource.Contains("TryFindNearestDecoration") &&
               sessionSource.Contains("TryPaintPointedBlock") &&
               sessionSource.Contains("block.SetColor") &&
               sessionSource.Contains("ServerOutgoingRpcs.PaintBlock") &&
               sessionSource.Contains("ClientOutgoingRpcs.PaintBlock") &&
               sessionSource.Contains("_tool == DecorationEditorTool.Paint || _showMeshPalette") &&
               sessionSource.Contains("_toolBeforePaint") &&
               sessionSource.Contains("_showMeshPaletteBeforePaint") &&
               sessionSource.Contains("TogglePaintTool") &&
               sessionSource.Contains("IsPaintRestorableTool") &&
               sessionSource.Contains("SetActiveTool(tool);") &&
               sessionSource.Contains("s_showMeshPalette = _tool == DecorationEditorTool.Paint") &&
               sessionSource.Contains("Pick a color, then click decoration centers or blocks in the viewport to paint them.") &&
               sessionSource.Contains("Painted decoration color #") &&
               sessionSource.Contains("Painted \" + selectedCount.ToString(\"N0\", CultureInfo.InvariantCulture) + \" selected decorations color #") &&
               ExtractMethodSource(sessionSource, "PaintNearest").Contains("paintSelection") &&
               ExtractMethodSource(sessionSource, "PaintNearest").Contains("_selection.Contains(best.Decoration)") &&
               sessionSource.Contains("CountSelectedDecorationsWithColor") &&
               sessionSource.Contains("Painted block color #") &&
               sessionSource.Contains("_tool != DecorationEditorTool.Paint") &&
               sessionSource.Contains("_tool == DecorationEditorTool.Paint)") &&
               sessionSource.Contains("_tool == DecorationEditorTool.Surface ||") &&
               sessionSource.Contains("_tool == DecorationEditorTool.Paint);"),
            "Decoration Edit Mode Paint has real color swatch palettes and viewport click painting for decorations and pointed blocks.");

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

        Assert(inputScopeSource.Contains("DecorationTooltipSuppressor.IsLegacyPaintHoverMessage") &&
               inputScopeSource.Contains("InfoStore") &&
               inputScopeSource.Contains("EsuHudNotifications.TryCaptureInfoStore") &&
               tooltipSuppressorSource.Contains("ResolveBlockGetToolTipTarget") &&
               tooltipSuppressorSource.Contains("\"Block\"") &&
               tooltipSuppressorSource.Contains("\"IInteractionSettings\"") &&
               tooltipSuppressorSource.Contains("\"GetToolTip\"") &&
               tooltipSuppressorSource.Contains("ShouldIncludeVanillaPaintTooltipLine") &&
               tooltipSuppressorSource.Contains("Tip_Colored") &&
               tooltipSuppressorSource.Contains("PatchedBlockTooltipColorCheckCount") &&
               tooltipSuppressorSource.Contains("outerChecks != 1 || innerChecks != 1") &&
               tooltipSuppressorSource.Contains("OpCodes.Brtrue") &&
               tooltipSuppressorSource.Contains("OpCodes.Cgt_Un") &&
               tooltipSuppressorSource.Contains("return color != 0 &&") &&
               tooltipSuppressorSource.Contains("StaticOptionsManager.ShowMouseCursor") &&
               tooltipSuppressorSource.Contains("!EsuOwnsEditorView;") &&
               tooltipSuppressorSource.Contains("DecorationEditorInputScope.Active || SmartBuildInputScope.Active") &&
               tooltipSuppressorSource.Contains("return false;") &&
               !tooltipSuppressorSource.Contains("harmony.Patch(method, prefix: patch)") &&
               !tooltipSuppressorSource.Contains("LogSuppressedCall(__originalMethod, __args);") &&
               !tooltipSuppressorSource.Contains("TipDisplayer") &&
               !tooltipSuppressorSource.Contains("ForceTip") &&
               !tooltipSuppressorSource.Contains("TooltipGUI") &&
               !tooltipSuppressorSource.Contains("ProTip.Add") &&
               !tooltipSuppressorSource.Contains("StaticOptionsManager.SetMouseCursorActive") &&
               !tooltipSuppressorSource.Contains("SetGameControlOptions") &&
               !tooltipSuppressorSource.Contains("PlayerWantsMouseLookInUi") &&
               !tooltipSuppressorSource.Contains("GUI.tooltip") &&
               !tooltipSuppressorSource.Contains("tooltipOverride") &&
               !tooltipSuppressorSource.Contains("ClearStaleTooltipStateAfterEditorExit") &&
               !tooltipSuppressorSource.Contains("PostExitRepairActive") &&
               !tooltipSuppressorSource.Contains("LogPostExitTooltipCandidate") &&
               !focusGuardSource.Contains("ClearStaleTooltipStateAfterEditorExit") &&
               decorationBehaviourSource.Contains("EsuInputFocusGuard.TickPostExitRepair") &&
               smartBuildSessionSource.Contains("SmartBuildInputScope.End();") &&
               smartInputScopeSource.Contains("EsuInputFocusGuard.BeginEditor") &&
               smartInputScopeSource.Contains("EsuInputFocusGuard.EndEditor") &&
               pluginSource.Contains("DecorationTooltipSuppressor.Install(harmony)") &&
               pluginSource.Contains("VerifyExactTranspiler") &&
               pluginSource.Contains("DecorationTooltipSuppressor.ResolveBlockGetToolTipTarget()") &&
               pluginSource.Contains("PatchedBlockTooltipColorCheckCount != 2") &&
               !pluginSource.Contains("PatchedTipSetterCount <= 0") &&
               !sessionSource.Contains("DecorationTooltipSuppressor.ClearActiveTooltipState();") &&
               inputScopeSource.Contains("DecorationTooltipSuppressor.ClearActiveTooltipState(force: true)") &&
               !smartBuildSessionSource.Contains("DecorationTooltipSuppressor.ClearActiveTooltipState();") &&
               smartInputScopeSource.Contains("DecorationTooltipSuppressor.ClearActiveTooltipState(force: true)") &&
               notificationSource.Contains("TryCaptureInfoStore") &&
               notificationSource.Contains("DrawToolbarSlot") &&
               notificationSource.Contains("fallbackMessage") &&
               notificationSource.Contains("hasTransientMessage") &&
                notificationSource.Contains("Vector2 screenOrigin") &&
                notificationSource.Contains("screenOrigin.x + rect.x") &&
                notificationSource.Contains("screenOrigin.y + rect.y") &&
               notificationSource.Contains("DecorationEditorInputScope.Active") &&
               notificationSource.Contains("SmartBuildInputScope.Active") &&
               notificationSource.Contains("DisplaySeconds = 6f") &&
               notificationSource.Contains("ToolbarHeightScaled") &&
               notificationSource.Contains("EsuHudLayout.Scale(baseHeight)") &&
               notificationSource.Contains("MessageOverflows") &&
               notificationSource.Contains("DrawCollapsedOverflow") &&
               notificationSource.Contains("DrawExpandedPopup") &&
               notificationSource.Contains("ContainsMouse") &&
               notificationSource.Contains("CalcHeight") &&
               notificationSource.Contains("MessageStyle") &&
               notificationSource.Contains("CollapsedSeverityStyle") &&
               notificationSource.Contains("CollapsedSeverityLabel") &&
               notificationSource.Contains("FitsCollapsedSeverityLabel") &&
               notificationSource.Contains("CompactKindLabel") &&
               notificationSource.Contains("wordWrap = false") &&
               notificationSource.Contains("TextClipping.Clip") &&
               notificationSource.Contains("style.CalcSize") &&
               notificationSource.Contains("lineHeight <= height") &&
               notificationSource.Contains("return \"Warn\"") &&
               notificationSource.Contains("return \"Err\"") &&
               !notificationSource.Contains("GUI.Label(labelRect, KindLabel(kind), style)") &&
               notificationSource.Contains("\"Details\"") &&
               notificationSource.Contains("\"Hide\"") &&
               !notificationSource.Contains("PreferredSlotHeight") &&
               !notificationSource.Contains("DecorationEditorIconCatalog.Get") &&
               !notificationSource.Contains("IconKey") &&
                sessionSource.Contains("EsuHudNotifications.DrawToolbarSlot(") &&
                sessionSource.Contains("toolbarScreenOrigin") &&
                sessionSource.Contains("EsuHudNotifications.DrawExpandedPopup()") &&
                sessionSource.Contains("EsuHudNotifications.ContainsMouse(mouse)") &&
                smartBuildSessionSource.Contains("EsuHudNotifications.ToolbarHeightScaled(ToolbarHeight") &&
                smartBuildSessionSource.Contains("EsuHudNotifications.DrawToolbarSlot(") &&
                smartBuildSessionSource.Contains("new Vector2(_toolbarRect.x + frame.Rect.x") &&
                smartBuildSessionSource.Contains("EsuHudNotifications.DrawExpandedPopup()") &&
               smartBuildSessionSource.Contains("EsuHudNotifications.ContainsMouse(Event.current.mousePosition)") &&
               inGameTestPlanSource.Contains("slot stays fixed-height") &&
               inGameTestPlanSource.Contains("no icon") &&
               inGameTestPlanSource.Contains("Details") &&
               inGameTestPlanSource.Contains("no `Colored with paint #N` tooltip") &&
               inGameTestPlanSource.Contains("without changing top-panel padding"),
            "ESU modal build tools suppress only FTD Block.GetToolTip paint-hover text while preserving vanilla cursor state and route native InfoStore popups into a fixed-height, text-only ESU toolbar slot with expandable details while active.");
        Assert(toolbarAttentionSource.Contains("PulseSeconds = 2.5f") &&
               toolbarAttentionSource.Contains("DrawLastButtonPulse") &&
               toolbarAttentionSource.Contains("GUILayoutUtility.GetLastRect()") &&
               toolbarAttentionSource.Contains("DecorationEditorTheme.ErrorColor") &&
               toolbarAttentionSource.Contains("DrawBorderInside") &&
               toolbarAttentionSource.Contains("DrawBorder") &&
               toolbarAttentionSource.Contains("GUI.DrawTexture") &&
               toolbarAttentionSource.Contains("Inset(rect") &&
               sessionSource.Contains("private float _applyCancelAttentionUntil") &&
               sessionSource.Contains("AttentionIconButton(\"save\", \"Apply\"") &&
               sessionSource.Contains("AttentionIconButton(\"cancel\", \"Cancel\"") &&
               sessionSourceNormalized.Contains("RefreshApplyCancelAttention();\n                reason = \"Apply or Cancel Decoration Edit changes before switching to Smart Builder.") &&
               sessionSourceNormalized.Contains("RefreshApplyCancelAttention();\n                reason = \"Apply or Cancel Decoration Edit changes before switching to Surface Builder.") &&
               sessionSource.Contains("ClearApplyCancelAttention();") &&
               smartBuildSessionSource.Contains("private float _applyCancelAttentionUntil") &&
               smartBuildSessionSource.Contains("AttentionIconButton(\"save\", \"Apply\"") &&
               smartBuildSessionSource.Contains("AttentionIconButton(\"cancel\", \"Cancel\"") &&
               smartBuildSessionSourceNormalized.Contains("RefreshApplyCancelAttention();\n                reason = \"Apply or cancel the Smart Builder preview before switching modes.") &&
               smartBuildSessionSource.Contains("ClearApplyCancelAttention();") &&
               inGameTestPlanSource.Contains("blocked mode switch") &&
               inGameTestPlanSource.Contains("Apply") &&
               inGameTestPlanSource.Contains("Cancel") &&
               inGameTestPlanSource.Contains("without toolbar movement"),
            "ESU mode-switch blockers flash fixed-layout Apply/Cancel attention outlines in Decoration Edit Mode and Smart Builder.");
        Assert(buildModeInputGateSource.Contains("internal static class EsuBuildModeInputGate") &&
               profileSource.Contains("Q(Key.Control, Key.Shift, Key.B)") &&
               inGameTestPlanSource.Contains("Ctrl+Shift+B") &&
               inGameTestPlanSource.Contains("remains open"),
            "Smart Builder/build-mode input-gate fix is documented and covered by in-game smoke tests.");

        string inputStateSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuInputState.cs"));
        Assert(!registrationSource.Contains("ChatGUI.Instance") &&
               !inputStateSource.Contains("ChatGUI.Instance") &&
               inputStateSource.Contains("AccessTools.Field(typeof(ChatGUI), \"_instance\")") &&
               inputStateSource.Contains("IsControlHeld()") &&
               inputStateSource.Contains("KeyCode.LeftControl") &&
               inputStateSource.Contains("KeyCode.RightControl") &&
               inputStateSource.Contains("internal static bool IsEsuNumberShortcutDown(int shortcut)") &&
               inputStateSource.Contains("KeyCode.Alpha1") &&
               inputStateSource.Contains("KeyCode.Keypad1") &&
               inputStateSource.Contains("KeyCode.Alpha2") &&
               inputStateSource.Contains("KeyCode.Keypad2") &&
               inputStateSource.Contains("KeyCode.Alpha3") &&
               inputStateSource.Contains("KeyCode.Keypad3") &&
               inputStateSource.Contains("internal static bool AnyEsuNumberShortcutDown()") &&
               inGameTestPlanSource.Contains("press Ctrl alone") &&
               inGameTestPlanSource.Contains("screen does not") &&
               inGameTestPlanSource.Contains("Surface Builder Shift-click point selection still works"),
            "ESU hotkey guards inspect an existing ChatGUI instance without constructing ChatGUI/FtdKeyMap during boot and expose shared Ctrl and ESU number shortcut state.");

        Assert(serializationHudSource.Contains("DecorationEditorIconCatalog.GetRuntimeIcon(icon)") &&
               serializationHudSource.Contains("\"count\"") &&
               serializationHudSource.Contains("\"mesh\"") &&
               serializationHudSource.Contains("\"risk\""),
            "Serialization HUD rows use runtime icons outside Decoration Edit Mode without generated fallback tiles.");

        string nativeUiDoc = ReadDocumentationText(root, "docs", "DECORATION_EDITOR_NATIVE_UI.md");
        Assert(nativeUiDoc.Contains("StreamingAssets/Mods/UI/Ui Elements") &&
               nativeUiDoc.Contains("editButton") &&
               nativeUiDoc.Contains("ESU-owned fallback icons") &&
               nativeUiDoc.Contains("does not package copied FTD texture files"),
            "Decoration Edit Mode native UI and FTD icon catalog are documented.");

        string[] copiedTextureExtensions = { ".png", ".jpg", ".jpeg", ".texture", ".uielements" };
        string packageRoot = Path.Combine(root, "EndlessShapesUnlimited");
        bool copiedIconAsset = Directory
            .EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Any(path =>
                copiedTextureExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) &&
                !string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(Path.Combine(packageRoot, "header.jpg")),
                    StringComparison.OrdinalIgnoreCase));
        Assert(!copiedIconAsset,
            "Decoration Edit Mode packages no copied FTD icon texture or UI-element assets besides the required Steam Workshop header.jpg.");
    }

    private static bool ToolbarBudgetsStayInsideAvailableWidth()
    {
        var cases = new[]
        {
            new { Width = 1920f - 16f, Scale = 1f },
            new { Width = 1366f - 16f, Scale = 0.72f },
            new { Width = 1280f - 16f, Scale = 1.44f },
            new { Width = 1280f - 16f, Scale = 2f },
            new { Width = 800f - 16f, Scale = 0.72f },
            new { Width = 640f - 16f, Scale = 0.72f },
            new { Width = 640f - 16f, Scale = 2f },
        };

        foreach (var testCase in cases)
        {
            EsuHudLayout.ToolbarBudget budget = EsuHudLayout.CalculateToolbarBudget(
                testCase.Width,
                testCase.Scale);
            if (budget.LeftRailWidth < 0f ||
                budget.NotificationWidth < 0f ||
                budget.RightControlsWidth < 0f ||
                budget.Gap < 0f)
            {
                return false;
            }

            if (budget.TotalWidth > testCase.Width + 0.01f)
                return false;
        }

        return true;
    }

    private static void VerifyEsuRuntimeConsole()
    {
        EsuRuntimeLog.Clear();
        for (int index = 0; index < EsuRuntimeLog.Capacity + 5; index++)
        {
            EsuRuntimeLog.Info("test", "entry " + index.ToString(CultureInfo.InvariantCulture));
        }

        IReadOnlyList<EsuRuntimeLogEntry> snapshot = EsuRuntimeLog.Snapshot();
        string expectedLastEntry = "entry " + (EsuRuntimeLog.Capacity + 4).ToString(CultureInfo.InvariantCulture);
        Assert(EsuRuntimeLog.Count == EsuRuntimeLog.Capacity &&
               snapshot.Count == EsuRuntimeLog.Capacity &&
               snapshot[0].Message == "entry 5" &&
               snapshot[snapshot.Count - 1].Message == expectedLastEntry,
            "ESU runtime console log keeps a bounded newest-entry ring buffer.");

        EsuRuntimeLog.Clear();
        EsuRuntimeLog.Info("Deco", "info message");
        EsuRuntimeLog.Warning("Surface", "warning message");
        EsuRuntimeLog.Error("Smart", "error message", "detail text");
        EsuRuntimeLogEntry captured = EsuRuntimeLog.FromNotification(
            "HUD",
            EsuHudNotificationKind.Warning,
            "notification warning");
        IReadOnlyList<EsuRuntimeLogEntry> newest = EsuRuntimeLog.Snapshot(newestFirst: true);
        string formatted = EsuRuntimeLog.FormatForClipboard(newest);
        Assert(captured.Severity == EsuRuntimeLogSeverity.Warning &&
               newest[0].Message == "notification warning" &&
               EsuRuntimeLog.Filtered(EsuRuntimeLogSeverity.Error).Count == 1 &&
               formatted.Contains("[Warning] [HUD] notification warning") &&
               formatted.Contains("detail text"),
            "ESU runtime console log records severity, source, newest-first ordering, filters, and copy formatting.");
        EsuRuntimeLog.Clear();

        string root = FindRepositoryRoot();
        string runtimeLogSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuRuntimeLog.cs"));
        string consoleSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuConsoleWindow.cs"));
        string notificationSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuHudNotifications.cs"));
        string overlaySource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuHudNotificationOverlayRegistration.cs"));
        string sessionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditSession.cs"));
        string sessionSourceNormalized = sessionSource.Replace("\r\n", "\n");
        string smartBuildSessionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildSession.cs"));
        string smartBuildDrawGuiSource = ExtractMethodSource(smartBuildSessionSource, "DrawGui").Replace("\r\n", "\n");
        string decorationBehaviourSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditModeBehaviour.cs"));
        string smartBehaviourSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildModeBehaviour.cs"));
        string transactionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditTransactionSet.cs"));

        Assert(runtimeLogSource.Contains("internal const int Capacity = 25") &&
               runtimeLogSource.Contains("FormatForClipboard") &&
               runtimeLogSource.Contains("FromNotification") &&
               runtimeLogSource.Contains("ToSeverity") &&
               runtimeLogSource.Contains("Exception(") &&
               consoleSource.Contains("internal static class EsuConsoleWindow") &&
               !consoleSource.Contains("GUI.DragWindow") &&
               consoleSource.Contains("DrawForegroundWindow") &&
               consoleSource.Contains("ForegroundWindowId") &&
               consoleSource.Contains("GUI.Window(") &&
               consoleSource.Contains("GUI.BringWindowToFront(ForegroundWindowId)") &&
               consoleSource.Contains("GUI.depth = Math.Min(previousDepth, -10000)") &&
               consoleSource.Contains("HandleDrag") &&
               consoleSource.Contains("HeaderDragRect") &&
               consoleSource.Contains("DrawPanel(Rect rect)") &&
               consoleSource.Contains("HeaderTitleStyle") &&
               consoleSource.Contains("wordWrap = false") &&
               consoleSource.Contains("EsuHudLayout.DrawResizeGrip") &&
               consoleSource.Contains("GUI.BeginScrollView") &&
               consoleSource.Contains("FilterButton(EsuConsoleFilter.All") &&
               consoleSource.Contains("Clear") &&
               consoleSource.Contains("Copy") &&
               consoleSource.Contains("Close") &&
               consoleSource.Contains("ContainsMouse") &&
               notificationSource.Contains("EsuRuntimeLog.FromNotification") &&
               notificationSource.Contains("ShowSystem") &&
               notificationSource.Contains("DrawLogButton") &&
               notificationSource.Contains("\"Log\"") &&
               notificationSource.Contains("EsuConsoleWindow.Toggle") &&
               notificationSource.Contains("EsuConsoleWindow.ContainsMouse(mouse)") &&
               notificationSource.Contains("Vector2 screenOrigin") &&
               notificationSource.Contains("screenOrigin.x + rect.x") &&
               notificationSource.Contains("screenOrigin.y + rect.y") &&
               overlaySource.Contains("EsuHudNotifications.DrawToolbarSlot") &&
               overlaySource.Contains("EsuHudNotifications.DrawExpandedPopup") &&
               overlaySource.Contains("EsuConsoleWindow.Draw") &&
               overlaySource.Contains("!EsuHudNotifications.HasMessage") &&
               overlaySource.Contains("!EsuConsoleWindow.IsOpen") &&
               sessionSource.Contains("EsuHudNotifications.SetActiveSource(CurrentModeLogSource())") &&
               sessionSourceNormalized.Contains("DrawTopToolbar(\n                new Rect(0f, 0f, toolbarInner.width, toolbarInner.height),\n                toolbarInner.position)") &&
               sessionSource.Contains("toolbarScreenOrigin") &&
               sessionSource.Contains("EsuConsoleWindow.Draw()") &&
               smartBuildSessionSource.Contains("EsuHudNotifications.SetActiveSource(\"Smart Builder\")") &&
               smartBuildSessionSource.Contains("new Vector2(_toolbarRect.x + frame.Rect.x, _toolbarRect.y + frame.Rect.y)") &&
               smartBuildSessionSource.Contains("EsuConsoleWindow.DrawForegroundWindow()") &&
               decorationBehaviourSource.Contains("EsuRuntimeLog.Exception(\"Decoration Edit\"") &&
               smartBehaviourSource.Contains("EsuRuntimeLog.Exception(\"Smart Builder\"") &&
               transactionSource.Contains("EsuRuntimeLog.Exception(\"Decoration Edit\"") &&
               decorationBehaviourSource.Contains("AdvLogger.LogException") &&
               smartBehaviourSource.Contains("AdvLogger.LogException") &&
               transactionSource.Contains("AdvLogger.LogException"),
            "ESU runtime console is shared by Deco/Surface/Smart Builder, captures notifications, owns input hover, and mirrors handled exceptions without replacing AdvLogger.");

        int smartConsoleDraw = smartBuildDrawGuiSource.IndexOf("EsuConsoleWindow.DrawForegroundWindow()", StringComparison.Ordinal);
        int smartExpandedNotificationDraw = smartBuildDrawGuiSource.IndexOf("EsuHudNotifications.DrawExpandedPopup()", StringComparison.Ordinal);
        Assert(smartConsoleDraw > smartBuildDrawGuiSource.IndexOf("GUI.Window(_rightPanelWindowId", StringComparison.Ordinal) &&
               smartConsoleDraw > smartBuildDrawGuiSource.IndexOf("GUI.Window(_statusWindowId", StringComparison.Ordinal) &&
               smartConsoleDraw > smartBuildDrawGuiSource.IndexOf("DrawPreviewContextMenu()", StringComparison.Ordinal) &&
               smartConsoleDraw > smartBuildDrawGuiSource.IndexOf("DrawShapePreviewCard()", StringComparison.Ordinal) &&
               smartExpandedNotificationDraw > smartBuildDrawGuiSource.IndexOf("GUI.Window(_rightPanelWindowId", StringComparison.Ordinal) &&
               smartExpandedNotificationDraw > smartBuildDrawGuiSource.IndexOf("GUI.Window(_statusWindowId", StringComparison.Ordinal) &&
               smartExpandedNotificationDraw < smartBuildDrawGuiSource.IndexOf("DrawPreviewContextMenu()", StringComparison.Ordinal) &&
               smartConsoleDraw < smartBuildDrawGuiSource.IndexOf("EsuCursorTooltip.Draw()", StringComparison.Ordinal),
            "Smart Builder draws expanded notifications and the ESU console as foreground UI above panels and preview overlays.");
    }

    private static void VerifyPointerFlushPlacement()
    {
        var hit = new DecorationPointerHit(
            null,
            new Vector3i(10, 20, 30),
            new Vector3(10.49f, 20.5f, 29.75f),
            new Vector3(100f, 200f, 300f),
            Vector3.up);
        Vector3 expectedPositioning = DecorationEditMath.Snap(new Vector3(0.49f, 0.5f, -0.25f));
        Assert(VectorApproximately(hit.LocalPositioning, expectedPositioning, 0.0001f),
            "Pointer-hit positioning is computed from the reported surface hit, not an inward anchor-resolution sample.");

        Vector3 refined = DecorationPointerProbe.RefineBoundaryForTests(
            new Vector3(0f, 2f, 0f),
            new Vector3(0f, 0f, 0f),
            sample => sample.y <= 0.5f);
        Assert(Mathf.Abs(refined.y - 0.5f) <= 0.002f,
            "Pointer ray-sampling fallback refines to the outside/inside block boundary instead of keeping the first coarse sample.");

        var anchor = new Vector3i(2, 4, 6);
        bool topFace = DecorationPointerProbe.TryGetLocalFaceNormal(
            anchor,
            new Vector3(2.1f, 4.51f, 6.2f),
            out Vector3 topNormal);
        bool leftFace = DecorationPointerProbe.TryGetLocalFaceNormal(
            anchor,
            new Vector3(1.49f, 4.02f, 6.01f),
            out Vector3 leftNormal);
        bool backFace = DecorationPointerProbe.TryGetLocalFaceNormal(
            anchor,
            new Vector3(2.01f, 4.01f, 5.49f),
            out Vector3 backNormal);
        bool centerHit = DecorationPointerProbe.TryGetLocalFaceNormal(
            anchor,
            new Vector3(2.04f, 4.02f, 6.01f),
            out _);
        Assert(topFace &&
               leftFace &&
               backFace &&
               VectorApproximately(topNormal, Vector3.up, 0.0001f) &&
               VectorApproximately(leftNormal, Vector3.left, 0.0001f) &&
               VectorApproximately(backNormal, Vector3.back, 0.0001f) &&
               !centerHit,
            "Pointer hits derive local block-face normals from surface offsets instead of camera ray direction.");

        Assert(!DecorationPointerProbe.TryGetExpandedLocalBoundsRayIntervalForTests(
                    new Vector3(0f, 200f, 0f),
                    Vector3.right,
                    new Vector3(-1f, -1f, -1f),
                    new Vector3(1f, 1f, 1f),
                    DecorationPointerProbe.MeshPlacementCraftBubbleRadius,
                    out _,
                    out _),
            "Bounded mesh-placement probing rejects rays that miss the active craft bubble before doing long fallback sampling.");
        Assert(DecorationPointerProbe.TryGetExpandedLocalBoundsRayIntervalForTests(
                    new Vector3(-200f, 0f, 0f),
                    Vector3.right,
                    new Vector3(-1f, -1f, -1f),
                    new Vector3(1f, 1f, 1f),
                    DecorationPointerProbe.MeshPlacementCraftBubbleRadius,
                    out float bubbleEnter,
                    out float bubbleExit) &&
               Mathf.Abs(bubbleEnter - 99f) <= 0.002f &&
               Mathf.Abs(bubbleExit - 301f) <= 0.002f,
            "Bounded mesh-placement probing clamps physics and fallback sampling to the ray interval that intersects the 100m craft bubble.");

        Assert(SmartBuildSession.AdjacentCellFromSurfaceHit(
                    anchor,
                    new Vector3(2.1f, 4.51f, 6.2f),
                    Vector3.right).Equals(new Vector3i(2, 5, 6)) &&
               SmartBuildSession.AdjacentCellFromSurfaceHit(
                    anchor,
                    new Vector3(1.49f, 4.02f, 6.01f),
                    Vector3.up).Equals(new Vector3i(1, 4, 6)) &&
               SmartBuildSession.AdjacentCellFromSurfaceHit(
                    anchor,
                    new Vector3(2.01f, 4.01f, 6.01f),
                    Vector3.back).Equals(new Vector3i(2, 4, 5)),
            "Smart Builder derives adjacent preview cells from the clicked surface offset and only falls back to the local normal near cell centers.");
    }

    private static bool VectorApproximately(Vector3 left, Vector3 right, float tolerance) =>
        Mathf.Abs(left.x - right.x) <= tolerance &&
        Mathf.Abs(left.y - right.y) <= tolerance &&
        Mathf.Abs(left.z - right.z) <= tolerance;

    private static void VerifySurfaceDecorationBuilder()
    {
        var resolver = new SetSurfaceAnchorResolver(new[] { new Vector3i(0, 0, 0) });
        SurfaceDecorationPlan plan;
        string message;

        SurfaceDraft right = SurfaceDraftForTests(
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f));
        AssertSurfacePlanCount(right, resolver, 1,
            "Surface planner maps a right triangle to one generated decoration.");

        SurfaceDraft preciseRight = SurfaceDraftForTests(
            new Vector3(0f, 0f, 0f),
            new Vector3(1.05f, 0f, 0f),
            new Vector3(0f, 1f, 0f));
        Assert(SurfaceDecorationPlanner.TryPlan(preciseRight, resolver, out plan, out message) &&
               plan.DecorationCount == 1 &&
               Math.Abs(plan.Placements[0].Positioning.x - 0.525f) < 0.0001f &&
               Math.Abs(plan.Placements[0].Positioning.z + 0.025f) < 0.0001f,
            "Surface planner preserves precise generated decoration centers instead of snapping them to the 0.05m move grid.");

        SurfaceDraft isosceles = SurfaceDraftForTests(
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(0f, 1f, 0f));
        AssertSurfacePlanCount(isosceles, resolver, 1,
            "Surface planner maps an isosceles triangle to one generated decoration.");

        SurfaceDraft scalene = SurfaceDraftForTests(
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 0f, 0f),
            new Vector3(0.25f, 1.1f, 0f));
        AssertSurfacePlanCount(scalene, resolver, 2,
            "Surface planner maps a scalene triangle to two generated decorations.");
        Assert(SurfaceDecorationPlanner.TryPlan(scalene, resolver, out plan, out message) &&
               SurfacePlacementThicknessAxesAreCoherent(plan.Placements) &&
               SurfacePlacementTransformPlanesAreCoherent(plan.Placements),
            "Surface planner splits a scalene face into co-planar right-triangle decorations with matching final transform planes.");
        Assert(SurfacePlacementsMatchDraftNormal(scalene, plan.Placements),
            "Surface planner keeps scalene split child normals aligned with the parent face normal.");
        SurfaceDraft flippedScalene = SurfaceDraftForTests(
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 0f, 0f),
            new Vector3(0.25f, 1.1f, 0f));
        flippedScalene.Settings.NormalReversal = true;
        Assert(SurfaceDecorationPlanner.TryPlan(flippedScalene, resolver, out plan, out message) &&
               SurfacePlacementsMatchDraftNormal(flippedScalene, plan.Placements) &&
               SurfacePlacementCentersSitBehindDraftNormal(flippedScalene, plan.Placements),
            "Surface planner keeps normal-flipped split surface centers on the committed side of the source plane.");

        SurfaceDraft slantedSeparateTriangles = SlantedTwoTriangleSurfaceDraftForTests();
        Assert(SurfaceDecorationPlanner.TryPlan(slantedSeparateTriangles, resolver, out plan, out message) &&
               slantedSeparateTriangles.Faces.Count == 2 &&
               SurfacePlacementsMatchAnyDraftFaceNormal(slantedSeparateTriangles, plan.Placements),
            "Surface planner previews two separate non-axis-aligned triangles from six points.");

        SurfaceDraft slantedManualFace = SlantedTwoTriangleSurfaceDraftForTests();
        Assert(slantedManualFace.ToggleManualFacePoint(1, out message) &&
               slantedManualFace.ToggleManualFacePoint(2, out message) &&
               slantedManualFace.ToggleManualFacePoint(3, out message) &&
               slantedManualFace.TryCreateFaceFromManualSelection(out message) &&
               slantedManualFace.Faces.Count == 3 &&
               SurfaceDecorationPlanner.TryPlan(slantedManualFace, resolver, out plan, out message) &&
               SurfacePlacementsMatchAnyDraftFaceNormal(slantedManualFace, plan.Placements),
            "Surface planner previews a non-axis-aligned face created from Shift-selected points between two triangles.");

        SurfaceDraft slantedBridge = SlantedTwoTriangleSurfaceDraftForTests();
        Assert(slantedBridge.ToggleBridgeEdge(new SurfaceEdge(1, 2), out message) &&
               slantedBridge.ToggleBridgeEdge(new SurfaceEdge(3, 4), out message) &&
               slantedBridge.TryBridgeSelectedEdges(out message) &&
               slantedBridge.Faces.Count == 4 &&
               SurfaceSharedEdgesAreOpposed(slantedBridge.Faces) &&
               SurfaceDecorationPlanner.TryPlan(slantedBridge, resolver, out plan, out message) &&
               SurfacePlacementsMatchAnyDraftFaceNormal(slantedBridge, plan.Placements),
            "Surface planner previews a non-axis-aligned bridge between two selected triangle edges.");

        AssertMirroredSurfaceNormals(
            "right triangle",
            new Vector3(1.1f, 1.2f, 1.3f),
            new Vector3(2.1f, 1.2f, 1.3f),
            new Vector3(1.1f, 2.2f, 1.3f));
        AssertMirroredSurfaceNormals(
            "isosceles triangle",
            new Vector3(0.5f, 1.2f, 1.3f),
            new Vector3(1.7f, 1.2f, 1.3f),
            new Vector3(1.1f, 2.2f, 1.3f));
        AssertMirroredSurfaceNormals(
            "scalene triangle",
            new Vector3(1.1f, 1.2f, 1.3f),
            new Vector3(2.6f, 1.2f, 1.3f),
            new Vector3(1.45f, 2.25f, 1.3f));

        var connected = new SurfaceDraft();
        connected.SetConstructForTests(null);
        connected.AddPointForTests(new Vector3(0f, 0f, 0f));
        connected.AddPointForTests(new Vector3(1f, 0f, 0f));
        connected.AddPointForTests(new Vector3(0f, 1f, 0f));
        connected.AddPointForTests(new Vector3(1f, 1f, 0f));
        Assert(connected.TryAddFace(0, 1, 2, out message) &&
               connected.TryAddFace(1, 2, 3, out message) &&
               SurfaceDecorationPlanner.TryPlan(connected, resolver, out plan, out message) &&
               plan.DecorationCount == 2,
            "Surface planner supports connected edge-extended face drafts.");
        Assert(SurfaceSharedEdgesAreOpposed(connected.Faces),
            "Surface draft auto-orients connected faces so adjacent triangle normals stay coherent.");
        Assert(!connected.TryAddFace(2, 1, 0, out message) &&
               message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0,
            "Surface draft rejects duplicate or reversed faces.");

        var coloredFaces = new SurfaceDraft();
        coloredFaces.SetConstructForTests(null);
        coloredFaces.AddPointForTests(new Vector3(0f, 0f, 0f));
        coloredFaces.AddPointForTests(new Vector3(1f, 0f, 0f));
        coloredFaces.AddPointForTests(new Vector3(0f, 1f, 0f));
        coloredFaces.AddPointForTests(new Vector3(1f, 1f, 0f));
        coloredFaces.Settings.StructureBlockType = StructureBlockType.HeavyArmour;
        coloredFaces.Settings.ColorIndex = 2;
        bool firstColoredFace = coloredFaces.TryAddFace(0, 1, 2, out message);
        coloredFaces.Settings.ColorIndex = 7;
        bool secondColoredFace = coloredFaces.TryAddFace(1, 3, 2, out message);
        bool selectedFaceColor = coloredFaces.TrySetFaceColor(0, 5, out message);
        bool coloredPlan = SurfaceDecorationPlanner.TryPlan(coloredFaces, resolver, out plan, out message);
        Assert(firstColoredFace &&
               secondColoredFace &&
               selectedFaceColor &&
               coloredFaces.FaceStyleAt(0).ColorIndex == 5 &&
               coloredFaces.FaceStyleAt(1).ColorIndex == 7 &&
               coloredPlan &&
               plan.Placements.Any(placement => placement.Color == 5) &&
               plan.Placements.Any(placement => placement.Color == 7) &&
               plan.Placements.All(placement => placement.StructureBlockType == StructureBlockType.HeavyArmour),
            "Surface draft faces carry independent paint colors and the selected structure material into planned decoration placements.");
        coloredFaces.SetAllFaceColors(11);
        Assert(coloredFaces.FaceStyles.Count == coloredFaces.Faces.Count &&
               coloredFaces.FaceStyles.All(style => style.ColorIndex == 11) &&
               SurfaceDecorationPlanner.TryPlan(coloredFaces, resolver, out plan, out message) &&
               plan.Placements.All(placement => placement.Color == 11),
            "Surface draft global paint changes can recolor every current face before placement.");

        var freeClick = new SurfaceDraft();
        freeClick.SetConstructForTests(null);
        Assert(freeClick.TryAddPointForTests(new Vector3(0f, 0f, 0f), extendSelectedEdge: false, out message) &&
               freeClick.FreeTriangleSelectionCount == 1 &&
               freeClick.TryAddPointForTests(new Vector3(1f, 0f, 0f), extendSelectedEdge: false, out message) &&
               freeClick.FreeTriangleSelectionCount == 2 &&
               freeClick.TryAddPointForTests(new Vector3(0f, 1f, 0f), extendSelectedEdge: false, out message) &&
               freeClick.Faces.Count == 1 &&
               freeClick.FreeTriangleSelectionCount == 0 &&
               freeClick.TryAddPointForTests(new Vector3(2f, 0f, 0f), extendSelectedEdge: false, out message) &&
               freeClick.TryAddPointForTests(new Vector3(3f, 0f, 0f), extendSelectedEdge: false, out message) &&
               freeClick.TryAddPointForTests(new Vector3(2f, 1f, 0f), extendSelectedEdge: false, out message) &&
               freeClick.Faces.Count == 2 &&
               freeClick.FreeTriangleSelectionCount == 0,
            "Surface draft normal free-click grouping creates another standalone triangle every three points.");

        var manualSelection = new SurfaceDraft();
        manualSelection.SetConstructForTests(null);
        manualSelection.AddPointForTests(new Vector3(0f, 0f, 0f));
        manualSelection.AddPointForTests(new Vector3(1f, 0f, 0f));
        manualSelection.AddPointForTests(new Vector3(0f, 1f, 0f));
        Assert(manualSelection.ToggleManualFacePoint(0, out message) &&
               manualSelection.ToggleManualFacePoint(1, out message) &&
               manualSelection.ManualFaceSelectionCount == 2 &&
               manualSelection.Faces.Count == 0 &&
               manualSelection.TrySelectManualPointEdge(out message) &&
               manualSelection.SelectionKind == SurfaceSelectionKind.Edge &&
               manualSelection.SelectedEdge.Matches(0, 1) &&
               manualSelection.ManualFaceSelectionCount == 0,
            "Surface draft Shift-selected two-point sets connect into an edge seed without creating a face.");
        Assert(manualSelection.ToggleManualFacePoint(0, out message) &&
               manualSelection.ToggleManualFacePoint(1, out message) &&
               manualSelection.ToggleManualFacePoint(2, out message) &&
               manualSelection.ManualFaceSelectionCount == 3 &&
               manualSelection.Faces.Count == 0 &&
               manualSelection.TryCreateFaceFromManualSelection(out message) &&
               manualSelection.Faces.Count == 1 &&
               manualSelection.ManualFaceSelectionCount == 0,
            "Surface draft Shift-selected three-point sets create faces only through the explicit context action.");

        var edgeExtended = new SurfaceDraft();
        edgeExtended.SetConstructForTests(null);
        Assert(edgeExtended.TryAddPointForTests(new Vector3(0f, 0f, 0f), extendSelectedEdge: false, out message) &&
               edgeExtended.TryAddPointForTests(new Vector3(1f, 0f, 0f), extendSelectedEdge: false, out message) &&
               edgeExtended.TryAddPointForTests(new Vector3(0f, 1f, 0f), extendSelectedEdge: false, out message),
            "Surface draft test setup creates the base triangle through point placement.");
        edgeExtended.SelectEdge(1, 2);
        Assert(edgeExtended.TryAddPointForTests(new Vector3(1f, 1f, 0f), extendSelectedEdge: true, out message) &&
               edgeExtended.Faces.Count == 2 &&
               edgeExtended.SelectionKind == SurfaceSelectionKind.Edge &&
               edgeExtended.SelectedEdge.Matches(2, 3),
            "Surface draft selected-edge extension still creates connected faces and advances the selected edge.");

        var selectedEdgeBridge = new SurfaceDraft();
        selectedEdgeBridge.SetConstructForTests(null);
        selectedEdgeBridge.AddPointForTests(new Vector3(0f, 0f, 0f));
        selectedEdgeBridge.AddPointForTests(new Vector3(1f, 0f, 0f));
        selectedEdgeBridge.AddPointForTests(new Vector3(0f, 1f, 0f));
        selectedEdgeBridge.AddPointForTests(new Vector3(0f, -1f, 0f));
        selectedEdgeBridge.AddPointForTests(new Vector3(-1f, 0f, 0f));
        Assert(selectedEdgeBridge.TryAddFace(0, 1, 2, out message) &&
               selectedEdgeBridge.TryAddFace(0, 3, 4, out message),
            "Surface draft selected-edge bridge test setup creates two seed triangles.");
        selectedEdgeBridge.SelectEdge(0, 1);
        Assert(selectedEdgeBridge.ToggleBridgeEdge(new SurfaceEdge(0, 3), out message) &&
               selectedEdgeBridge.BridgeEdgeSelection.Count == 2 &&
               selectedEdgeBridge.BridgeEdgeSelection[0].Matches(0, 1) &&
               selectedEdgeBridge.BridgeEdgeSelection[1].Matches(0, 3) &&
               selectedEdgeBridge.TryBridgeSelectedEdges(out message) &&
               selectedEdgeBridge.Faces.Count == 3,
            "Surface draft uses the already selected edge as the first bridge edge when Shift-clicking a second edge.");

        var sharedBridge = new SurfaceDraft();
        sharedBridge.SetConstructForTests(null);
        sharedBridge.AddPointForTests(new Vector3(0f, 0f, 0f));
        sharedBridge.AddPointForTests(new Vector3(1f, 0f, 0f));
        sharedBridge.AddPointForTests(new Vector3(0f, 1f, 0f));
        sharedBridge.AddPointForTests(new Vector3(0f, -1f, 0f));
        sharedBridge.AddPointForTests(new Vector3(-1f, 0f, 0f));
        Assert(sharedBridge.TryAddFace(0, 1, 2, out message) &&
               sharedBridge.TryAddFace(0, 3, 4, out message) &&
               sharedBridge.ToggleBridgeEdge(new SurfaceEdge(0, 1), out message) &&
               sharedBridge.ToggleBridgeEdge(new SurfaceEdge(0, 3), out message) &&
               sharedBridge.TryBridgeSelectedEdges(out message) &&
               sharedBridge.Faces.Count == 3 &&
               sharedBridge.BridgeEdgeSelection.Count == 0,
            "Surface draft bridges two selected edges that share one point into one triangle.");

        var quadBridge = new SurfaceDraft();
        quadBridge.SetConstructForTests(null);
        quadBridge.AddPointForTests(new Vector3(0f, 0f, 0f));
        quadBridge.AddPointForTests(new Vector3(1f, 0f, 0f));
        quadBridge.AddPointForTests(new Vector3(0f, -1f, 0f));
        quadBridge.AddPointForTests(new Vector3(0f, 2f, 0f));
        quadBridge.AddPointForTests(new Vector3(1f, 2f, 0f));
        quadBridge.AddPointForTests(new Vector3(0f, 3f, 0f));
        Assert(quadBridge.TryAddFace(0, 1, 2, out message) &&
               quadBridge.TryAddFace(3, 4, 5, out message) &&
               quadBridge.ToggleBridgeEdge(new SurfaceEdge(0, 1), out message) &&
               quadBridge.ToggleBridgeEdge(new SurfaceEdge(3, 4), out message) &&
               quadBridge.TryBridgeSelectedEdges(out message) &&
               quadBridge.Faces.Count == 4,
            "Surface draft bridges two selected four-point edges into two triangle faces.");

        var alternateBridge = new SurfaceDraft();
        alternateBridge.SetConstructForTests(null);
        alternateBridge.AddPointForTests(new Vector3(0f, 0f, 0f));
        alternateBridge.AddPointForTests(new Vector3(1f, 0f, 0f));
        alternateBridge.AddPointForTests(new Vector3(0f, 1f, 0f));
        alternateBridge.AddPointForTests(new Vector3(1f, 1f, 0f));
        alternateBridge.AddPointForTests(new Vector3(0f, 2f, 0f));
        Assert(alternateBridge.TryAddFace(0, 1, 2, out message) &&
               alternateBridge.TryAddFace(2, 3, 4, out message) &&
               alternateBridge.ToggleBridgeEdge(new SurfaceEdge(0, 1), out message) &&
               alternateBridge.ToggleBridgeEdge(new SurfaceEdge(2, 3), out message) &&
               alternateBridge.TryBridgeSelectedEdges(out message) &&
               alternateBridge.Faces.Count == 4 &&
               SurfaceSharedEdgesAreOpposed(alternateBridge.Faces),
            "Surface draft tries the alternate quad bridge diagonal when the preferred pairing would duplicate an existing face.");

        var sharedCenterRightTriangles = new SurfaceDraft();
        sharedCenterRightTriangles.SetConstructForTests(null);
        sharedCenterRightTriangles.AddPointForTests(new Vector3(-1f, 0f, 0f));
        sharedCenterRightTriangles.AddPointForTests(new Vector3(1f, 0f, 0f));
        sharedCenterRightTriangles.AddPointForTests(new Vector3(0f, 1f, 0f));
        sharedCenterRightTriangles.AddPointForTests(new Vector3(0f, -2f, 0f));
        sharedCenterRightTriangles.AddPointForTests(new Vector3(0f, 2f, 0f));
        sharedCenterRightTriangles.AddPointForTests(new Vector3(2f, 0f, 0f));
        bool sharedCenterFirstFace = sharedCenterRightTriangles.TryAddFace(0, 1, 2, out message);
        bool sharedCenterSecondFace = sharedCenterRightTriangles.TryAddFace(3, 5, 4, out message);
        bool sharedCenterPlanned = SurfaceDecorationPlanner.TryPlanMirroredVariants(
            sharedCenterRightTriangles,
            resolver,
            new[]
            {
                new EsuSymmetry.SymmetryVariant(Array.Empty<DecorationEditAxis>())
            },
            out plan,
            out message);
        Assert(sharedCenterFirstFace &&
               sharedCenterSecondFace &&
               sharedCenterPlanned &&
               plan.DecorationCount == 2 &&
               SurfacePlacementsSharePositionButNotTransform(plan.Placements),
            "Surface planner preserves distinct bridge-style right-triangle placements that share a generated center.");

        SurfaceDraft duplicateBridge = SurfaceDraftForTests(Vector3.zero, Vector3.right, Vector3.up);
        Assert(duplicateBridge.ToggleBridgeEdge(new SurfaceEdge(0, 1), out message) &&
               duplicateBridge.ToggleBridgeEdge(new SurfaceEdge(0, 2), out message) &&
               !duplicateBridge.TryBridgeSelectedEdges(out message) &&
               duplicateBridge.Faces.Count == 1 &&
               duplicateBridge.BridgeEdgeSelection.Count == 2,
            "Surface draft rejects duplicate bridge faces without adding partial bridge geometry.");

        var repeated = new SurfaceDraft();
        repeated.SetConstructForTests(null);
        repeated.AddPointForTests(Vector3.zero);
        repeated.AddPointForTests(Vector3.right);
        repeated.AddPointForTests(Vector3.up);
        Assert(!repeated.TryAddFace(0, 0, 1, out message),
            "Surface draft rejects repeated point faces.");

        SurfaceDraft zeroArea = SurfaceDraftForTests(
            Vector3.zero,
            Vector3.right,
            Vector3.right * 2f);
        Assert(!SurfaceDecorationPlanner.TryPlan(zeroArea, resolver, out plan, out message) &&
               message.IndexOf("zero", StringComparison.OrdinalIgnoreCase) >= 0,
            "Surface planner rejects zero-area/collinear faces.");

        SurfaceDraft nonFinite = SurfaceDraftForTests(
            Vector3.zero,
            new Vector3(float.NaN, 0f, 0f),
            Vector3.up);
        Assert(!SurfaceDecorationPlanner.TryPlan(nonFinite, resolver, out plan, out message) &&
               message.IndexOf("non-finite", StringComparison.OrdinalIgnoreCase) >= 0,
            "Surface planner rejects non-finite coordinates.");

        SurfaceDraft anchored = SurfaceDraftForTests(Vector3.zero, Vector3.right, Vector3.up);
        Assert(SurfaceDecorationPlanner.TryPlan(
                   anchored,
                   new SetSurfaceAnchorResolver(new[] { new Vector3i(0, 0, 0) }),
                   out plan,
                   out message) &&
               plan.Placements.All(placement => DecorationEditMath.IsWithinPositionLimit(placement.Positioning)),
            "Surface nearest-anchor planning accepts generated decoration offsets inside +/-10m.");
        Assert(!SurfaceDecorationPlanner.TryPlan(
                   anchored,
                   new SetSurfaceAnchorResolver(Array.Empty<Vector3i>()),
                   out plan,
                   out message) &&
               message.IndexOf("no valid nearest anchor", StringComparison.OrdinalIgnoreCase) >= 0,
            "Surface nearest-anchor planning rejects generated decorations with no valid nearby block.");

        SurfaceDraft explicitSharedSurface = SurfaceDraftForTests(
            Vector3.zero,
            Vector3.right,
            Vector3.up);
        explicitSharedSurface.Settings.NearestAnchor = false;
        bool explicitSurfaceSet = explicitSharedSurface.TrySetSharedAnchor(null, new Vector3i(5, 0, 0), out message);
        bool explicitSurfacePlanned = SurfaceDecorationPlanner.TryPlan(
            explicitSharedSurface,
            new SetSurfaceAnchorResolver(new[] { new Vector3i(5, 0, 0) }),
            out plan,
            out message);
        bool explicitSurfaceAnchors = explicitSurfacePlanned &&
                                      plan.Placements.All(placement => SameCell(placement.Anchor, new Vector3i(5, 0, 0)));
        Assert(explicitSurfaceSet && explicitSurfacePlanned && explicitSurfaceAnchors,
            "Surface same-anchor mode accepts an explicit shared anchor selected independently from generated faces. Set=" +
            explicitSurfaceSet.ToString(CultureInfo.InvariantCulture) +
            " planned=" + explicitSurfacePlanned.ToString(CultureInfo.InvariantCulture) +
            " anchors=" + explicitSurfaceAnchors.ToString(CultureInfo.InvariantCulture) +
            " message=" + (message ?? string.Empty));
        bool movedSurfaceAnchor = explicitSharedSurface.TryMoveSharedAnchor(new Vector3i(4, 0, 0), out message);
        bool movedSurfacePlanned = SurfaceDecorationPlanner.TryPlan(
            explicitSharedSurface,
            new SetSurfaceAnchorResolver(new[] { new Vector3i(4, 0, 0) }),
            out plan,
            out message);
        bool movedSurfaceAnchors = movedSurfacePlanned &&
                                   plan.Placements.All(placement => SameCell(placement.Anchor, new Vector3i(4, 0, 0)));
        Assert(movedSurfaceAnchor && movedSurfacePlanned && movedSurfaceAnchors,
            "Surface same-anchor draft can move the explicit shared anchor before applying. Move=" +
            movedSurfaceAnchor.ToString(CultureInfo.InvariantCulture) +
            " planned=" + movedSurfacePlanned.ToString(CultureInfo.InvariantCulture) +
            " anchors=" + movedSurfaceAnchors.ToString(CultureInfo.InvariantCulture) +
            " message=" + (message ?? string.Empty));
        Assert(!SurfaceDecorationPlanner.TryPlan(
                   explicitSharedSurface,
                   new SetSurfaceAnchorResolver(new[] { new Vector3i(0, 0, 0) }),
                   out plan,
                   out message) &&
               message.IndexOf("shared anchor", StringComparison.OrdinalIgnoreCase) >= 0,
            "Surface same-anchor planning rejects an explicit shared anchor that is no longer a valid block.");

        Guid generatorMesh = new Guid("11111111-2222-3333-4444-555555555555");
        Guid generatorMaterial = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var defaultGeneratorSettings = new DecorationGeneratorSettings
        {
            MeshGuid = generatorMesh
        };
        var defaultGeneratorDraft = new DecorationGeneratorDraft();
        defaultGeneratorDraft.SetTool(SurfaceExtraTool.Path);
        Assert(defaultGeneratorSettings.MeshGuid == generatorMesh &&
               DecorationGeneratorSettings.MetalPole1mGuid == new Guid("ad00935b-e95c-4345-8ea7-646846bc16db") &&
               defaultGeneratorSettings.LengthAxis == DecorationEditAxis.Z &&
               Math.Abs(defaultGeneratorSettings.Diameter - 0.05f) < 0.0001f &&
               Math.Abs(defaultGeneratorSettings.ArcDegrees - 360f) < 0.0001f &&
               Math.Abs(defaultGeneratorSettings.ShapeHeight - 2f) < 0.0001f &&
               Math.Abs(defaultGeneratorSettings.TopRadius - 1f) < 0.0001f &&
               defaultGeneratorSettings.RingCount == 6 &&
               defaultGeneratorDraft.TryAddPathPoint(null, new Vector3(0f, 0f, 0f), out message) &&
               defaultGeneratorDraft.TryAddPathPoint(null, new Vector3(0f, 0f, 2f), out message) &&
               DecorationGeneratorPlanner.TryPlan(
                   defaultGeneratorDraft,
                   defaultGeneratorSettings,
                   new SetSurfaceAnchorResolver(new[] { new Vector3i(0, 0, 1) }),
                   out DecorationGeneratorPlan defaultGeneratorPlan,
                   out message) &&
               defaultGeneratorPlan.DecorationCount == 1 &&
               Math.Abs(defaultGeneratorPlan.Placements[0].Scaling.x - 0.05f) < 0.0001f &&
               Math.Abs(defaultGeneratorPlan.Placements[0].Scaling.y - 0.05f) < 0.0001f &&
               Math.Abs(defaultGeneratorPlan.Placements[0].Scaling.z - 2f) < 0.0001f,
            "Decoration generator defaults to the ES2 metal 1m pole contract: Z length axis, 0.05 diameter, and stretched pole segments.");

        var generatorSettings = new DecorationGeneratorSettings
        {
            MeshGuid = generatorMesh,
            LengthAxis = DecorationEditAxis.X,
            Diameter = 0.25f,
            ColorIndex = 9,
            MaterialReplacement = generatorMaterial
        };
        var generatorDraft = new DecorationGeneratorDraft();
        generatorDraft.SetTool(SurfaceExtraTool.Path);
        Assert(generatorDraft.TryAddPathPoint(null, new Vector3(0f, 0f, 0f), out message) &&
               generatorDraft.TryAddPathPoint(null, new Vector3(2f, 0f, 0f), out message) &&
               DecorationGeneratorPlanner.TryPlan(
                   generatorDraft,
                   generatorSettings,
                   new SetSurfaceAnchorResolver(new[] { new Vector3i(1, 0, 0) }),
                   out DecorationGeneratorPlan generatorPlan,
                   out message) &&
               generatorPlan.DecorationCount == 1 &&
               generatorPlan.Placements[0].MeshGuid == generatorMesh &&
               generatorPlan.Placements[0].Color == 9 &&
               generatorPlan.Placements[0].MaterialReplacement == generatorMaterial &&
               Math.Abs(generatorPlan.Placements[0].Scaling.x - 2f) < 0.0001f &&
               Math.Abs(generatorPlan.Placements[0].Scaling.y - 0.25f) < 0.0001f,
            "Decoration generator path with two points creates one scaled segment placement carrying mesh, color, and material.");

        var anchorOnlyGenerator = new DecorationGeneratorDraft();
        Assert(anchorOnlyGenerator.TrySetSharedAnchor(null, new Vector3i(6, 0, 0), out message) &&
               anchorOnlyGenerator.HasDraft &&
               !anchorOnlyGenerator.HasPlaceableGeometry,
            "Decoration generator treats an explicit shared anchor as draft state without previewing geometry before a path or shape exists.");

        var sharedGeneratorSettings = new DecorationGeneratorSettings
        {
            MeshGuid = generatorMesh,
            LengthAxis = DecorationEditAxis.X,
            Diameter = 0.25f,
            NearestAnchor = false
        };
        var sharedGeneratorDraft = new DecorationGeneratorDraft();
        sharedGeneratorDraft.SetTool(SurfaceExtraTool.Path);
        Assert(sharedGeneratorDraft.TryAddPathPoint(null, new Vector3(0f, 0f, 0f), out message) &&
               sharedGeneratorDraft.TryAddPathPoint(null, new Vector3(2f, 0f, 0f), out message) &&
               sharedGeneratorDraft.TrySetSharedAnchor(null, new Vector3i(6, 0, 0), out message) &&
               DecorationGeneratorPlanner.TryPlan(
                   sharedGeneratorDraft,
                   sharedGeneratorSettings,
                   new SetSurfaceAnchorResolver(new[] { new Vector3i(6, 0, 0) }),
                   out DecorationGeneratorPlan sharedGeneratorPlan,
                   out message) &&
               sharedGeneratorPlan.Placements.All(placement => SameCell(placement.Anchor, new Vector3i(6, 0, 0))),
            "Decoration generator same-anchor mode accepts an explicit shared anchor selected independently from path/shape points.");
        Assert(sharedGeneratorDraft.TryMoveSharedAnchor(new Vector3i(5, 0, 0), out message) &&
               DecorationGeneratorPlanner.TryPlan(
                   sharedGeneratorDraft,
                   sharedGeneratorSettings,
                   new SetSurfaceAnchorResolver(new[] { new Vector3i(5, 0, 0) }),
                   out sharedGeneratorPlan,
                   out message) &&
               sharedGeneratorPlan.Placements.All(placement => SameCell(placement.Anchor, new Vector3i(5, 0, 0))),
            "Decoration generator same-anchor draft can move the explicit shared anchor before applying.");
        Assert(!DecorationGeneratorPlanner.TryPlan(
                   sharedGeneratorDraft,
                   sharedGeneratorSettings,
                   new SetSurfaceAnchorResolver(new[] { new Vector3i(0, 0, 0) }),
                   out sharedGeneratorPlan,
                   out message) &&
               message.IndexOf("shared anchor", StringComparison.OrdinalIgnoreCase) >= 0,
            "Decoration generator same-anchor planning rejects an explicit shared anchor that is no longer a valid block.");

        Assert(generatorDraft.TryAddPathPoint(null, new Vector3(2f, 1f, 0f), out message) &&
               DecorationGeneratorPlanner.TryPlan(
                   generatorDraft,
                   generatorSettings,
                   new SetSurfaceAnchorResolver(new[] { new Vector3i(1, 0, 0), new Vector3i(2, 1, 0) }),
                   out generatorPlan,
                   out message) &&
               generatorPlan.DecorationCount == 2,
            "Decoration generator path with three points creates two segment placements.");

        var circleDraft = new DecorationGeneratorDraft();
        circleDraft.SetTool(SurfaceExtraTool.Circle);
        generatorSettings.CircleRadius = 2f;
        generatorSettings.CircleSegments = 8;
        Assert(circleDraft.TrySetCircleCenter(null, Vector3.zero, Vector3.up, out message) &&
               DecorationGeneratorPlanner.TryPlan(
                   circleDraft,
                   generatorSettings,
                   new SetSurfaceAnchorResolver(new[] { new Vector3i(0, 0, 0) }),
                   out generatorPlan,
                   out message) &&
               generatorPlan.DecorationCount == 8,
            "Decoration generator circle creates the requested number of tangent segment placements.");

        var generatorShapeSettings = new DecorationGeneratorSettings
        {
            MeshGuid = generatorMesh,
            LengthAxis = DecorationEditAxis.Z,
            Diameter = 0.05f,
            CircleRadius = 2f,
            CircleSegments = 4,
            ArcDegrees = 180f,
            ShapeHeight = 3f,
            TopRadius = 1f,
            RingCount = 1,
            NearestAnchor = false
        };
        AssertGeneratorShapePlanCount(
            SurfaceExtraTool.PartialCircle,
            generatorShapeSettings,
            4,
            "Decoration generator partial circle creates one open arc segment per requested segment.");
        AssertGeneratorShapePlanCount(
            SurfaceExtraTool.Cone2D,
            generatorShapeSettings,
            9,
            "Decoration generator 2D cone creates a flat partial sector arc plus spokes from the center.");

        generatorShapeSettings.CircleSegments = 8;
        generatorShapeSettings.ArcDegrees = 360f;
        generatorShapeSettings.RingCount = 2;
        AssertGeneratorShapePlanCount(
            SurfaceExtraTool.Cone,
            generatorShapeSettings,
            16,
            "Decoration generator cone creates a base ring plus apex spokes.");
        AssertGeneratorShapePlanCount(
            SurfaceExtraTool.Frustum,
            generatorShapeSettings,
            24,
            "Decoration generator frustum creates bottom/top rings plus side lines.");
        AssertGeneratorShapePlanCount(
            SurfaceExtraTool.Sphere,
            generatorShapeSettings,
            40,
            "Decoration generator sphere creates latitude rings plus meridians.");

        generatorShapeSettings.CircleSegments = 4;
        generatorShapeSettings.ArcDegrees = 180f;
        generatorShapeSettings.RingCount = 1;
        AssertGeneratorShapePlanCount(
            SurfaceExtraTool.PartialSphere,
            generatorShapeSettings,
            14,
            "Decoration generator partial sphere creates an open latitude arc plus partial meridians.");

        Assert(!DecorationGeneratorPlanner.TryPlan(
                   circleDraft,
                   generatorSettings,
                   new SetSurfaceAnchorResolver(Array.Empty<Vector3i>()),
                   out generatorPlan,
                   out message) &&
               message.IndexOf("no valid nearest anchor", StringComparison.OrdinalIgnoreCase) >= 0,
            "Decoration generator rejects the whole plan when no segment can resolve a nearest anchor.");

        try
        {
            EsuSymmetry.Clear();
            EsuSymmetry.SetPlaneForTests(DecorationEditAxis.X, 0);
            IReadOnlyList<EsuSymmetry.SymmetryVariant> xVariants = EsuSymmetry.Variants();
            EsuSymmetry.SymmetryVariant xMirror =
                xVariants.First(variant => variant.AxisCount == 1 &&
                                           variant.Axes.Contains(DecorationEditAxis.X));
            SurfaceDraft xSymmetry = SurfaceDraftForTests(
                new Vector3(8f, 0f, 0f),
                new Vector3(9f, 0f, 0f),
                new Vector3(8f, 1f, 0f));
            SurfaceDraft xMirrored = xSymmetry.CreateMirroredForSymmetry(xMirror);
            Assert(Math.Abs(xMirrored.Points[0].x + 8f) < 0.0001f &&
                   xMirrored.Faces[0].A == 0 &&
                   xMirrored.Faces[0].B == 2 &&
                   xMirrored.Faces[0].C == 1,
                "Surface symmetry mirrors draft points and flips face winding for odd-axis mirror variants.");

            Assert(SurfaceDecorationPlanner.TryPlanMirroredVariants(
                       xSymmetry,
                       new SetSurfaceAnchorResolver(new[]
                       {
                           new Vector3i(8, 0, 0),
                           new Vector3i(-8, 0, 0)
                       }),
                       xVariants,
                       out plan,
                       out message) &&
                   plan.DecorationCount == 2,
                "Surface symmetry plans original and mirrored surface decorations as one combined target set.");

            Assert(!SurfaceDecorationPlanner.TryPlanMirroredVariants(
                       xSymmetry,
                       new SetSurfaceAnchorResolver(new[] { new Vector3i(8, 0, 0) }),
                       xVariants,
                       out plan,
                       out message) &&
                   message.IndexOf("Surface symmetry placement rejected", StringComparison.OrdinalIgnoreCase) >= 0,
                "Surface symmetry rejects the whole placement when a mirrored nearest anchor is missing.");

            EsuSymmetry.Clear();
            EsuSymmetry.SetPlaneForTests(DecorationEditAxis.X, 0);
            EsuSymmetry.SetPlaneForTests(DecorationEditAxis.Y, 0);
            EsuSymmetry.SetPlaneForTests(DecorationEditAxis.Z, 0);
            SurfaceDraft xyzSymmetry = SurfaceDraftForTests(
                new Vector3(1f, 1f, 1f),
                new Vector3(2f, 1f, 1f),
                new Vector3(1f, 2f, 1f));
            xyzSymmetry.Settings.NearestAnchor = false;
            Assert(SurfaceDecorationPlanner.TryPlanMirroredVariants(
                       xyzSymmetry,
                       new SetSurfaceAnchorResolver(new[] { new Vector3i(0, 0, 0) }),
                       EsuSymmetry.Variants(),
                       out plan,
                       out message) &&
                   plan.DecorationCount == 8,
                "Surface symmetry combines X/Y/Z variants into eight distinct generated surface placements.");

            Assert(!SurfaceDecorationPlanner.TryPlanMirroredVariants(
                       xyzSymmetry,
                       new SetSurfaceAnchorResolver(Array.Empty<Vector3i>()),
                       EsuSymmetry.Variants(),
                       out plan,
                       out message) &&
                   message.IndexOf("same-anchor", StringComparison.OrdinalIgnoreCase) >= 0,
                "Surface same-anchor planning rejects drafts when no shared anchor can be resolved.");

            EsuSymmetry.Clear();
            EsuSymmetry.SetPlaneForTests(DecorationEditAxis.X, 0);
            SurfaceDraft onPlane = SurfaceDraftForTests(
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0f, 1f));
            onPlane.Settings.NearestAnchor = false;
            Assert(SurfaceDecorationPlanner.TryPlanMirroredVariants(
                       onPlane,
                       new SetSurfaceAnchorResolver(new[] { new Vector3i(0, 0, 0) }),
                       EsuSymmetry.Variants(),
                       out plan,
                       out message) &&
                   plan.DecorationCount == 1,
                "Surface symmetry dedupes mirrored draft geometry that lies on the active plane.");

            EsuSymmetry.Clear();
            EsuSymmetry.SetPlaneForTests(DecorationEditAxis.X, 0);
            var symmetryGenerator = new DecorationGeneratorDraft();
            symmetryGenerator.SetTool(SurfaceExtraTool.Path);
            Assert(symmetryGenerator.TryAddPathPoint(null, new Vector3(1f, 0f, 0f), out message) &&
                   symmetryGenerator.TryAddPathPoint(null, new Vector3(2f, 0f, 0f), out message) &&
                   DecorationGeneratorPlanner.TryPlanMirroredVariants(
                       symmetryGenerator,
                       generatorSettings,
                       new SetSurfaceAnchorResolver(new[]
                       {
                           new Vector3i(1, 0, 0),
                           new Vector3i(-1, 0, 0)
                       }),
                       EsuSymmetry.Variants(),
                       out generatorPlan,
                       out message) &&
                   generatorPlan.DecorationCount == 2,
                "Decoration generator symmetry mirrors path segment placements and deduplicates them through the shared placement key path.");
        }
        finally
        {
            EsuSymmetry.Clear();
        }

        string root = FindRepositoryRoot();
        string plannerSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "SurfaceDecorationPlanner.cs"));
        string generatorSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationGeneratorPlanner.cs"));
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
        string meshPreviewRendererSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationMeshPreviewRenderer.cs"));
        string sessionSourceNormalized = sessionSource.Replace("\r\n", "\n");
        string sharedAnchorDragSource = ExtractMethodSource(sessionSource, "TryUpdateSharedAnchorDrag");
        string surfaceExtraToolsPanelSource = ExtractMethodSource(sessionSource, "DrawSurfaceExtraToolsPanel");
        string generatorMaterialEditorSource = ExtractMethodSource(sessionSource, "DrawGeneratorMaterialEditor");
        string generatorMaterialListViewportHeightSource = ExtractMethodSource(sessionSource, "GeneratorMaterialListViewportHeight");
        string drawSurfaceOverlaySource = ExtractMethodSource(sessionSource, "DrawSurfaceOverlay");
        string drawSurfacePlanMeshPreviewSource = ExtractMethodSource(sessionSource, "DrawSurfacePlanMeshPreview");
        string surfacePreviewMaterialSource = ExtractMethodSource(sessionSource, "SurfacePreviewMaterial");
        string surfacePlacementMatrixSource = ExtractMethodSource(sessionSource, "SurfacePlacementMatrix");
        string createFaceFromSelectedSurfacePointsSource = ExtractMethodSource(sessionSource, "CreateFaceFromSelectedSurfacePoints");
        string commitSharedAnchorDragSource = ExtractMethodSource(sessionSource, "CommitSharedAnchorDrag");
        string drawSurfaceSameAnchorPreviewSource = ExtractMethodSource(sessionSource, "DrawSurfaceSameAnchorPreview");
        string placeSurfacePlanSource = ExtractMethodSource(sessionSource, "PlaceSurfacePlan");
        int surfacePreviewSafeTransformIndex = surfacePlacementMatrixSource.IndexOf(
            "construct.SafeLocalToGlobal(centerLocal)",
            StringComparison.Ordinal);
        int surfacePreviewFullMatrixFallbackIndex = surfacePlacementMatrixSource.IndexOf(
            "construct.myTransform.localToWorldMatrix * localPlacement",
            StringComparison.Ordinal);
        string symmetrySource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuSymmetry.cs"));
        string themeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditorTheme.cs"));
        Assert(themeSource.Contains("Surface") &&
               plannerSource.Contains("MADCD_PolygonInput.Start") &&
               plannerSource.Contains("DecorationAnchorResolver") &&
               plannerSource.Contains("ConstructSurfaceAnchorResolver") &&
               plannerSource.Contains("ValidateSurfaceFace") &&
               plannerSource.Contains("TryOrientFace") &&
               plannerSource.Contains("TryBuildCoPlanarScalenePolygons") &&
               plannerSource.Contains("CreateRightTrianglePolygon") &&
               plannerSource.Contains("IntendedFaceNormal") &&
               plannerSource.Contains("AlignChildVerticesToParentNormal") &&
               plannerSource.Contains("AlignAxisWithIntendedNormal") &&
               plannerSource.Contains("ShouldReversePolygonForConverter") &&
               plannerSource.Contains("normal did not match the source face normal") &&
               plannerSource.Contains("RoundPlacementPosition") &&
               !plannerSource.Contains("DecorationEditMath.Snap(center - ToVector3(anchor))") &&
               plannerSource.Contains("ThicknessAxis") &&
               plannerSource.Contains("TransformThicknessAxis") &&
               plannerSource.Contains("TransformPlaneDistance") &&
               plannerSource.Contains("DecorationTransformThicknessAxis") &&
               plannerSource.Contains("Quaternion.Euler(degrees) * vector") &&
               plannerSource.Contains("ManagedRotateUnityEuler") &&
               plannerSource.Contains("UnityEulerFromRotationMatrix") &&
               plannerSource.Contains("TryOrientBridgeFace(candidate, orientedFaces") &&
               plannerSource.Contains("preferred.AddRange(alternate)") &&
               sessionSource.Contains("TrySwitchToSurfaceBuilder") &&
               sessionSource.Contains("IsSurfaceMode") &&
               sessionSource.Contains("surface ? \"Surf\" : \"Deco\"") &&
               sessionSource.Contains("Mode: Deco | Tab to Surface when clean") &&
               sessionSource.Contains("Mode: Surface | Tab to Build when clean") &&
               !sessionSource.Contains("ToolButton(DecorationEditorTool.Surface") &&
               sessionSource.Contains("DrawSurfacePanel") &&
               sessionSource.Contains("DrawCompactIconHeader(\"Surface Builder\", \"build\")") &&
               sessionSource.Contains("HandleSurfaceSceneInput") &&
               sessionSource.Contains("DrawSurfaceOverlay") &&
               sessionSource.Contains("SurfaceBuilderTool") &&
               sessionSource.Contains("SurfaceCreateToolButton") &&
               sessionSource.Contains("SurfaceCreateToolLabel") &&
               sessionSource.Contains("SurfaceToolButton(SurfaceBuilderTool.Move") &&
               sessionSource.Contains("SurfaceToolButton(SurfaceBuilderTool.Rotate") &&
               sessionSource.Contains("SurfaceToolButton(SurfaceBuilderTool.Scale") &&
               sessionSource.Contains("ToolButton(DecorationEditorTool.Focus, \"visibility\", \"View\"") &&
               sessionSource.Contains("DrawSurfaceExtraToolsPanel") &&
               !sessionSource.Contains("DrawSurfaceExtraShelf") &&
               sessionSource.Contains("HandleGeneratorSceneInput") &&
               sessionSource.Contains("UseGeneratorSceneInput") &&
               sessionSource.Contains("DrawGeneratorColorEditor") &&
               sessionSource.Contains("DrawGeneratorMaterialEditor") &&
               sessionSource.Contains("DecorationGeneratorPlanner.TryPlanWithSymmetry") &&
               sessionSource.Contains("DrawGeneratorOverlay") &&
               sessionSource.Contains("MaterialReplacement.Us = placement.MaterialReplacement") &&
               sessionSource.Contains("CaptureSurfaceSnapshot") &&
               sessionSource.Contains("CaptureGeneratorEditSnapshot") &&
               sessionSource.Contains("TryRestoreSurfaceDraftHistory") &&
               sessionSource.Contains("TryRestoreGeneratorDraftHistory") &&
               historySource.Contains("SurfaceDraftHistoryCommand") &&
               historySource.Contains("GeneratorDraftHistoryCommand") &&
               generatorSource.Contains("SurfaceExtraTool") &&
               !generatorSource.Contains("DecorationGeneratorStyle") &&
               generatorSource.Contains("DecorationGeneratorDraft") &&
               generatorSource.Contains("DecorationGeneratorSettings") &&
               generatorSource.Contains("MetalPole1mGuid") &&
               generatorSource.Contains("LengthAxis = DecorationEditAxis.Z") &&
               generatorSource.Contains("Diameter = 0.05f") &&
               generatorSource.Contains("ArcDegrees = 360f") &&
               generatorSource.Contains("ShapeHeight = 2f") &&
               generatorSource.Contains("TopRadius = 1f") &&
               generatorSource.Contains("RingCount = 6") &&
               generatorSource.Contains("NearestAnchor = true") &&
               generatorSource.Contains("PartialCircle") &&
               generatorSource.Contains("PartialSphere") &&
               generatorSource.Contains("Cone2D") &&
               generatorSource.Contains("BuildSphereSegments") &&
               generatorSource.Contains("BuildConeSegments") &&
               generatorSource.Contains("BuildFrustumSegments") &&
               generatorSource.Contains("BuildCone2DSegments") &&
               generatorSource.Contains("TryBuildPreviewSegments") &&
               generatorSource.Contains("ArcPoints") &&
               generatorSource.Contains("AddPolylineSegments") &&
               generatorSource.Contains("UsesCenter(SurfaceExtraTool tool)") &&
               plannerSource.Contains("HasSharedAnchor") &&
               plannerSource.Contains("SharedAnchorSelected") &&
               plannerSource.Contains("TrySetSharedAnchor") &&
               plannerSource.Contains("TryMoveSharedAnchor") &&
               plannerSource.Contains("TryValidateExplicitSharedAnchor") &&
               generatorSource.Contains("HasSharedAnchor") &&
               generatorSource.Contains("HasPlaceableGeometry") &&
               generatorSource.Contains("SharedAnchorSelected") &&
               generatorSource.Contains("TrySetSharedAnchor") &&
               generatorSource.Contains("TryMoveSharedAnchor") &&
               generatorSource.Contains("TryValidateExplicitSharedAnchor") &&
               sessionSource.Contains("DrawGeneratorToolButton(SurfaceExtraTool.PartialCircle") &&
               sessionSource.Contains("DrawGeneratorToolButton(SurfaceExtraTool.PartialSphere") &&
               sessionSource.Contains("DrawGeneratorToolButton(SurfaceExtraTool.Cone") &&
               sessionSource.Contains("DrawGeneratorToolButton(SurfaceExtraTool.Frustum") &&
               sessionSource.Contains("DrawGeneratorToolButton(SurfaceExtraTool.Cone2D") &&
               sessionSource.Contains("DrawGeneratorNumberField(\"Arc\"") &&
               sessionSource.Contains("DrawGeneratorNumberField(\"Height\"") &&
               sessionSource.Contains("DrawGeneratorNumberField(\"Top radius\"") &&
               sessionSource.Contains("DrawGeneratorNumberField(\"Rings\"") &&
               sessionSource.Contains("DecorationGeneratorDraft.UsesCenter(_surfaceExtraTool)") &&
               sessionSource.Contains("GeneratorToolDisplayName") &&
               sessionSource.Contains("DecorationGeneratorPlanner.TryBuildPreviewSegments") &&
               sessionSource.Contains("DrawSurfaceSharedAnchorControls") &&
               sessionSource.Contains("DrawGeneratorSharedAnchorControls") &&
               sessionSource.Contains("!_generatorDraft.HasPlaceableGeometry") &&
               sessionSource.Contains("TryHandleSurfaceSharedAnchorClick") &&
               sessionSource.Contains("TryHandleGeneratorSharedAnchorClick") &&
               sessionSource.Contains("BeginSharedAnchorDrag") &&
               sharedAnchorDragSource.Contains("if (magnitude <= 0)") &&
               !sharedAnchorDragSource.Contains("Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(projected)))") &&
               sessionSource.Contains("DrawSharedAnchorTarget") &&
               sessionSource.Contains("_pendingSurfaceSharedAnchorPick") &&
               sessionSource.Contains("_pendingGeneratorSharedAnchorPick") &&
               generatorSource.Contains("CircleTangentA") &&
               generatorSource.Contains("TrySetCircleCenterWithBasis") &&
               generatorSource.Contains("DecorationGeneratorPlan") &&
               generatorSource.Contains("DecorationGeneratorPlacement") &&
               generatorSource.Contains("TryPlanWithSymmetry") &&
               generatorSource.Contains("TryPlanMirroredVariants") &&
               generatorSource.Contains("LookRotationEuler") &&
               generatorSource.Contains("EulerFromTo") &&
               generatorSource.Contains("MaterialReplacement") &&
               sessionSource.Contains("DrawUnifiedSurfaceDraftRows") &&
               sessionSource.Contains("ActiveSurfaceDraftActionTarget") &&
               sessionSource.Contains("PreviewSurfaceActionTarget") &&
               sessionSource.Contains("PlaceSurfaceActionTarget") &&
               sessionSource.Contains("ClearSurfaceActionTarget") &&
               sessionSource.Contains("DeleteSurfaceActionTarget") &&
               sessionSource.Contains("SurfaceContextTargetKind") &&
               sessionSource.Contains("SurfacePointSelection") &&
               sessionSource.Contains("SurfacePoint") &&
               sessionSource.Contains("SurfaceEdge") &&
               sessionSource.Contains("SurfaceFace") &&
               sessionSource.Contains("GeneratorPoint") &&
               sessionSource.Contains("TryOpenSurfaceContextMenu") &&
               sessionSource.Contains("TryOpenSurfacePointSelectionContextMenu") &&
               sessionSource.Contains("TryOpenSurfacePointContextMenu") &&
               sessionSource.Contains("TryOpenSurfaceEdgeContextMenu") &&
               sessionSource.Contains("TryOpenSurfaceFaceContextMenu") &&
               sessionSource.Contains("TryOpenGeneratorPointContextMenu") &&
               sessionSource.Contains("DrawSurfaceContextMenu") &&
               sessionSource.Contains("ConnectSelectedSurfacePoints") &&
               sessionSource.Contains("CreateFaceFromSelectedSurfacePoints") &&
               sessionSource.Contains("LocalNormalFromHit(hit)") &&
               sessionSource.Contains("DecorationPointerProbe.TryGetLocalFaceNormal(hit.Anchor, hit.LocalHit") &&
               sessionSource.Contains("TrySetCircleCenter(hit.Construct, hit.LocalHit, normal") &&
               !sessionSource.Contains("GetCameraCircleBasis") &&
               sessionSource.Contains("DrawSurfaceSameAnchorPreview") &&
               sessionSource.Contains("DrawGeneratorSameAnchorPreview") &&
               sessionSource.Contains("DrawSameAnchorPreview") &&
               sessionSource.Contains("PlannedAnchorLine") &&
               sessionSource.Contains("DrawSurfaceActionBar") &&
               !sessionSource.Contains("DrawGeneratorActionBar") &&
               sessionSource.Contains("DrawGeneratorMeshEditor") &&
               sessionSource.Contains("ResetGeneratorMesh") &&
               sessionSource.Contains("DrawGeneratorDiameterPresets") &&
               sessionSource.Contains("DrawGeneratorAnchorModeControl") &&
               !sessionSource.Contains("DrawGeneratorStyleButton") &&
               sessionSource.Contains("IsSurfaceMode || _tool == DecorationEditorTool.Paint || _showMeshPalette") &&
               sessionSource.Contains("PlaceSurfacePlan") &&
               plannerSource.Contains("TryPlanWithSymmetry") &&
               plannerSource.Contains("TryPlanMirroredVariants") &&
               plannerSource.Contains("CreateMirroredForSymmetry") &&
               plannerSource.Contains("BridgeEdgeSelection") &&
               plannerSource.Contains("ManualFaceSelectionCount") &&
               plannerSource.Contains("FreeTriangleSelectionCount") &&
               plannerSource.Contains("TrySelectManualPointEdge") &&
               plannerSource.Contains("TryCreateFaceFromManualSelection") &&
               plannerSource.Contains("ToggleBridgeEdge") &&
               plannerSource.Contains("TryBridgeSelectedEdges") &&
               plannerSource.Contains("IsBridgeEdgeSelected") &&
               plannerSource.Contains("variant.AxisCount % 2 == 1") &&
               plannerSource.Contains("SameGeometry") &&
               sessionSource.Contains("SurfaceDecorationPlanner.TryPlanWithSymmetry") &&
               sessionSource.Contains("BridgeSurfaceEdges") &&
               sessionSource.Contains("IsShiftHeld") &&
               sessionSource.Contains("Use Clear to remove the surface draft.") &&
               sessionSource.Contains("GUILayout.Height(SurfaceDraftListHeight(contentRect.height))") &&
               sessionSource.Contains("SurfaceSettingsShelfHeight(inner.height, actionHeight, gap)") &&
               sessionSource.Contains("Shift-click two edges, then Bridge, to connect them.") &&
               sessionSource.Contains("DrawMirroredSurfaceOverlay") &&
               sessionSource.Split(new[] { "SymmetryButton(DecorationEditAxis.X)" }, StringSplitOptions.None).Length >= 3 &&
               sessionSource.Split(new[] { "SymmetryButton(DecorationEditAxis.Y)" }, StringSplitOptions.None).Length >= 3 &&
               sessionSource.Split(new[] { "SymmetryButton(DecorationEditAxis.Z)" }, StringSplitOptions.None).Length >= 3 &&
               symmetrySource.Contains("TryGetConstructLocalBounds") &&
               symmetrySource.Contains("construct.GetMin()") &&
               symmetrySource.Contains("construct.GetMax()") &&
               symmetrySource.Contains("PlaneHalfSpan") &&
               symmetrySource.Contains("MinimumPlaneSpan") &&
               symmetrySource.Contains("BoundsCacheSeconds"),
            "Decoration Edit Mode exposes Surface Builder as the second ESU mode in the Tab cycle, backed by ES2 polygon conversion.");

        Assert(plannerSource.Contains("SurfaceFaceStyle") &&
               plannerSource.Contains("FaceStyleAt(faceIndex)") &&
               plannerSource.Contains("Mathf.Clamp(faceStyle.ColorIndex") &&
               plannerSource.Contains("SetAllFaceColors") &&
               sessionSource.Contains("SetSurfaceBuilderPaintColor") &&
               sessionSource.Contains("_surfaceDraft.SetAllFaceColors(value)") &&
               sessionSource.Contains("_surfaceDraft.TrySetFaceColor") &&
               sessionSource.Contains("SyncSharedPaintColorFromSurfaceFace") &&
               sessionSource.Contains("SurfaceFaceLabel") &&
               sessionSource.Contains("| color {4}") &&
               sessionSource.Contains("SurfaceDraftPaintColor") &&
               sessionSource.Contains("DrawMirroredSurfaceOverlay") &&
               plannerSource.Contains("internal StructureBlockType StructureBlockType") &&
               plannerSource.Contains("draft.Settings.StructureBlockType") &&
               sessionSource.Contains("_surfacePreviewMaterials") &&
               sessionSource.Contains("ApplySurfacePreviewPaint") &&
               sessionSource.Contains("SurfaceFallbackMaterialPreviewColor") &&
               sessionSource.Contains("SurfacePlacementMatrix") &&
               meshPreviewRendererSource.Contains("internal Material GetMaterial") &&
               meshPreviewRendererSource.Contains("definition.GetMaterial()") &&
               drawSurfacePlanMeshPreviewSource.Contains("_previewRenderer?.GetMesh(entry)") &&
               drawSurfacePlanMeshPreviewSource.Contains("SurfacePreviewMaterial(placement, entry)") &&
               drawSurfacePlanMeshPreviewSource.Contains("Graphics.DrawMesh(mesh, SurfacePlacementMatrix") &&
               drawSurfaceOverlaySource.Contains("bool drewPreviewMeshes = hasPreview && DrawSurfacePlanMeshPreview(_surfacePlan)") &&
               drawSurfaceOverlaySource.Contains("bool drawDraftFill = !hasPreview || !drewPreviewMeshes") &&
               drawSurfaceOverlaySource.Contains("if (drawDraftFill)") &&
               surfacePreviewMaterialSource.Contains("_previewRenderer?.GetMaterial(entry)") &&
               surfacePreviewMaterialSource.Contains("new Material(sourceMaterial)") &&
               surfacePreviewMaterialSource.Contains("ApplySurfacePreviewPaint(material, placement.Color)") &&
               !surfacePreviewMaterialSource.Contains("ConfigureTransparentMaterial(material)") &&
               historySource.Contains("SurfaceBuilderStyleHistoryCommand"),
            "Surface Builder shares paint color across Draw and Extra Tools, supports per-face surface colors, shows face colors in the draft list, and renders planned mesh/material previews from selected paint.");

        Assert(surfacePlacementMatrixSource.Contains("Quaternion localRotation = Quaternion.Euler(placement.Orientation)") &&
               surfacePlacementMatrixSource.Contains("ConstructRotation(construct) * localRotation") &&
               surfacePreviewSafeTransformIndex >= 0 &&
               surfacePreviewFullMatrixFallbackIndex > surfacePreviewSafeTransformIndex,
            "Surface Builder material previews use construct local-to-global position/rotation before the full matrix fallback, avoiding subobject scale or shear distortion.");

        Assert(!createFaceFromSelectedSurfacePointsSource.Contains("RebuildSurfacePreview") &&
               !commitSharedAnchorDragSource.Contains("RebuildSurfacePreview") &&
               drawSurfaceSameAnchorPreviewSource.Contains("TryBuildSurfacePlan(out plan") &&
               !drawSurfaceSameAnchorPreviewSource.Contains("RebuildSurfacePreview") &&
               placeSurfacePlanSource.Contains("TryBuildSurfacePlan(out SurfaceDecorationPlan plan") &&
               !placeSurfacePlanSource.Contains("RebuildSurfacePreview"),
            "Surface Builder only stores material preview plans from explicit Preview actions; create-face, same-anchor guides, and Place use invalidation or temporary plans.");

        Assert(surfaceExtraToolsPanelSource.Contains("_surfaceExtraToolsViewportHeight = Mathf.Max") &&
               surfaceExtraToolsPanelSource.Contains("GUILayout.Height(_surfaceExtraToolsViewportHeight)") &&
               sessionSource.Contains("private bool _showGeneratorMaterialPicker = true;") &&
               generatorMaterialEditorSource.Contains("_generatorMaterialListHeight = 0f") &&
               generatorMaterialEditorSource.Contains("GeneratorMaterialListViewportHeight") &&
               !generatorMaterialEditorSource.Contains("EsuHudLayout.Scale(220f)") &&
               generatorMaterialListViewportHeightSource.Contains("_surfaceExtraToolsViewportHeight - Mathf.Max(0f, footerReserve)") &&
               generatorMaterialListViewportHeightSource.Contains("GUILayoutUtility.GetLastRect()") &&
               generatorMaterialListViewportHeightSource.Contains("_generatorScroll.y + _surfaceExtraToolsViewportHeight") &&
               generatorMaterialListViewportHeightSource.Contains("_generatorMaterialListHeight = Mathf.Clamp"),
            "Surface Builder Extra Tools material picker opens by default and sizes from the active panel viewport instead of a fixed list height.");

        int surfacePanelIndex = sessionSource.IndexOf("private void DrawSurfacePanel", StringComparison.Ordinal);
        int surfaceHelperIndex = sessionSource.IndexOf("Shift-click points, then right-click", surfacePanelIndex, StringComparison.Ordinal);
        int surfaceSettingsShelfIndex = sessionSource.IndexOf("GUILayout.BeginArea(settingsRect)", surfacePanelIndex, StringComparison.Ordinal);
        int surfaceSettingsIndex = sessionSource.IndexOf("DrawSurfaceSettings();", surfaceSettingsShelfIndex, StringComparison.Ordinal);
        int surfaceActionBarIndex = sessionSource.IndexOf("DrawSurfaceActionBar(actionRect)", surfaceSettingsIndex, StringComparison.Ordinal);
        Assert(surfaceHelperIndex >= 0 &&
               surfaceSettingsShelfIndex > surfaceHelperIndex &&
               surfaceSettingsIndex > surfaceSettingsShelfIndex &&
               surfaceActionBarIndex > surfaceSettingsIndex &&
               sessionSource.Contains("private float SurfaceDraftListHeight(float contentHeight)") &&
               sessionSource.Contains("UnifiedSurfaceDraftRowCount()") &&
               sessionSource.Contains("DrawUnifiedSurfaceDraftRows();") &&
               sessionSource.Contains("SetSurfaceBuilderTool(SurfaceBuilderTool.Draw)") &&
               sessionSource.Contains("Current surface paint color #") &&
               !sessionSource.Contains("GUILayout.ExpandHeight(true))"),
            "Surface Builder caps the unified draft list height, exposes Draw in the left panel, and pins material, thickness, color, and anchor settings above the action buttons.");

        int drawTopToolbar = sessionSourceNormalized.IndexOf("private void DrawTopToolbar", StringComparison.Ordinal);
        int surfaceTopbar = sessionSourceNormalized.IndexOf("if (IsSurfaceMode)\n                {", drawTopToolbar, StringComparison.Ordinal);
        int surfaceTopbarElse = sessionSourceNormalized.IndexOf("\n                }\n                else", surfaceTopbar, StringComparison.Ordinal);
        string surfaceTopbarBlock = surfaceTopbar >= 0 && surfaceTopbarElse > surfaceTopbar
            ? sessionSourceNormalized.Substring(surfaceTopbar, surfaceTopbarElse - surfaceTopbar)
            : string.Empty;
        int rightControls = sessionSourceNormalized.IndexOf("GUILayout.BeginHorizontal(GUILayout.Width(budget.RightControlsWidth));", drawTopToolbar, StringComparison.Ordinal);
        int surfaceRightControls = sessionSourceNormalized.IndexOf("if (IsSurfaceMode)\n            {", rightControls, StringComparison.Ordinal);
        int surfaceRightControlsElse = sessionSourceNormalized.IndexOf("\n            }\n            else", surfaceRightControls, StringComparison.Ordinal);
        string surfaceRightControlsBlock = surfaceRightControls >= 0 && surfaceRightControlsElse > surfaceRightControls
            ? sessionSourceNormalized.Substring(surfaceRightControls, surfaceRightControlsElse - surfaceRightControls)
            : string.Empty;
        Assert(surfaceTopbarBlock.Contains("SurfaceCreateToolButton") &&
               !surfaceTopbarBlock.Contains("SurfaceToolButton(SurfaceBuilderTool.Draw") &&
               !surfaceTopbarBlock.Contains("SurfaceToolButton(SurfaceBuilderTool.Path") &&
               !surfaceTopbarBlock.Contains("SurfaceToolButton(SurfaceBuilderTool.Circle") &&
               surfaceTopbarBlock.Contains("SurfaceToolButton(SurfaceBuilderTool.Move") &&
               surfaceTopbarBlock.Contains("SurfaceToolButton(SurfaceBuilderTool.Rotate") &&
               surfaceTopbarBlock.Contains("SurfaceToolButton(SurfaceBuilderTool.Scale") &&
               surfaceTopbarBlock.Contains("ToolButton(DecorationEditorTool.Focus, \"visibility\", \"View\"") &&
               surfaceRightControlsBlock.Contains("\"Tools\"") &&
               !surfaceRightControlsBlock.Contains("\"Insp\"") &&
               !surfaceRightControlsBlock.Contains("\"Pal\"") &&
               !surfaceRightControlsBlock.Contains("\"Out\"") &&
               !surfaceRightControlsBlock.Contains("\"Anch\""),
            "Surface Builder top bar exposes one cycling create tool plus Move/Rotate/Scale/View and only the Tools panel toggle.");

        int drawLeftPanel = sessionSourceNormalized.IndexOf("private void DrawLeftPanelStack", StringComparison.Ordinal);
        int drawRightPanel = sessionSourceNormalized.IndexOf("private void DrawRightPanel", StringComparison.Ordinal);
        string drawLeftPanelBlock = drawLeftPanel >= 0 && drawRightPanel > drawLeftPanel
            ? sessionSourceNormalized.Substring(drawLeftPanel, drawRightPanel - drawLeftPanel)
            : string.Empty;
        Assert(drawLeftPanelBlock.Contains("if (IsSurfaceMode)") &&
               drawLeftPanelBlock.Contains("ClearStackDividerDrag(StackDividerKind.Left)") &&
               drawLeftPanelBlock.Contains("DrawMeshPalette(stackRect)") &&
               drawLeftPanelBlock.Contains("return;"),
            "Surface Builder uses the full left panel stack instead of splitting with the Inspector.");

        int drawRightPanelEnd = sessionSourceNormalized.IndexOf("private static void SplitVerticalStack", drawRightPanel, StringComparison.Ordinal);
        string drawRightPanelBlock = drawRightPanel >= 0 && drawRightPanelEnd > drawRightPanel
            ? sessionSourceNormalized.Substring(drawRightPanel, drawRightPanelEnd - drawRightPanel)
            : string.Empty;
        Assert(drawRightPanelBlock.Contains("DrawSurfaceExtraToolsPanel(rightRect)") &&
               drawRightPanelBlock.Contains("return;") &&
               !drawRightPanelBlock.Contains("DrawGeneratorActionBar") &&
               drawRightPanelBlock.IndexOf("DrawSurfaceExtraToolsPanel", StringComparison.Ordinal) <
               drawRightPanelBlock.IndexOf("DrawOutlinerPanel", StringComparison.Ordinal),
            "Surface Builder right panel draws settings-only Extra Tools before returning instead of showing Outliner, Selected Anchor, or duplicate action buttons.");

        int surfaceInput = sessionSourceNormalized.IndexOf("private void HandleSurfaceSceneInput()", StringComparison.Ordinal);
        int surfaceRightClick = sessionSourceNormalized.IndexOf("if (Input.GetMouseButtonDown(1))", surfaceInput, StringComparison.Ordinal);
        int surfaceLeftClick = sessionSourceNormalized.IndexOf("if (!Input.GetMouseButtonDown(0))", surfaceRightClick, StringComparison.Ordinal);
        string surfaceRightClickBlock = surfaceRightClick >= 0 && surfaceLeftClick > surfaceRightClick
            ? sessionSourceNormalized.Substring(surfaceRightClick, surfaceLeftClick - surfaceRightClick)
            : string.Empty;
        int generatorInput = sessionSourceNormalized.IndexOf("private void HandleGeneratorSceneInput()", StringComparison.Ordinal);
        int generatorRightClick = sessionSourceNormalized.IndexOf("if (Input.GetMouseButtonDown(1))", generatorInput, StringComparison.Ordinal);
        int generatorLeftClick = sessionSourceNormalized.IndexOf("if (!Input.GetMouseButtonDown(0))", generatorRightClick, StringComparison.Ordinal);
        string generatorRightClickBlock = generatorRightClick >= 0 && generatorLeftClick > generatorRightClick
            ? sessionSourceNormalized.Substring(generatorRightClick, generatorLeftClick - generatorRightClick)
            : string.Empty;
        Assert(surfaceRightClickBlock.Contains("TryOpenSurfaceContextMenu()") &&
               surfaceRightClickBlock.Contains("HasActiveSelection") &&
               surfaceRightClickBlock.Contains("ClearSelection") &&
               surfaceRightClickBlock.Contains("Use Clear to remove the surface draft.") &&
               generatorRightClickBlock.Contains("TryOpenGeneratorPointContextMenu()") &&
               generatorRightClickBlock.Contains("HasActiveSelection") &&
               generatorRightClickBlock.Contains("ClearSelection") &&
               generatorRightClickBlock.Contains("Use Clear to remove the draft.") &&
               !surfaceRightClickBlock.Contains("ClearSurfaceDraft"),
            "Surface Builder right-click opens surface point/edge/face context menus first, then clears only selection state and leaves full draft removal to the Clear button.");
        int surfaceContextOpen = sessionSourceNormalized.IndexOf("private bool TryOpenSurfaceContextMenu()", StringComparison.Ordinal);
        int surfaceContextOpenEnd = sessionSourceNormalized.IndexOf("private bool TryOpenSurfacePointContextMenu()", surfaceContextOpen, StringComparison.Ordinal);
        string surfaceContextOpenBlock = surfaceContextOpen >= 0 && surfaceContextOpenEnd > surfaceContextOpen
            ? sessionSourceNormalized.Substring(surfaceContextOpen, surfaceContextOpenEnd - surfaceContextOpen)
            : string.Empty;
        int selectedPointSetPick = surfaceContextOpenBlock.IndexOf("TryOpenSurfacePointSelectionContextMenu(requirePointUnderMouse: true)", StringComparison.Ordinal);
        int pointPick = surfaceContextOpenBlock.IndexOf("TryOpenSurfacePointContextMenu()", StringComparison.Ordinal);
        int edgePick = surfaceContextOpenBlock.IndexOf("TryOpenSurfaceEdgeContextMenu()", StringComparison.Ordinal);
        int facePick = surfaceContextOpenBlock.IndexOf("TryOpenSurfaceFaceContextMenu()", StringComparison.Ordinal);
        int selectedPointSetFallback = surfaceContextOpenBlock.IndexOf("TryOpenSurfacePointSelectionContextMenu(requirePointUnderMouse: false)", StringComparison.Ordinal);
        Assert(selectedPointSetPick >= 0 &&
               pointPick > selectedPointSetPick &&
               edgePick > pointPick &&
               facePick > edgePick &&
               selectedPointSetFallback > facePick &&
               sessionSource.Contains("RebuildSurfacePreview(showMessage: true)") &&
               sessionSource.Contains("BridgeSurfaceEdges();") &&
               sessionSource.Contains("ConnectSelectedSurfacePoints();") &&
               sessionSource.Contains("CreateFaceFromSelectedSurfacePoints();") &&
               sessionSource.Contains("DrawSelectedSurfacePointSetPreview") &&
               sessionSource.Contains("DeleteSurfaceActionTarget(generator ? SurfaceDraftActionTarget.Generator : SurfaceDraftActionTarget.Surface)") &&
               sessionSource.Contains("SelectSurfaceContextTarget"),
            "Surface Builder context menus try selected point sets, point, edge, then face targets and route preview, connect, face creation, bridge, and delete through existing helpers.");

        int handleScene = sessionSourceNormalized.IndexOf("private void HandleSceneInput()", StringComparison.Ordinal);
        int pendingInScene = sessionSourceNormalized.IndexOf(
            "if (DecoLimitLifter.EsuSymmetry.PendingAxis != DecorationEditAxis.None)",
            handleScene,
            StringComparison.Ordinal);
        int surfaceInScene = sessionSourceNormalized.IndexOf(
            "if (_tool == DecorationEditorTool.Surface)",
            handleScene,
            StringComparison.Ordinal);
        Assert(handleScene >= 0 &&
               pendingInScene > handleScene &&
               surfaceInScene > pendingInScene,
            "Decoration Edit Mode handles pending symmetry plane placement before Surface Builder point placement.");
    }

    private static SurfaceDraft SurfaceDraftForTests(params Vector3[] points)
    {
        var draft = new SurfaceDraft();
        draft.SetConstructForTests(null);
        foreach (Vector3 point in points)
            draft.AddPointForTests(point);
        if (points.Length >= 3 && !draft.TryAddFace(0, 1, 2, out string message))
            throw new InvalidOperationException(message);
        return draft;
    }

    private static SurfaceDraft SlantedTwoTriangleSurfaceDraftForTests()
    {
        var draft = new SurfaceDraft();
        draft.SetConstructForTests(null);
        var points = new[]
        {
            new Vector3(-1.5f, 0f, -0.6f),
            new Vector3(-0.4f, 1f, 0.04f),
            new Vector3(0.2f, 0f, 0.08f),
            new Vector3(0.8f, 0f, 0.32f),
            new Vector3(1.4f, 1f, 0.76f),
            new Vector3(2.5f, 0f, 1f)
        };
        foreach (Vector3 point in points)
            draft.AddPointForTests(point);

        if (!draft.TryAddFace(0, 1, 2, out string message) ||
            !draft.TryAddFace(3, 4, 5, out message))
        {
            throw new InvalidOperationException(message);
        }

        return draft;
    }

    private static void AssertMirroredSurfaceNormals(
        string shapeName,
        Vector3 a,
        Vector3 b,
        Vector3 c)
    {
        var axisSets = new[]
        {
            new[] { DecorationEditAxis.X },
            new[] { DecorationEditAxis.Y },
            new[] { DecorationEditAxis.Z },
            new[] { DecorationEditAxis.X, DecorationEditAxis.Y },
            new[] { DecorationEditAxis.X, DecorationEditAxis.Y, DecorationEditAxis.Z }
        };

        for (int reversalIndex = 0; reversalIndex < 2; reversalIndex++)
        {
            bool normalReversal = reversalIndex == 1;
            foreach (DecorationEditAxis[] axes in axisSets)
            {
                SurfaceDraft draft = SurfaceDraftForTests(a, b, c);
                draft.Settings.NearestAnchor = false;
                draft.Settings.NormalReversal = normalReversal;
                try
                {
                    EsuSymmetry.Clear();
                    for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
                        EsuSymmetry.SetPlaneForTests(axes[axisIndex], 0);

                    IReadOnlyList<EsuSymmetry.SymmetryVariant> variants = EsuSymmetry.Variants();
                    Assert(SurfaceDecorationPlanner.TryPlanMirroredVariants(
                               draft,
                               new SetSurfaceAnchorResolver(new[] { new Vector3i(0, 0, 0) }),
                               variants,
                               out SurfaceDecorationPlan plan,
                               out string message) &&
                           SurfacePlacementsMatchVariantNormals(draft, variants, plan),
                        "Surface symmetry keeps " +
                        shapeName +
                        " committed normals aligned with preview normals across " +
                        string.Join("+", axes.Select(axis => axis.ToString()).ToArray()) +
                        (normalReversal ? " with normal reversal." : ".") +
                        " Planner said: " +
                        message);
                }
                finally
                {
                    EsuSymmetry.Clear();
                }
            }
        }
    }

    private static bool SurfacePlacementsMatchVariantNormals(
        SurfaceDraft draft,
        IReadOnlyList<EsuSymmetry.SymmetryVariant> variants,
        SurfaceDecorationPlan plan)
    {
        if (draft == null || variants == null || plan == null || plan.Placements.Count == 0)
            return false;

        var normals = new List<Vector3>();
        foreach (EsuSymmetry.SymmetryVariant variant in variants)
        {
            SurfaceDraft variantDraft = variant.IsIdentity
                ? draft
                : draft.CreateMirroredForSymmetry(variant);
            if (normals.Any(existing => SurfaceSameNormal(existing, SurfaceDraftFirstNormal(variantDraft))))
                continue;

            Vector3 normal = SurfaceDraftFirstNormal(variantDraft);
            if (normal == Vector3.zero)
                return false;
            normals.Add(normal);
        }

        if (normals.Count == 0)
            return false;

        foreach (SurfaceDecorationPlacement placement in plan.Placements)
        {
            if (!SurfaceAxisMatchesAnyNormal(placement.TransformThicknessAxis, normals) ||
                !SurfaceAxisMatchesAnyNormal(placement.ThicknessAxis, normals))
            {
                return false;
            }
        }

        for (int normalIndex = 0; normalIndex < normals.Count; normalIndex++)
        {
            Vector3 normal = normals[normalIndex];
            if (!plan.Placements.Any(placement =>
                    Vector3.Dot(placement.TransformThicknessAxis, normal) >= 0.999f))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SurfacePlacementsMatchDraftNormal(
        SurfaceDraft draft,
        IReadOnlyList<SurfaceDecorationPlacement> placements)
    {
        if (draft == null || placements == null || placements.Count == 0)
            return false;

        Vector3 normal = SurfaceDraftFirstNormal(draft);
        if (normal == Vector3.zero)
            return false;

        for (int index = 0; index < placements.Count; index++)
        {
            if (Vector3.Dot(placements[index].TransformThicknessAxis, normal) < 0.999f ||
                Vector3.Dot(placements[index].ThicknessAxis, normal) < 0.999f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SurfacePlacementCentersSitBehindDraftNormal(
        SurfaceDraft draft,
        IReadOnlyList<SurfaceDecorationPlacement> placements)
    {
        if (draft == null || placements == null || placements.Count == 0 || draft.Faces.Count == 0)
            return false;

        SurfaceFace face = draft.Faces[0];
        Vector3 normal = SurfaceDraftFirstNormal(draft);
        if (normal == Vector3.zero)
            return false;

        float plane = Vector3.Dot(normal, draft.Points[face.A]);
        float expectedCenterPlane = plane - draft.Settings.FaceThickness * 0.5f;
        for (int index = 0; index < placements.Count; index++)
        {
            Vector3 center = PlacementCenter(placements[index]);
            float centerPlane = Vector3.Dot(normal, center);
            if (Mathf.Abs(centerPlane - expectedCenterPlane) > 0.002f)
                return false;
        }

        return true;
    }

    private static bool SurfacePlacementsMatchAnyDraftFaceNormal(
        SurfaceDraft draft,
        IReadOnlyList<SurfaceDecorationPlacement> placements)
    {
        if (draft == null || placements == null || placements.Count == 0)
            return false;

        IReadOnlyList<Vector3> normals = SurfaceDraftFaceNormals(draft);
        if (normals.Count == 0)
            return false;

        for (int index = 0; index < placements.Count; index++)
        {
            if (!SurfaceAxisMatchesAnyNormal(placements[index].TransformThicknessAxis, normals) ||
                !SurfaceAxisMatchesAnyNormal(placements[index].ThicknessAxis, normals))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<Vector3> SurfaceDraftFaceNormals(SurfaceDraft draft)
    {
        var normals = new List<Vector3>();
        if (draft == null)
            return normals;

        foreach (SurfaceFace face in draft.Faces)
        {
            Vector3 axis = Vector3.Cross(
                draft.Points[face.B] - draft.Points[face.A],
                draft.Points[face.C] - draft.Points[face.A]);
            if (!DecorationEditMath.IsFinite(axis) || axis.sqrMagnitude <= 0.000000000001f)
                continue;

            axis.Normalize();
            if (draft.Settings.NormalReversal)
                axis = -axis;
            if (!normals.Any(existing => SurfaceSameNormal(existing, axis)))
                normals.Add(axis);
        }

        return normals;
    }

    private static Vector3 SurfaceDraftFirstNormal(SurfaceDraft draft)
    {
        if (draft == null || draft.Faces.Count == 0)
            return Vector3.zero;

        SurfaceFace face = draft.Faces[0];
        Vector3 axis = Vector3.Cross(
            draft.Points[face.B] - draft.Points[face.A],
            draft.Points[face.C] - draft.Points[face.A]);
        if (!DecorationEditMath.IsFinite(axis) || axis.sqrMagnitude <= 0.000000000001f)
            return Vector3.zero;

        axis.Normalize();
        return draft.Settings.NormalReversal ? -axis : axis;
    }

    private static bool SurfaceAxisMatchesAnyNormal(
        Vector3 axis,
        IReadOnlyList<Vector3> normals)
    {
        if (axis.sqrMagnitude < 0.999f)
            return false;

        for (int index = 0; index < normals.Count; index++)
        {
            if (Vector3.Dot(axis, normals[index]) >= 0.999f)
                return true;
        }

        return false;
    }

    private static bool SurfaceSameNormal(Vector3 left, Vector3 right) =>
        left != Vector3.zero &&
        right != Vector3.zero &&
        Vector3.Dot(left, right) >= 0.999f;

    private static void AssertSurfacePlanCount(
        SurfaceDraft draft,
        ISurfaceAnchorResolver resolver,
        int expected,
        string description)
    {
        if (!SurfaceDecorationPlanner.TryPlan(draft, resolver, out SurfaceDecorationPlan plan, out string message))
            throw new InvalidOperationException("Verification failed: " + description + " Planner said: " + message);
        Assert(plan.DecorationCount == expected,
            description + " Expected " + expected.ToString(CultureInfo.InvariantCulture) +
            ", got " + plan.DecorationCount.ToString(CultureInfo.InvariantCulture) + ".");
    }

    private static void AssertGeneratorShapePlanCount(
        SurfaceExtraTool tool,
        DecorationGeneratorSettings settings,
        int expected,
        string description)
    {
        var draft = new DecorationGeneratorDraft();
        draft.SetTool(tool);
        if (!draft.TrySetCircleCenter(null, Vector3.zero, Vector3.up, out string centerMessage))
            throw new InvalidOperationException("Verification failed: " + description + " Center said: " + centerMessage);

        if (!DecorationGeneratorPlanner.TryBuildPreviewSegments(
                draft,
                settings,
                out IReadOnlyList<DecorationGeneratorSegment> segments,
                out string previewMessage))
        {
            throw new InvalidOperationException("Verification failed: " + description + " Preview said: " + previewMessage);
        }

        if (!DecorationGeneratorPlanner.TryPlan(
                draft,
                settings,
                new SetSurfaceAnchorResolver(new[] { new Vector3i(0, 0, 0) }),
                out DecorationGeneratorPlan plan,
                out string planMessage))
        {
            throw new InvalidOperationException("Verification failed: " + description + " Planner said: " + planMessage);
        }

        Assert(segments.Count == expected &&
               plan.DecorationCount == expected,
            description + " Expected " + expected.ToString(CultureInfo.InvariantCulture) +
            ", got preview " + segments.Count.ToString(CultureInfo.InvariantCulture) +
            " and plan " + plan.DecorationCount.ToString(CultureInfo.InvariantCulture) + ".");
    }

    private static bool SurfaceSharedEdgesAreOpposed(IReadOnlyList<SurfaceFace> faces)
    {
        for (int left = 0; left < faces.Count; left++)
        {
            foreach (SurfaceEdge edge in faces[left].Edges())
            {
                for (int right = left + 1; right < faces.Count; right++)
                {
                    if (faces[right].ContainsEdge(edge.A, edge.B) &&
                        faces[right].HasDirectedEdge(edge.A, edge.B))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool SurfacePlacementThicknessAxesAreCoherent(
        IReadOnlyList<SurfaceDecorationPlacement> placements)
    {
        if (placements == null || placements.Count < 2)
            return false;

        Vector3 reference = placements[0].ThicknessAxis;
        if (reference.sqrMagnitude < 0.999f)
            return false;

        for (int index = 1; index < placements.Count; index++)
        {
            Vector3 axis = placements[index].ThicknessAxis;
            if (axis.sqrMagnitude < 0.999f ||
                Vector3.Dot(reference, axis) < 0.999f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SurfacePlacementTransformPlanesAreCoherent(
        IReadOnlyList<SurfaceDecorationPlacement> placements)
    {
        if (placements == null || placements.Count < 2)
            return false;

        Vector3 referenceAxis = placements[0].TransformThicknessAxis;
        float referenceDistance = placements[0].TransformPlaneDistance;
        if (referenceAxis.sqrMagnitude < 0.999f ||
            float.IsNaN(referenceDistance) ||
            float.IsInfinity(referenceDistance))
        {
            return false;
        }

        for (int index = 1; index < placements.Count; index++)
        {
            Vector3 axis = placements[index].TransformThicknessAxis;
            float distance = placements[index].TransformPlaneDistance;
            if (axis.sqrMagnitude < 0.999f ||
                Vector3.Dot(referenceAxis, axis) < 0.999f ||
                float.IsNaN(distance) ||
                float.IsInfinity(distance) ||
                Mathf.Abs(referenceDistance - distance) > 0.002f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SurfacePlacementsSharePositionButNotTransform(
        IReadOnlyList<SurfaceDecorationPlacement> placements)
    {
        if (placements == null || placements.Count != 2)
            return false;

        return SameVector(placements[0].Positioning, placements[1].Positioning) &&
               (!SameVector(placements[0].Scaling, placements[1].Scaling) ||
                !SameVector(placements[0].Orientation, placements[1].Orientation));
    }

    private static bool SameVector(Vector3 left, Vector3 right) =>
        Mathf.Abs(left.x - right.x) <= 0.0001f &&
        Mathf.Abs(left.y - right.y) <= 0.0001f &&
        Mathf.Abs(left.z - right.z) <= 0.0001f;

    private static Vector3 PlacementCenter(SurfaceDecorationPlacement placement) =>
        new Vector3(
            placement.Anchor.x,
            placement.Anchor.y,
            placement.Anchor.z) + placement.Positioning;

    private static bool SameCell(Vector3i left, Vector3i right) =>
        left.x == right.x &&
        left.y == right.y &&
        left.z == right.z;

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

        IReadOnlyList<SmartBuildMaterial> materials = SmartBlockFamilyCatalog.BasicMaterials;
        Assert(materials.Count == 8 &&
               materials.Contains(SmartBuildMaterial.Wood) &&
               materials.Contains(SmartBuildMaterial.Stone) &&
               materials.Contains(SmartBuildMaterial.Metal) &&
               materials.Contains(SmartBuildMaterial.Alloy) &&
               materials.Contains(SmartBuildMaterial.Glass) &&
               materials.Contains(SmartBuildMaterial.Lead) &&
               materials.Contains(SmartBuildMaterial.HeavyArmour) &&
               materials.Contains(SmartBuildMaterial.Rubber) &&
               SmartBlockFamilyCatalog.MaterialDisplayName(SmartBuildMaterial.HeavyArmour) == "Heavy armour",
            "Smart Block Builder exposes all 8 basic structural materials through its internal picker.");

        EsuSymmetry.Clear();
        EsuSymmetry.SetPlaneForTests(DecorationEditAxis.X, 0);
        Vector3i[] mirroredTargets = EsuSymmetry.MirrorCells(new[]
            {
                new Vector3i(1, 0, 0),
                new Vector3i(2, 0, 0)
            })
            .ToArray();
        SmartBuildPlan mirroredPlan = SmartBuildPlanner.BuildPlanFromCells(
            line,
            mirroredTargets,
            SmartBuildAxis.X,
            SmartBlockFamily.ForTests(1),
            _ => false);
        Assert(mirroredPlan.CanCommit &&
               mirroredPlan.CoveredCellCount == 4 &&
               mirroredTargets.Select(EsuSymmetry.CellKey).Distinct().Count() == 4,
            "Smart Block Builder can plan mirrored composite target cells without duplicate placements.");
        EsuSymmetry.Clear();

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

        MethodInfo downSlopeGeometryParser = typeof(SmartBlockFamilyCatalog)
            .GetMethod(
                "TryLengthFromDownSlopeGeometry",
                BindingFlags.Static | BindingFlags.NonPublic);
        bool downSlopeGeometryNamesParse = downSlopeGeometryParser != null;
        for (int length = 1; length <= 4 && downSlopeGeometryNamesParse; length++)
        {
            object[] args =
            {
                "DownSlope" + length.ToString(CultureInfo.InvariantCulture) + "m",
                0
            };
            downSlopeGeometryNamesParse =
                (bool)downSlopeGeometryParser.Invoke(null, args) &&
                (int)args[1] == length;
        }

        Assert(downSlopeGeometryNamesParse,
            "Smart Builder catalog parses DownSlope1m..4m geometry metadata for runtime slope discovery.");

        var structuralGeometryChecks = new List<Tuple<string, string, int>>();
        for (int length = 1; length <= 4; length++)
        {
            string len = length.ToString(CultureInfo.InvariantCulture);
            structuralGeometryChecks.Add(Tuple.Create("Pole" + len + "m", "pole", length));
            structuralGeometryChecks.Add(Tuple.Create("Wedge" + len + "m", "wedge", length));
            structuralGeometryChecks.Add(Tuple.Create("Wedge" + len + "mFront", "wedge-front", length));
            structuralGeometryChecks.Add(Tuple.Create("Wedge" + len + "mBack", "wedge-back", length));
            structuralGeometryChecks.Add(Tuple.Create("LeftSquareCorner" + len + "m", "square-corner-left", length));
            structuralGeometryChecks.Add(Tuple.Create("RightSquareCorner" + len + "m", "square-corner-right", length));
            structuralGeometryChecks.Add(Tuple.Create("LeftTriangleCorner" + len + "m", "triangle-corner-left", length));
            structuralGeometryChecks.Add(Tuple.Create("RightTriangleCorner" + len + "m", "triangle-corner-right", length));
            structuralGeometryChecks.Add(Tuple.Create("LeftInvertedTriangleCorner" + len + "m", "inverted-triangle-corner-left", length));
            structuralGeometryChecks.Add(Tuple.Create("RightInvertedTriangleCorner" + len + "m", "inverted-triangle-corner-right", length));
            structuralGeometryChecks.Add(Tuple.Create("Offset" + len + "mSlopeLeft", "offset-slope-left", length));
            structuralGeometryChecks.Add(Tuple.Create("Offset" + len + "mSlopeRight", "offset-slope-right", length));
        }

        for (int length = 2; length <= 4; length++)
        {
            string len = length.ToString(CultureInfo.InvariantCulture);
            structuralGeometryChecks.Add(Tuple.Create("SquareBacked" + len + "mCornerLeft", "square-backed-corner-left", length));
            structuralGeometryChecks.Add(Tuple.Create("SquareBacked" + len + "mCornerRight", "square-backed-corner-right", length));
            structuralGeometryChecks.Add(Tuple.Create("LeftFacingDownSlope1m_" + len + "mLong", "facing-down-slope-left", length));
            structuralGeometryChecks.Add(Tuple.Create("RightFacingDownSlope1m_" + len + "mLong", "facing-down-slope-right", length));
        }

        int[] transitionStarts = { 1, 2, 3 };
        foreach (int from in transitionStarts)
        {
            for (int to = from + 1; to <= 4; to++)
            {
                string suffix = from.ToString(CultureInfo.InvariantCulture) +
                                "mTo" +
                                to.ToString(CultureInfo.InvariantCulture) +
                                "m";
                structuralGeometryChecks.Add(Tuple.Create("Slope" + suffix + "TransitionLeft", "slope-transition-" + from + "-" + to + "-left", to));
                structuralGeometryChecks.Add(Tuple.Create("Slope" + suffix + "TransitionRight", "slope-transition-" + from + "-" + to + "-right", to));
                structuralGeometryChecks.Add(Tuple.Create("Slope" + suffix + "InverseTransitionLeft", "slope-inverse-transition-" + from + "-" + to + "-left", to));
                structuralGeometryChecks.Add(Tuple.Create("Slope" + suffix + "InverseTransitionRight", "slope-inverse-transition-" + from + "-" + to + "-right", to));
            }
        }

        bool structuralGeometryNamesParse = structuralGeometryChecks.All(check =>
            SmartBuildShapeDescriptors.TryParseGeometryName(check.Item1, out SmartBuildGeometryInfo info) &&
            info.Descriptor.Key == check.Item2 &&
            info.Length == check.Item3);
        Assert(structuralGeometryNamesParse &&
               structuralGeometryChecks.Count >= 80,
            "Smart Builder structural shape descriptors parse vanilla pole, wedge, corner, transition, facing-slope, and offset-slope geometry metadata.");

        var downSlopeFamily = new SmartBlockFamily(
            "test down slopes",
            Enumerable.Range(1, 4)
                .Select(length => new SmartBlockCandidate(
                    length.ToString(CultureInfo.InvariantCulture) + "m test down slope",
                    length,
                    null,
                    SmartBuildShapeKind.DownSlope,
                    "DownSlope" + length.ToString(CultureInfo.InvariantCulture) + "m")));
        SmartBuildShapeDescriptor triangleLeftDescriptor =
            SmartBuildShapeDescriptors.ByKey("triangle-corner-left");
        SmartBuildShapeDescriptor triangleRightDescriptor =
            SmartBuildShapeDescriptors.ByKey("triangle-corner-right");
        var fixedShapeFamilies = new Dictionary<string, SmartBlockFamily>(StringComparer.OrdinalIgnoreCase)
        {
            [triangleLeftDescriptor.Key] = new SmartBlockFamily(
                "test left triangles",
                new[]
                {
                    new SmartBlockCandidate(
                        "2m left triangle",
                        2,
                        null,
                        triangleLeftDescriptor.Kind,
                        "LeftTriangleCorner2m",
                        triangleLeftDescriptor,
                        "LeftTriangleCorner2m")
                }),
            [triangleRightDescriptor.Key] = new SmartBlockFamily(
                "test right triangles",
                new[]
                {
                    new SmartBlockCandidate(
                        "2m right triangle",
                        2,
                        null,
                        triangleRightDescriptor.Kind,
                        "RightTriangleCorner2m",
                        triangleRightDescriptor,
                        "RightTriangleCorner2m")
                })
        };
        var smartSource = new SmartBuildSource(
            null,
            Guid.Empty,
            "test material",
            new Vector3i(1, 1, 1),
            SmartBlockFamily.ForTests(1, 2, 3, 4),
            downSlopeFamily,
            fixedShapeFamilies);
        SmartBuildShapeDescriptor circleDescriptor =
            SmartBuildShapeDescriptors.ByKey("generated-circle");
        SmartBuildShapeDescriptor polygonDescriptor =
            SmartBuildShapeDescriptors.ByKey("generated-polygon");
        SmartBuildShapeDescriptor sphereDescriptor =
            SmartBuildShapeDescriptors.ByKey("generated-sphere");
        SmartBuildPiece circleGenerator = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(20, 0, 0),
            circleDescriptor,
            SmartBuildAxis.Z,
            1,
            width: 5,
            drawPlane: SmartBuildDrawPlane.Camera);
        int circleShellCells = circleGenerator.EnumeratePreviewCells(smartSource).Count();
        circleGenerator.SetGeneratorFillMode(SmartBuildGeneratorFillMode.Filled);
        int circleFilledCells = circleGenerator.EnumeratePreviewCells(smartSource).Count();
        Assert(circleDescriptor?.IsGenerator == true &&
               circleShellCells > 0 &&
               circleFilledCells > circleShellCells,
            "Smart Builder generated circles support shell and filled cell footprints.");

        SmartBuildPiece resizedCircleGenerator = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(20, 0, 0),
            circleDescriptor,
            SmartBuildAxis.Z,
            1,
            width: 5,
            drawPlane: SmartBuildDrawPlane.Camera);
        resizedCircleGenerator.ResizeFromHandle(DecorationEditAxis.X, sign: 1, delta: 4);
        SmartBuildPiece resizedSphereGenerator = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(40, 0, 0),
            sphereDescriptor,
            SmartBuildAxis.Z,
            1,
            width: 5,
            drawPlane: SmartBuildDrawPlane.Camera);
        resizedSphereGenerator.ResizeFromHandle(DecorationEditAxis.X, sign: 1, delta: 4);
        Assert(resizedCircleGenerator.FormatDimensions().Contains("9 x 9 x 1") &&
               resizedCircleGenerator.Origin.Equals(new Vector3i(20, 0, -2)) &&
               resizedSphereGenerator.FormatDimensions().Contains("9 x 9 x 9") &&
               resizedSphereGenerator.Origin.Equals(new Vector3i(40, 2, -2)),
            "Smart Builder round-locked generators scale once from the dragged handle and center forced radii on the other axes.");

        bool polygonPresetsWork = true;
        foreach (int sides in new[] { 1, 2, 3, 8, 10 })
        {
            var polygonScene = new SmartBuildPieceScene(null);
            SmartBuildPiece polygon = SmartBuildPiece.CreateGeneratedShape(
                null,
                new Vector3i(30 + sides * 10, 0, 0),
                polygonDescriptor,
                SmartBuildAxis.Z,
                1,
                width: 5,
                drawPlane: SmartBuildDrawPlane.Camera);
            polygon.SetGeneratorSides(sides);
            polygonScene.Add(polygon);
            SmartBuildPlan polygonPlan = polygonScene.BuildPlan(
                smartSource,
                _ => false,
                new SmartBuildPlannerOptions
                {
                    AllowNullConstructForVerification = true
                },
                out _);
            polygonPresetsWork &= polygonPlan.CanCommit &&
                                  polygonPlan.CoveredCellCount > 0 &&
                                  polygon.GeneratorSides == sides;
        }

        Assert(polygonPresetsWork,
            "Smart Builder generated polygons support 1, 2, 3, 8, and 10 sided footprints through the normal scene planner.");

        SmartBuildPiece resizedTriangleGenerator = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(80, 0, 0),
            polygonDescriptor,
            SmartBuildAxis.Z,
            1,
            width: 7,
            drawPlane: SmartBuildDrawPlane.Camera);
        resizedTriangleGenerator.SetGeneratorSides(3);
        resizedTriangleGenerator.ResizeFromHandle(DecorationEditAxis.Z, sign: 1, delta: 2);
        Assert(resizedTriangleGenerator.FormatDimensions().Contains("9 x 7 x 1") &&
               resizedTriangleGenerator.Origin.Equals(new Vector3i(80, 0, 0)),
            "Smart Builder polygon generators resize from their stored generator dimensions rather than their rasterized cell hull.");

        SmartBuildPiece sphereGenerator = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(120, 0, 0),
            sphereDescriptor,
            SmartBuildAxis.Z,
            1,
            width: 5,
            drawPlane: SmartBuildDrawPlane.Camera);
        Vector3i sphereCenterCell = new Vector3i(122, -2, 2);
        Vector3i[] sphereShellCells = sphereGenerator.EnumeratePreviewCells(smartSource).ToArray();
        sphereGenerator.SetGeneratorFillMode(SmartBuildGeneratorFillMode.Filled);
        Vector3i[] sphereFilledCells = sphereGenerator.EnumeratePreviewCells(smartSource).ToArray();
        Assert(sphereDescriptor?.IsGenerator == true &&
               sphereShellCells.Length > 0 &&
               sphereFilledCells.Length > sphereShellCells.Length &&
               !sphereShellCells.Contains(sphereCenterCell) &&
               sphereFilledCells.Contains(sphereCenterCell),
            "Smart Builder generated spheres support hollow shells and filled solid interiors.");

        var smoothScene = new SmartBuildPieceScene(null);
        SmartBuildPiece smoothCircle = SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(150, 0, 0),
            circleDescriptor,
            SmartBuildAxis.Z,
            1,
            width: 5,
            drawPlane: SmartBuildDrawPlane.Camera);
        smoothCircle.SetGeneratorSmoothingMode(SmartBuildGeneratorSmoothingMode.PerimeterDownSlope);
        smoothScene.Add(smoothCircle);
        SmartBuildPlan smoothPlan = smoothScene.BuildPlan(
            smartSource,
            _ => false,
            new SmartBuildPlannerOptions
            {
                AllowNullConstructForVerification = true
            },
            out _);
        Assert(smoothPlan.CanCommit &&
               smoothPlan.Placements.Any(placement => placement.Candidate.ShapeKind == SmartBuildShapeKind.DownSlope) &&
               smoothPlan.Warnings.Count == 0,
            "Smart Builder generated shape smoothing emits 1m down-slope placements when the material supports them.");

        var noSlopeSource = new SmartBuildSource(
            null,
            Guid.Empty,
            "test material without slopes",
            new Vector3i(1, 1, 1),
            SmartBlockFamily.ForTests(1, 2, 3, 4));
        SmartBuildPlan smoothFallbackPlan = smoothScene.BuildPlan(
            noSlopeSource,
            _ => false,
            new SmartBuildPlannerOptions
            {
                AllowNullConstructForVerification = true
            },
            out _);
        Assert(smoothFallbackPlan.CanCommit &&
               smoothFallbackPlan.CoveredCellCount > 0 &&
               smoothFallbackPlan.Warnings.Any(warning => warning.Contains("smoothing skipped")),
            "Smart Builder generated shape smoothing falls back to normal blocks with a warning when down slopes are unavailable.");

        var generatedMirrorScene = new SmartBuildPieceScene(null);
        generatedMirrorScene.Add(SmartBuildPiece.CreateGeneratedShape(
            null,
            new Vector3i(8, 0, 0),
            polygonDescriptor,
            SmartBuildAxis.Z,
            1,
            width: 5,
            drawPlane: SmartBuildDrawPlane.Camera));
        EsuSymmetry.Clear();
        EsuSymmetry.SetPlaneForTests(DecorationEditAxis.X, 0);
        SmartBuildPlan generatedMirrorPlan = generatedMirrorScene.BuildPlan(
            smartSource,
            _ => false,
            new SmartBuildPlannerOptions
            {
                AllowNullConstructForVerification = true
            },
            out _);
        EsuSymmetry.Clear();
        Vector3i[] generatedMirrorCells = generatedMirrorPlan.Placements
            .SelectMany(placement => placement.CoveredCells())
            .ToArray();
        Assert(generatedMirrorPlan.CanCommit &&
               generatedMirrorCells.Length > 0 &&
               generatedMirrorCells.Select(EsuSymmetry.CellKey).Distinct().Count() == generatedMirrorCells.Length,
            "Smart Builder generated shapes mirror through the scene planner without duplicate placement cells.");

        Vector3i occupiedGeneratedCell = circleGenerator.EnumeratePreviewCells(smartSource).First();
        var occupiedGeneratedScene = new SmartBuildPieceScene(null);
        occupiedGeneratedScene.Add(circleGenerator);
        SmartBuildPlan generatedBlockedPlan = occupiedGeneratedScene.BuildPlan(
            smartSource,
            cell => cell.Equals(occupiedGeneratedCell),
            new SmartBuildPlannerOptions
            {
                SkipOccupiedCells = false,
                AllowNullConstructForVerification = true
            },
            out _);
        SmartBuildPlan generatedSkippedPlan = occupiedGeneratedScene.BuildPlan(
            smartSource,
            cell => cell.Equals(occupiedGeneratedCell),
            new SmartBuildPlannerOptions
            {
                SkipOccupiedCells = true,
                AllowNullConstructForVerification = true
            },
            out _);
        Assert(!generatedBlockedPlan.CanCommit &&
               generatedBlockedPlan.FailureReason.Contains("intersects") &&
               generatedSkippedPlan.CanCommit &&
               generatedSkippedPlan.SkippedCells.Count == 1,
            "Smart Builder generated shapes respect Block-on-overlap and Skip-occupied occupancy modes.");

        SmartBuildPiece fixedTriangle = SmartBuildPiece.CreateFixedShape(
            null,
            new Vector3i(0, 0, 0),
            triangleLeftDescriptor,
            selectedLength: 2,
            forwardAxis: SmartBuildAxis.X,
            forwardSign: 1,
            width: 2,
            drawPlane: SmartBuildDrawPlane.Camera);
        fixedTriangle.ResizeFromHandle(DecorationEditAxis.X, sign: 1, delta: 3);
        IReadOnlyList<SmartBuildPlacement> fixedPlacements = fixedTriangle.BuildFixedPlacements(
            smartSource,
            out string fixedReason);
        Vector3i[] fixedCoveredCells = fixedPlacements
            .SelectMany(placement => placement.CoveredCells())
            .ToArray();
        Assert(string.IsNullOrWhiteSpace(fixedReason) &&
               fixedTriangle.FixedForwardTiles >= 2 &&
               fixedTriangle.FixedRightTiles == 2 &&
               fixedPlacements.Count == fixedTriangle.FixedForwardTiles * fixedTriangle.FixedRightTiles &&
               fixedPlacements.All(placement => placement.Candidate.Descriptor == triangleLeftDescriptor) &&
               fixedPlacements.All(placement => placement.CoveredCells().Count == 2) &&
               fixedCoveredCells.Select(EsuSymmetry.CellKey).Distinct().Count() == fixedCoveredCells.Length,
            "Smart Builder fixed structural shapes repeat whole items on voxel strides and use the candidate footprint for placement coverage.");

        var mirrorShapeScene = new SmartBuildPieceScene(null);
        mirrorShapeScene.Add(SmartBuildPiece.CreateFixedShape(
            null,
            new Vector3i(1, 0, 0),
            triangleLeftDescriptor,
            selectedLength: 2,
            forwardAxis: SmartBuildAxis.Z,
            forwardSign: 1,
            width: 1,
            drawPlane: SmartBuildDrawPlane.Camera));
        EsuSymmetry.Clear();
        EsuSymmetry.SetPlaneForTests(DecorationEditAxis.X, 0);
        SmartBuildPlan mirroredShapePlan = mirrorShapeScene.BuildPlan(
            smartSource,
            _ => false,
            new SmartBuildPlannerOptions
            {
                AllowNullConstructForVerification = true
            },
            out _);
        EsuSymmetry.Clear();
        Assert(mirroredShapePlan.CanCommit &&
               mirroredShapePlan.Placements.Any(placement => placement.Candidate.Descriptor == triangleLeftDescriptor) &&
               mirroredShapePlan.Placements.Any(placement => placement.Candidate.Descriptor == triangleRightDescriptor),
            "Smart Builder mirrors handed fixed structural shapes by swapping to the descriptor mirror replacement on odd-axis symmetry.");

        SmartBuildPiece ramp = SmartBuildPiece.CreateDownSlope(
            null,
            new Vector3i(0, 2, 0),
            slopeLength: 4,
            forwardAxis: SmartBuildAxis.Z,
            forwardSign: 1,
            width: 2,
            drawPlane: SmartBuildDrawPlane.Camera);
        ramp.ResizeFromHandle(DecorationEditAxis.Z, sign: 1, delta: 8);
        IReadOnlyList<SmartBuildPlacement> rampPlacements = ramp.BuildFixedPlacements(
            smartSource,
            out string rampReason);
        var rampStarts = new HashSet<string>(
            rampPlacements.Select(placement => EsuSymmetry.CellKey(placement.Position)));
        Assert(string.IsNullOrWhiteSpace(rampReason) &&
               ramp.SlopeSteps == 3 &&
               ramp.SlopeLength == 4 &&
               ramp.SlopeWidth == 2 &&
               rampPlacements.Count == 6 &&
               rampPlacements.All(placement => placement.CoveredCells().Count == 4) &&
               rampStarts.SetEquals(new[]
               {
                   EsuSymmetry.CellKey(new Vector3i(0, 2, 0)),
                   EsuSymmetry.CellKey(new Vector3i(1, 2, 0)),
                   EsuSymmetry.CellKey(new Vector3i(0, 1, 4)),
                   EsuSymmetry.CellKey(new Vector3i(1, 1, 4)),
                   EsuSymmetry.CellKey(new Vector3i(0, 0, 8)),
                   EsuSymmetry.CellKey(new Vector3i(1, 0, 8))
               }),
            "Smart Builder 4m down slopes place one segment per width lane and drop Y by one per snapped ramp step.");

        Vector3i[] supportCells = ramp.EnumerateSupportCells().ToArray();
        Assert(supportCells.Length == 24 &&
               supportCells.Contains(new Vector3i(0, 0, 0)) &&
               supportCells.Contains(new Vector3i(1, 1, 3)) &&
               supportCells.Contains(new Vector3i(0, 0, 4)) &&
               !supportCells.Contains(new Vector3i(0, 0, 8)),
            "Smart Builder fills normal support cells under higher down-slope segments without filling below the bottom step.");

        Vector3i[] slopePlacementCells = ramp.EnumeratePlacementCells().ToArray();
        IReadOnlyList<Vector3i>[] slopeVisualLines = ramp.EnumerateSlopeLines().ToArray();
        Assert(slopePlacementCells.Length == 24 &&
               slopeVisualLines.Length == 6 &&
               slopeVisualLines.All(slopeLine => slopeLine.Count == 4) &&
               !supportCells.All(cell => slopePlacementCells.Contains(cell)) &&
               ramp.EnumeratePreviewCells().Count() == 48,
            "Smart Builder exposes down-slope placement lines separately from support cells so preview geometry can draw true slopes.");

        SmartBuildPiece supportToggleRamp = SmartBuildPiece.CreateDownSlope(
            null,
            new Vector3i(0, 2, 0),
            slopeLength: 4,
            forwardAxis: SmartBuildAxis.Z,
            forwardSign: 1,
            width: 2,
            drawPlane: SmartBuildDrawPlane.Camera);
        supportToggleRamp.ResizeFromHandle(DecorationEditAxis.Z, sign: 1, delta: 8);
        int fixedPlacementCountWithSupport = supportToggleRamp.BuildFixedPlacements(
            smartSource,
            out _).Count;
        supportToggleRamp.SetSupportMode(SmartBuildSlopeSupportMode.Step);
        Vector3i[] stepSupportCells = supportToggleRamp.EnumerateSupportCells().ToArray();
        Vector3i[] expectedStepSupportCells = supportToggleRamp.EnumeratePlacementCells()
            .Select(cell => cell + new Vector3i(0, -1, 0))
            .GroupBy(EsuSymmetry.CellKey)
            .Select(group => group.First())
            .ToArray();
        var stepSupportKeys = new HashSet<string>(stepSupportCells.Select(EsuSymmetry.CellKey));
        var expectedStepSupportKeys = new HashSet<string>(expectedStepSupportCells.Select(EsuSymmetry.CellKey));
        Assert(supportToggleRamp.SupportMode == SmartBuildSlopeSupportMode.Step &&
               stepSupportCells.Length > 0 &&
               stepSupportKeys.SetEquals(expectedStepSupportKeys) &&
               stepSupportCells.Contains(new Vector3i(0, 1, 0)) &&
               stepSupportCells.Contains(new Vector3i(0, 0, 4)) &&
               stepSupportCells.Contains(new Vector3i(0, -1, 8)) &&
               !stepSupportCells.Contains(new Vector3i(0, 0, 0)) &&
               supportToggleRamp.EnumeratePreviewCells().Count() == supportToggleRamp.EnumeratePlacementCells().Count() + stepSupportCells.Length &&
               supportToggleRamp.BuildFixedPlacements(smartSource, out _).Count == fixedPlacementCountWithSupport,
            "Smart Builder down-slope Step support places one support block under each slope cell without changing fixed slope placements.");

        SmartBuildPiece rotatedRamp = SmartBuildPiece.CreateDownSlope(
            null,
            new Vector3i(0, 0, 0),
            slopeLength: 4,
            forwardAxis: SmartBuildAxis.Z,
            forwardSign: 1,
            width: 1,
            drawPlane: SmartBuildDrawPlane.Camera);
        rotatedRamp.RotateAroundAxis(DecorationEditAxis.Z, 1);
        SmartBuildPlacement rotatedPlacement = rotatedRamp.BuildFixedPlacements(
            smartSource,
            out _).Single();
        MethodInfo mirrorPlacementMethod = AccessTools.Method(
            typeof(SmartBuildPieceScene),
            "MirrorPlacement",
            new[]
            {
                typeof(SmartBuildPlacement),
                typeof(EsuSymmetry.SymmetryVariant)
            });
        EsuSymmetry.Clear();
        EsuSymmetry.SetPlaneForTests(DecorationEditAxis.X, 0);
        var xMirrorVariant = new EsuSymmetry.SymmetryVariant(new[] { DecorationEditAxis.X });
        SmartBuildPlacement mirroredRotatedPlacement = mirrorPlacementMethod == null
            ? null
            : (SmartBuildPlacement)mirrorPlacementMethod.Invoke(
                null,
                new object[] { rotatedPlacement, xMirrorVariant });
        EsuSymmetry.Clear();
        Vector3 sourceForward = rotatedPlacement.Rotation * Vector3.forward;
        Vector3 sourceUp = rotatedPlacement.Rotation * Vector3.up;
        Vector3 expectedMirroredForward = new Vector3(-sourceForward.x, sourceForward.y, sourceForward.z);
        Vector3 expectedMirroredUp = new Vector3(-sourceUp.x, sourceUp.y, sourceUp.z);
        Vector3 actualMirroredForward = mirroredRotatedPlacement == null
            ? Vector3.zero
            : mirroredRotatedPlacement.Rotation * Vector3.forward;
        Vector3 actualMirroredUp = mirroredRotatedPlacement == null
            ? Vector3.zero
            : mirroredRotatedPlacement.Rotation * Vector3.up;
        Assert(mirrorPlacementMethod != null &&
               mirroredRotatedPlacement != null &&
               VectorApproximately(actualMirroredForward.normalized, expectedMirroredForward.normalized, 0.0001f) &&
               VectorApproximately(actualMirroredUp.normalized, expectedMirroredUp.normalized, 0.0001f),
            "Smart Builder mirrored fixed placements preserve custom down-slope rotations instead of rebuilding rotation from axis/sign.");

        SmartBuildPiece snappedForward = SmartBuildPiece.CreateDownSlope(
            null,
            new Vector3i(0, 0, 0),
            slopeLength: 4,
            forwardAxis: SmartBuildAxis.Z,
            forwardSign: 1,
            width: 1,
            drawPlane: SmartBuildDrawPlane.Camera);
        snappedForward.ResizeFromHandle(DecorationEditAxis.Z, sign: 1, delta: 1);
        SmartBuildPiece snappedDown = SmartBuildPiece.CreateDownSlope(
            null,
            new Vector3i(0, 4, 0),
            slopeLength: 4,
            forwardAxis: SmartBuildAxis.Z,
            forwardSign: 1,
            width: 1,
            drawPlane: SmartBuildDrawPlane.Camera);
        snappedDown.ResizeFromHandle(DecorationEditAxis.Y, sign: -1, delta: -2);
        Assert(snappedForward.SlopeSteps == 2 &&
               snappedForward.Size.z == 8 &&
               snappedForward.Size.y == 2 &&
               snappedDown.SlopeSteps == 3 &&
               snappedDown.Size.z == 12 &&
               snappedDown.Size.y == 3,
            "Smart Builder snaps down-slope forward and downward scaling to whole pitch-locked ramp steps.");

        AllConstruct fakeConstruct = null;
        var occupiedScene = new SmartBuildPieceScene(fakeConstruct);
        SmartBuildPiece occupiedCuboid = SmartBuildPiece.CreateCuboid(
            fakeConstruct,
            new Vector3i(0, 0, 0),
            SmartBuildDrawPlane.Camera);
        occupiedCuboid.ResizeFromHandle(DecorationEditAxis.X, sign: 1, delta: 1);
        occupiedScene.Add(occupiedCuboid);
        SmartBuildPlan blockModePlan = occupiedScene.BuildPlan(
            smartSource,
            cell => cell.Equals(new Vector3i(0, 0, 0)),
            new SmartBuildPlannerOptions
            {
                SkipOccupiedCells = false,
                AllowNullConstructForVerification = true
            },
            out _);
        SmartBuildPlan skipModePlan = occupiedScene.BuildPlan(
            smartSource,
            cell => cell.Equals(new Vector3i(0, 0, 0)),
            new SmartBuildPlannerOptions
            {
                SkipOccupiedCells = true,
                AllowNullConstructForVerification = true
            },
            out _);
        Assert(!blockModePlan.CanCommit &&
               blockModePlan.FailureReason.Contains("intersects") &&
               skipModePlan.CanCommit &&
               skipModePlan.SkippedCells.Count == 1 &&
               skipModePlan.CoveredCellCount == 1,
            "Smart Builder scene planning blocks or skips occupied footprints according to the selected occupancy mode.");

        var overlapScene = new SmartBuildPieceScene(fakeConstruct);
        overlapScene.Add(SmartBuildPiece.CreateCuboid(
            fakeConstruct,
            new Vector3i(0, 0, 0),
            SmartBuildDrawPlane.Camera));
        overlapScene.Add(SmartBuildPiece.CreateCuboid(
            fakeConstruct,
            new Vector3i(0, 0, 0),
            SmartBuildDrawPlane.Camera));
        SmartBuildPlan overlapPlan = overlapScene.BuildPlan(
            smartSource,
            _ => false,
            new SmartBuildPlannerOptions
            {
                SkipOccupiedCells = false,
                AllowNullConstructForVerification = true
            },
            out _);
        Assert(!overlapPlan.CanCommit &&
               overlapPlan.FailureReason.Contains("overlap"),
            "Smart Builder multi-piece scenes reject preview-piece footprint collisions before commit.");

        var editScene = new SmartBuildPieceScene(fakeConstruct);
        SmartBuildPiece editBlock = SmartBuildPiece.CreateCuboid(
            fakeConstruct,
            new Vector3i(4, 0, 0),
            SmartBuildDrawPlane.Camera);
        editScene.Add(editBlock);
        SmartBuildPiece duplicate = editScene.DuplicateSelected(new Vector3i(1, 0, 0));
        bool deletedDuplicate = editScene.DeleteSelected();
        Assert(duplicate != null &&
               duplicate.Id != editBlock.Id &&
               editScene.SelectedPiece == editBlock &&
               deletedDuplicate &&
               editScene.Count == 1,
            "Smart Builder multi-piece scenes can duplicate and delete selected pieces while preserving selection.");

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
        string sessionSourceNormalized = sessionSource.Replace("\r\n", "\n");
        string draftSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildDraft.cs"));
        string geometrySource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildGeometry.cs"));
        string smartInputScopeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildInputScope.cs"));
        string smartInputScopeSourceNormalized = smartInputScopeSource.Replace("\r\n", "\n");
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
        string descriptorSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildShapeDescriptors.cs"));
        string pieceSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildPiece.cs"));
        string sceneSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildPieceScene.cs"));
        string committerSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBuildCommitter.cs"));
        string itemPreviewRendererSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "SmartBuildMode",
            "SmartBlockItemPreviewRenderer.cs"));
        string notificationSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "EsuHudNotifications.cs"));
        string inputScopeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationEditorInputScope.cs"));
        string tooltipSuppressorSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "DecorationEditMode",
            "DecorationTooltipSuppressor.cs"));
        string vanillaInputBridgeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "EsuVanillaInputBridge.cs"));
        string automationRegistrationSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "AutomationEditMode",
            "AutomationEditModeRegistration.cs"));
        string automationBehaviourSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "AutomationEditMode",
            "AutomationEditModeBehaviour.cs"));
        string automationSessionSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "AutomationEditMode",
            "AutomationEditSession.cs"));
        string automationInputScopeSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "AutomationEditMode",
            "AutomationInputScope.cs"));
        string automationControllerCatalogSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "AutomationEditMode",
            "AutomationControllerCatalog.cs"));
        string automationTargetCatalogSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "AutomationEditMode",
            "AutomationTargetCatalog.cs"));
        string automationAcbInspectorSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "AutomationEditMode",
            "AutomationAcbInspector.cs"));
        string automationAcbControllerInspectorSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "AutomationEditMode",
            "AutomationAcbControllerInspector.cs"));
        string automationBreadboardInspectorSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "AutomationEditMode",
            "AutomationBreadboardInspector.cs"));
        string automationRuntimeDiagnosticsSource = File.ReadAllText(Path.Combine(
            root,
            "EndlessShapesUnlimited",
            "Source",
            "AutomationEditMode",
            "AutomationRuntimeDiagnostics.cs"));
        string readmeDocumentationSource = ReadDocumentationText(root);
        string inGameTestPlanSource = ReadDocumentationText(root, "docs", "IN_GAME_TEST_PLAN.md");
        string changeTestChecklistSource = ReadDocumentationText(root, "docs", "ESU_CHANGE_TEST_CHECKLIST.md");
        string smartBuilderHudDocSource = ReadDocumentationText(
            root,
            "SMART_BUILDER_HUD.md");
        string readFreezeDownSource = ExtractMethodSource(vanillaInputBridgeSource, "ReadFreezeDown").Replace("\r\n", "\n");
        Assert(profileSource.Contains("ToggleSmartBuildMode") &&
               profileSource.Contains("Q(Key.Control, Key.Shift, Key.B)") &&
               profileSource.Contains("ToggleAutomationEditMode") &&
               profileSource.Contains("Q(Key.Control, Key.Shift, Key.A)") &&
               profileSource.Contains("SwitchEsuBuildMode") &&
               profileSource.Contains("Q(Key.Tab)") &&
               pluginSource.Contains("SmartBuildModeRegistration.Register") &&
               pluginSource.Contains("AutomationEditModeRegistration.Register") &&
               registrationSource.Contains("DecorationEditModeRegistration.Active") &&
               registrationSource.Contains("AutomationEditModeRegistration.Active") &&
               registrationSource.Contains("CanOpenFromModeSwitch") &&
               registrationSource.Contains("ignoreDecorationEditMode") &&
               registrationSource.Contains("ignoreAutomationEditMode") &&
               registrationSource.Contains("modeSwitch: true") &&
               registrationSource.Contains("CanSwitchEsuModes") &&
               registrationSource.Contains("TrySwitchToAutomationEdit") &&
               behaviourSource.Contains("ReadSwitchModeKeyDown") &&
               behaviourSource.Contains("CanOpenFromModeSwitch") &&
               behaviourSource.Contains("EsuSymmetry.Clear") &&
               behaviourSource.Contains("AutomationEditModeRegistration.CanOpenFromModeSwitch") &&
               behaviourSource.Contains("AutomationEditModeRegistration.OpenFromModeSwitch") &&
               behaviourSource.Contains("ConsumeSmartBuildToggleDown") &&
               behaviourSource.Contains("ConsumeSwitchModeDown") &&
               decorationBehaviourSource.Contains("ConsumeDecorationEditToggleDown") &&
               decorationBehaviourSource.Contains("ConsumeSwitchModeDown") &&
               automationRegistrationSource.Contains("DecorationEditModeRegistration.Active") &&
               automationRegistrationSource.Contains("SmartBuildModeRegistration.Active") &&
               automationRegistrationSource.Contains("CanOpenFromModeSwitch") &&
               automationBehaviourSource.Contains("TrySwitchToDecorationEdit") &&
               automationBehaviourSource.Contains("DecorationEditModeRegistration.CanOpenFromModeSwitch") &&
               automationBehaviourSource.Contains("ConsumeAutomationEditToggleDown") &&
               automationBehaviourSource.Contains("ConsumeSwitchModeDown") &&
               automationSessionSource.Contains("SwitchToDecorationEditRequested") &&
               automationInputScopeSource.Contains("AutomationInputScope") &&
               automationControllerCatalogSource.Contains("7fcfdaf0-2d2a-43be-842a-423e736ccdd0") &&
               automationControllerCatalogSource.Contains("a3d914e9-697d-425f-abda-a6b21b4de952") &&
               automationTargetCatalogSource.Contains("AutomationTargetCategory.Spinblocks") &&
               automationTargetCatalogSource.Contains("AutomationTargetCategory.TurretsWeapons") &&
               automationTargetCatalogSource.Contains("AutomationTargetCategory.Propulsion") &&
               automationTargetCatalogSource.Contains("AutomationTargetCategory.AcbActions") &&
               automationTargetCatalogSource.Contains("AutomationTargetCategory.BreadboardReadable") &&
               automationTargetCatalogSource.Contains("AutomationTargetCategory.BreadboardWritable") &&
               automationTargetCatalogSource.Contains("AutomationTargetCategory.Movement") &&
               automationTargetCatalogSource.Contains("AutomationTargetCategory.Subobjects") &&
               automationTargetCatalogSource.Contains("AutomationTargetCategory.Utility") &&
               automationTargetCatalogSource.Contains("AutomationTargetCategory.Media") &&
               automationTargetCatalogSource.Contains("IsAcbActionTarget") &&
               automationTargetCatalogSource.Contains("IsBreadboardReadableTarget") &&
               automationTargetCatalogSource.Contains("IsBreadboardWritableTarget") &&
               automationTargetCatalogSource.Contains("RoleSearchTextFor") &&
               automationTargetCatalogSource.Contains("RoleLabel") &&
               automationTargetCatalogSource.Contains("RoleSummary") &&
               automationSessionSource.Contains("AutomationTargetCatalog.RoleSummary(target)") &&
               automationSessionSource.Contains("AutomationTargetCatalog.RoleLabel(link.Target)") &&
               automationTargetCatalogSource.Contains("internal static bool MatchesSearch") &&
               automationTargetCatalogSource.Contains("SearchTextFor") &&
               automationTargetCatalogSource.Contains("ItemGuid.ToString(\"D\")") &&
               automationTargetCatalogSource.Contains("StringSplitOptions.RemoveEmptyEntries") &&
               automationAcbInspectorSource.Contains("ControlBlockData") &&
               automationAcbInspectorSource.Contains("GetProperty(\"Us\"") &&
               automationAcbInspectorSource.Contains("ForceChanged") &&
               automationAcbInspectorSource.Contains("TriggerATestNow") &&
               automationAcbInspectorSource.Contains("ACB_ActionValue") &&
               automationAcbInspectorSource.Contains("ACB_ConditionValue") &&
               automationAcbInspectorSource.Contains("ACB_ActionType, Ftd") &&
               automationAcbInspectorSource.Contains("ACB_ConditionType, Ftd") &&
               automationAcbInspectorSource.Contains("CycleActionType") &&
               automationAcbInspectorSource.Contains("ConditionInverted") &&
               automationAcbInspectorSource.Contains("ConditionMinVariable") &&
               automationAcbControllerInspectorSource.Contains("AutomationAcbControllerInspector") &&
               automationAcbControllerInspectorSource.Contains("AcbControllerButtonData") &&
               automationAcbControllerInspectorSource.Contains("ButtonName") &&
               automationAcbControllerInspectorSource.Contains("Keyword") &&
               automationAcbControllerInspectorSource.Contains("ShapeId") &&
               automationAcbControllerInspectorSource.Contains("ButtonColor") &&
               automationAcbControllerInspectorSource.Contains("IsUsedForBreadboard") &&
               automationAcbControllerInspectorSource.Contains("TrySetBreadboardOutput") &&
               automationAcbControllerInspectorSource.Contains("TrySetButtonColor") &&
               automationBreadboardInspectorSource.Contains("BrilliantSkies.Common.Circuits.IBoard") &&
               automationBreadboardInspectorSource.Contains("AvailableComponentTypes") &&
               automationBreadboardInspectorSource.Contains("CanAddComponentMatching") &&
               automationBreadboardInspectorSource.Contains("TryAddFirstAvailableComponent") &&
               automationBreadboardInspectorSource.Contains("TryAddFirstAvailableComponents") &&
               automationBreadboardInspectorSource.Contains("FindAvailableComponentMatching") &&
               automationBreadboardInspectorSource.Contains("ControllerName") &&
               automationBreadboardInspectorSource.Contains("AddComponentCommand") &&
               automationBreadboardInspectorSource.Contains("MakeSystem") &&
               automationBreadboardInspectorSource.Contains("NewPackage") &&
               automationBreadboardInspectorSource.Contains("GetProperty(\"Us\"") &&
               automationBreadboardInspectorSource.Contains("TryCreateTargetProxy") &&
               automationBreadboardInspectorSource.Contains("ConfigureGenericProxy") &&
               automationBreadboardInspectorSource.Contains("TryAutoSelectAcbControllerGetter") &&
               automationBreadboardInspectorSource.Contains("TryAutoSelectTargetProxyProperty") &&
               automationBreadboardInspectorSource.Contains("FindGetterOption") &&
               automationBreadboardInspectorSource.Contains("FindProxyOption") &&
               automationBreadboardInspectorSource.Contains("TargetProxySearchTerms") &&
               automationBreadboardInspectorSource.Contains("internal static IReadOnlyList<string> TargetProxySearchTerms") &&
               automationBreadboardInspectorSource.Contains("AddTargetProxySearchTerms") &&
               automationBreadboardInspectorSource.Contains("Auto-selected") &&
               automationBreadboardInspectorSource.Contains("MatchesAnyTerm") &&
               automationBreadboardInspectorSource.Contains("button getters try to select a matching keyword automatically") &&
               automationBreadboardInspectorSource.Contains("TryCreateEvaluatorExpression") &&
               automationBreadboardInspectorSource.Contains("TryCreateIfElseSwitch") &&
               automationBreadboardInspectorSource.Contains("WriteValue(component, \"Expression\"") &&
               automationBreadboardInspectorSource.Contains("WriteValue(switchComponent, \"Threshold\"") &&
               automationBreadboardInspectorSource.Contains("WriteValue(switchComponent, \"FailValue\"") &&
               automationBreadboardInspectorSource.Contains("string failExpression") &&
               automationBreadboardInspectorSource.Contains("useFailExpression") &&
               automationBreadboardInspectorSource.Contains("TryCreateEvaluatorNode(fail") &&
               automationBreadboardInspectorSource.Contains("TryConnectNativePorts(failEvaluator, 0, switchComponent, 2") &&
               automationBreadboardInspectorSource.Contains("AutomationBreadboardCompileResult") &&
               automationBreadboardInspectorSource.Contains("TryCreateNativeComponentTracked") &&
               automationBreadboardInspectorSource.Contains("DeleteCreatedComponents") &&
               automationBreadboardInspectorSource.Contains("FromComponents") &&
               automationBreadboardInspectorSource.Contains("TryConnectNativePorts") &&
               automationBreadboardInspectorSource.Contains("LogicGate") &&
               automationBreadboardInspectorSource.Contains("SelectedGate") &&
               automationBreadboardInspectorSource.Contains("TrueLogic") &&
               automationBreadboardInspectorSource.Contains("TryCreateEvaluatorNode") &&
               automationBreadboardInspectorSource.Contains("WriteDirectMember") &&
               automationBreadboardInspectorSource.Contains("BlockTypeName") &&
               automationBreadboardInspectorSource.Contains("BlockFilter") &&
               automationBreadboardInspectorSource.Contains("SetBlockType") &&
               automationBreadboardInspectorSource.Contains("AutomationTargetCategory.Spinblocks") &&
               automationBreadboardInspectorSource.Contains("AutomationTargetCategory.Propulsion") &&
               automationBreadboardInspectorSource.Contains("AutomationTargetCategory.DoorsDocking") &&
               automationBreadboardInspectorSource.Contains("AutomationTargetCategory.ResourcePower") &&
               automationBreadboardInspectorSource.Contains("ProxyPropertyOptions") &&
               automationBreadboardInspectorSource.Contains("TrySelectProxyProperty") &&
               automationBreadboardInspectorSource.Contains("ExtractCorrectInputs") &&
               automationBreadboardInspectorSource.Contains("PropertyPicker") &&
               automationBreadboardInspectorSource.Contains("EnumerateItems") &&
               automationBreadboardInspectorSource.Contains("ReadableAttributeId") &&
               automationBreadboardInspectorSource.Contains("_computer") &&
               automationBreadboardInspectorSource.Contains("_moduleAttributePair") &&
               automationBreadboardInspectorSource.Contains("TryConnectPorts") &&
               automationBreadboardInspectorSource.Contains("TryClearInput") &&
               automationBreadboardInspectorSource.Contains("TryMoveComponent") &&
               automationBreadboardInspectorSource.Contains("TryDeleteComponent") &&
               automationBreadboardInspectorSource.Contains("CreateConnectionCommand") &&
               automationBreadboardInspectorSource.Contains("DeleteConnectionCommand") &&
               automationBreadboardInspectorSource.Contains("MoveComponentCommand") &&
               automationBreadboardInspectorSource.Contains("DeleteComponentCommand") &&
               automationBreadboardInspectorSource.Contains("AutomationBreadboardPortSummary") &&
               automationBreadboardInspectorSource.Contains("ConnectedFromComponentId") &&
               automationBreadboardInspectorSource.Contains("ConnectedFromOutputIndex") &&
               automationBreadboardInspectorSource.Contains("AutomationBreadboardComponentSettings") &&
               automationBreadboardInspectorSource.Contains("SettingsFor") &&
               automationBreadboardInspectorSource.Contains("TryUpdateComponentSetting") &&
               automationBreadboardInspectorSource.Contains("TryCycleComponentSetting") &&
               automationBreadboardInspectorSource.Contains("InputValueLongAsString") &&
               automationBreadboardInspectorSource.Contains("LogicGateLabels") &&
               automationSessionSource.Contains("DrawAcbInspectorSection") &&
               automationSessionSource.Contains("DrawAcbControllerInspectorSection") &&
               automationSessionSource.Contains("DrawAcbControllerButtonEditor") &&
               automationSessionSource.Contains("DrawAcbControllerColorEditor") &&
               automationSessionSource.Contains("ACB Controller Inspector") &&
               automationSessionSource.Contains("Breadboard output uses FtD's ACB Controller button keyword path") &&
               automationSessionSource.Contains("TryToggleControllerTargetLink") &&
               automationSessionSource.Contains("CanLinkControllerTarget") &&
               automationSessionSource.Contains("RebindAutomationLinks") &&
               automationSessionSource.Contains("RehydrateSelectedControllerLinksFromNativeProxies") &&
               automationSessionSource.Contains("\"Rehydrated \" +") &&
               automationSessionSource.Contains("from native Generic Getter/Setter proxies") &&
               automationSessionSource.Contains("UniqueTargetForPersistedProxy") &&
               automationSessionSource.Contains("ProxyComponentMatchesTarget") &&
               automationSessionSource.Contains("ExpectedProxyBlockTypeName") &&
               automationSessionSource.Contains("ExpectedProxyBlockFilter") &&
               automationSessionSource.Contains(".GroupBy(target => target.StableKey)") &&
               automationSessionSource.Contains("link.RebindTarget") &&
               automationSessionSource.Contains("_selectedLinkTargetKey") &&
               automationSessionSource.Contains("DrawLinkedTargetListRow") &&
               automationSessionSource.Contains("DrawSelectedLinkedTargetInspector") &&
               automationSessionSource.Contains("Selected linked target") &&
               automationSessionSource.Contains("SelectedLink()") &&
               automationSessionSource.Contains("LinkedTargetWarning") &&
               automationSessionSource.Contains("Open graph") &&
               automationSessionSource.Contains("Generic Getter/Setter properties are discovered") &&
               automationSessionSource.Contains("internal bool IsStale => Target == null") &&
               automationSessionSource.Contains("Target { get; private set; }") &&
               automationSessionSource.Contains("\" (missing)\"") &&
               automationSessionSource.Contains("not in the live target catalog after refresh") &&
               automationSessionSource.Contains("IsAcbControllerBridgeTarget") &&
               automationSessionSource.Contains("IsAcbProxyTarget") &&
               automationSessionSource.Contains("ACB rule proxy target") &&
               automationSessionSource.Contains("Rule getter") &&
               automationSessionSource.Contains("Rule setter") &&
               automationSessionSource.Contains("ACB condition/action rule proxy") &&
               automationSessionSource.Contains("Button getter") &&
               automationSessionSource.Contains("Matches a Breadboard-output keyword when FtD exposes one") &&
               automationSessionSource.Contains("ACB Controller keyword/button output proxy") &&
               automationSessionSource.Contains("DrawLinkedGenericTargetNode") &&
               automationSessionSource.Contains("DrawLinkedTargetProxyShortcuts") &&
               automationSessionSource.Contains("TryCreateSelectedBreadboardInspector") &&
               automationSessionSource.Contains("Linked Generic target node") &&
               automationSessionSource.Contains("Auto-pick terms") &&
               automationSessionSource.Contains("TargetProxySearchTerms(target, getter: true)") &&
               automationSessionSource.Contains("Linked \" + controller.Label + \" to ACB Controller button keyword output") &&
               automationSessionSource.Contains("DrawBreadboardInspectorSection") &&
               automationSessionSource.Contains("DrawBreadboardProxyActions") &&
               automationSessionSource.Contains("Linked target proxies") &&
               automationSessionSource.Contains("TryCreateTargetProxy") &&
               automationSessionSource.Contains("DrawLinkedAutomationNode") &&
               automationSessionSource.Contains("DrawLinkedAcbTargetNode") &&
               automationSessionSource.Contains("DrawLinkedAcbControllerTargetNode") &&
               automationSessionSource.Contains("Linked ACB target node") &&
               automationSessionSource.Contains("Linked ACB Controller node") &&
               automationSessionSource.Contains("DrawAcbInspectorBody") &&
               automationSessionSource.Contains("DrawAcbControllerInspectorBody") &&
               automationSessionSource.Contains("DrawAcbInspectorBody(inspector, \"Linked ACB\", allowTrigger: false)") &&
               automationSessionSource.Contains("DrawAcbControllerInspectorBody(inspector, maxButtons: 3, statusPrefix: \"Linked ACB Controller\")") &&
               automationSessionSource.Contains("PrefixStatus") &&
               automationSessionSource.Contains("DrawBreadboardProxyPropertyPicker") &&
               automationSessionSource.Contains("Proxy property filter") &&
               automationSessionSource.Contains("TrySelectProxyProperty") &&
               automationSessionSource.Contains("Code node compiler") &&
               automationSessionSource.Contains("CompileAutomationCodeExpression") &&
               automationSessionSource.Contains("AutomationCodeRecipe") &&
               automationSessionSource.Contains("s_codeRecipes") &&
               automationSessionSource.Contains("Validation proof") &&
               automationSessionSource.Contains("proof_signal * 0") &&
               automationSessionSource.Contains("DrawAutomationRecipePicker") &&
               automationSessionSource.Contains("DrawAutomationLinkedIdentifierHints") &&
               automationSessionSource.Contains("AutomationCodeLinkedIdentifiers") &&
               automationSessionSource.Contains("AutomationCodeIdentifierForTarget") &&
               automationSessionSource.Contains("InsertAutomationCodeIdentifier") &&
               automationSessionSource.Contains("Linked identifiers") &&
               automationSessionSource.Contains("button_signal") &&
               automationSessionSource.Contains("control_surface") &&
               automationSessionSource.Contains("SanitizeIdentifier") &&
               automationSessionSource.Contains("AutomationCompileRevertSet") &&
               automationSessionSource.Contains("RecordAutomationCompileResult") &&
               automationSessionSource.Contains("TryBindCompiledOutputToLinkedSetter") &&
               automationSessionSource.Contains("DrawAutomationCodeOutputTargetPicker") &&
               automationSessionSource.Contains("Code output target") &&
               automationSessionSource.Contains("_automationCodeOutputTargetKey") &&
               automationSessionSource.Contains("AutomationCodeOutputLink") &&
               automationSessionSource.Contains("TargetSettersFor") &&
               automationSessionSource.Contains("SetterMatchesTarget") &&
               automationSessionSource.Contains("_lastCompileBoundOutput") &&
               automationSessionSource.Contains("out bool boundOutput") &&
               automationSessionSource.Contains("boundOutput = true") &&
               automationSessionSource.Contains("ExpectedProxyBlockTypeName") &&
               automationSessionSource.Contains("ExpectedProxyBlockFilter") &&
               automationSessionSource.Contains("out AutomationBreadboardCompileResult proxyResult") &&
               automationSessionSource.Contains("auto-created target setter proxy") &&
               automationSessionSource.Contains("Bound compiled output to") &&
               automationSessionSource.Contains("target-specific Generic Setter") &&
               automationSessionSource.Contains("component.IsGenericSetter") &&
               automationSessionSource.Contains("inspector.TryConnectPorts(output, 0, setter") &&
               automationBreadboardInspectorSource.Contains("out AutomationBreadboardCompileResult result") &&
               automationBreadboardInspectorSource.Contains("CompileResultFromComponents(createdComponents, \"target proxy\")") &&
               automationSessionSource.Contains("CanRevertLastAutomationCompile") &&
               automationSessionSource.Contains("RevertLastAutomationCompile") &&
               automationSessionSource.Contains("revert compile") &&
               automationSessionSource.Contains("SuggestedAutomationCodeRecipeIndex") &&
               automationSessionSource.Contains("Spinblock park") &&
               automationSessionSource.Contains("Door sequence") &&
               automationSessionSource.Contains("Propulsion trim") &&
               automationSessionSource.Contains("ACB bridge") &&
               automationSessionSource.Contains("Missile fuse") &&
               automationSessionSource.Contains("reset recipe") &&
               automationSessionSource.Contains("TryExtractEvaluatorExpression") &&
               automationSessionSource.Contains("TryExtractIfElseSwitch") &&
               automationSessionSource.Contains("TrySplitLogicCondition") &&
               automationSessionSource.Contains("AutomationIfElseCompilePlan") &&
               automationSessionSource.Contains("SecondaryConditionExpression") &&
               automationSessionSource.Contains("FailExpression") &&
               automationSessionSource.Contains("HasFailExpression") &&
               automationSessionSource.Contains("number-or-expression") &&
               automationSessionSource.Contains("use the supported if/else form for control flow") &&
               !automationSessionSource.Contains("if/else lowering is not implemented yet") &&
               automationSessionSource.Contains("LogicGate") &&
               automationSessionSource.Contains("TryCreateIfElseSwitch") &&
               automationSessionSource.Contains("TryCreateEvaluatorExpression") &&
               automationSessionSource.Contains("IsSafeEvaluatorExpressionText") &&
               automationSessionSource.Contains("DrawBreadboardGraphCanvas") &&
               automationSessionSource.Contains("DrawEditorBottomStrip") &&
               automationSessionSource.Contains("EditorGridStatus") &&
               automationSessionSource.Contains("Page: ") &&
               automationSessionSource.Contains("Links: ") &&
               automationSessionSource.Contains("Grid: n/a") &&
               automationSessionSource.Contains("Native edits apply immediately") &&
               automationSessionSource.Contains("Generated nodes: revert available") &&
               automationSessionSource.Contains("AutomationBreadboardCanvasNode") &&
               automationSessionSource.Contains("DrawBreadboardCanvasWires") &&
               automationSessionSource.Contains("HandleBreadboardCanvasInput") &&
               automationSessionSource.Contains("CanvasPortRect") &&
               automationSessionSource.Contains("_canvasDragComponentId") &&
               automationSessionSource.Contains("_selectedCanvasComponentId") &&
               automationSessionSource.Contains("DrawSelectedBreadboardCanvasInspector") &&
               automationSessionSource.Contains("DeleteBreadboardComponent") &&
               automationSessionSource.Contains("ClearBreadboardComponentTransientState") &&
               automationSessionSource.Contains("DrawBreadboardWireControls") &&
               automationSessionSource.Contains("_wireSourceComponentId") &&
               automationSessionSource.Contains("TryConnectPorts") &&
               automationSessionSource.Contains("TryClearInput") &&
               automationSessionSource.Contains("DrawBreadboardMoveControls") &&
               automationSessionSource.Contains("TryMoveComponent") &&
               automationSessionSource.Contains("DrawBreadboardComponentSettings") &&
               automationSessionSource.Contains("DrawNativeEnumCycler") &&
               automationSessionSource.Contains("DrawNativeFloatSetting") &&
               automationSessionSource.Contains("DrawNativeLongSetting") &&
               automationSessionSource.Contains("Native settings") &&
               automationSessionSource.Contains("TryPickProjectedController") &&
               automationSessionSource.Contains("WorldToScreenPoint") &&
               automationSessionSource.Contains("screen-space controller pick") &&
               automationSessionSource.Contains("_targetSearchText") &&
               automationSessionSource.Contains("DrawControllerIndex") &&
               automationSessionSource.Contains("DrawControllerIndexRow") &&
               automationSessionSource.Contains("Controllers on craft") &&
               automationSessionSource.Contains("ConstructGroupLabel") &&
               automationSessionSource.Contains("ConstructGroupSortKey") &&
               automationSessionSource.Contains("Main construct") &&
               automationSessionSource.Contains("Subconstruct ") &&
               automationSessionSource.Contains("DrawTargetSearchControls") &&
               automationSessionSource.Contains("FilteredWorldTargets") &&
               automationSessionSource.Contains("Filter targets by label, class, category, cell, controller GUID") &&
               automationSessionSource.Contains("No targets match the current search and filter") &&
               automationSessionSource.Contains("DrawAcbRuleSection") &&
               automationSessionSource.Contains("ACB Inspector") &&
               automationSessionSource.Contains("ACB rule") &&
               automationSessionSource.Contains("Action value") &&
               automationSessionSource.Contains("Min value") &&
               automationSessionSource.Contains("Breadboard Inspector") &&
               automationSessionSource.Contains("Quick add native component") &&
               automationSessionSource.Contains("DrawMissileBreadboardQuickAdds") &&
               automationSessionSource.Contains("DrawBreadboardSearchAddButton") &&
               automationSessionSource.Contains("DrawBreadboardSharedVariableBridge") &&
               automationSessionSource.Contains("Shared variable bridge") &&
               automationSessionSource.Contains("TryAddFirstAvailableComponents") &&
               automationSessionSource.Contains("Use matching native variable names/settings") &&
               automationSessionSource.Contains("\"PID\", \"pid\"") &&
               automationSessionSource.Contains("\"Thresh\", \"threshold\"") &&
               automationSessionSource.Contains("\"Clamp\", \"clamp\"") &&
               automationSessionSource.Contains("\"Delay\", \"delay\"") &&
               automationSessionSource.Contains("\"Sum\", \"sum\"") &&
               automationSessionSource.Contains("\"Multiply\", \"multiply\"") &&
               automationSessionSource.Contains("\"Var read\", \"variable\", \"reader\"") &&
               automationSessionSource.Contains("\"Var write\", \"variable\", \"writer\"") &&
               automationSessionSource.Contains("Missile output components") &&
               automationSessionSource.Contains("Missile guidance") &&
               automationSessionSource.Contains("Missile thrust gate") &&
               automationSessionSource.Contains("Prox fuse") &&
               automationSessionSource.Contains("Alt fuse") &&
               automationSessionSource.Contains("GenericBlockGetter") &&
               automationSessionSource.Contains("GenericBlockSetter") &&
               automationSessionSource.Contains("Affect range") &&
               automationSessionSource.Contains("Search pattern") &&
               buildModeInputGateSource.Contains("_switchModeRequiresRelease") &&
               buildModeInputGateSource.Contains("_decorationEditToggleRequiresRelease") &&
               buildModeInputGateSource.Contains("_smartBuildToggleRequiresRelease") &&
               buildModeInputGateSource.Contains("_automationEditToggleRequiresRelease") &&
               buildModeInputGateSource.Contains("Time.frameCount") &&
               buildModeInputGateSource.Contains("ReadSwitchModeHeld") &&
               buildModeInputGateSource.Contains("ReadDecorationEditToggleHeld") &&
               buildModeInputGateSource.Contains("ReadSmartBuildToggleHeld") &&
               buildModeInputGateSource.Contains("ReadAutomationEditToggleHeld") &&
               buildModeInputGateSource.Contains("ReadProfileKey(") &&
               buildModeInputGateSource.Contains("KeyInputEventType.Held"),
            "Smart Builder and Automation Editor register at startup, default to Ctrl+Shift+B/Ctrl+Shift+A, reject direct opens over other ESU modes, and share one-press profiled input gates for Tab/Ctrl+D/Ctrl+Shift+B/Ctrl+Shift+A handoffs.");
        Assert(automationSessionSource.Contains("DrawStatusStrip") &&
               automationSessionSource.Contains("PrepareAutomationLayout") &&
               automationSessionSource.Contains("HandleAutomationPanelResize") &&
               automationSessionSource.Contains("EsuHudLayout.DrawResizeGrip(_leftPanelRect") &&
               automationSessionSource.Contains("EsuHudLayout.DrawResizeGrip(_rightPanelRect") &&
               automationSessionSource.Contains("EsuHudLayout.DrawResizeGrip(_editorRect") &&
               automationSessionSource.Contains("FitEditorToViewport") &&
               automationSessionSource.Contains("DrawPlacementPreview") &&
               automationSessionSource.Contains("\"placing\"") &&
               automationSessionSource.Contains("Placement preview shows green") &&
               automationSessionSource.Contains("Automation controller placement rejected") &&
               automationSessionSource.Contains("AutomationInputScope.MouseOverUi || IsMouseCurrentlyOverUi()"),
            "Automation Editor shares the resizable full-screen ESU shell pattern, fits the graph editor to the central viewport, arms palette placement explicitly, previews target cells, logs placement failures, and blocks panel clicks from world placement.");
        Assert(automationRuntimeDiagnosticsSource.Contains("AutomationRuntimeDiagnostics") &&
               automationRuntimeDiagnosticsSource.Contains("AutomationRuntimeDiagnosticResult") &&
               automationRuntimeDiagnosticsSource.Contains("AvailableComponentTypes is empty") &&
               automationRuntimeDiagnosticsSource.Contains("GenericBlockGetter") &&
               automationRuntimeDiagnosticsSource.Contains("GenericBlockSetter") &&
               automationRuntimeDiagnosticsSource.Contains("ProbeExpandedBreadboardComponents") &&
               automationRuntimeDiagnosticsSource.Contains("Extended Breadboard component palette") &&
               automationRuntimeDiagnosticsSource.Contains("Variable Reader") &&
               automationRuntimeDiagnosticsSource.Contains("\"variable\", \"reader\"") &&
               automationRuntimeDiagnosticsSource.Contains("Variable Writer") &&
               automationRuntimeDiagnosticsSource.Contains("\"variable\", \"writer\"") &&
               automationRuntimeDiagnosticsSource.Contains("ProbeExistingProxyPropertyPickers") &&
               automationRuntimeDiagnosticsSource.Contains("ProxyPropertyOptions") &&
               automationRuntimeDiagnosticsSource.Contains("property-picker enumeration") &&
               automationRuntimeDiagnosticsSource.Contains("ProbeSwitchFailExpressionReadiness") &&
               automationRuntimeDiagnosticsSource.Contains("Switch node exposes input 2") &&
               automationRuntimeDiagnosticsSource.Contains("ProbeNativeBreadboardPersistenceSnapshot") &&
               automationRuntimeDiagnosticsSource.Contains("Native persistence fingerprint") &&
               automationRuntimeDiagnosticsSource.Contains("Generic proxy fingerprint") &&
               automationRuntimeDiagnosticsSource.Contains("NativePersistenceFingerprint") &&
               automationRuntimeDiagnosticsSource.Contains("GenericProxyFingerprint") &&
               automationRuntimeDiagnosticsSource.Contains("HasNativePersistenceFingerprint") &&
               automationRuntimeDiagnosticsSource.Contains("HasCompleteValidationEvidence") &&
               automationRuntimeDiagnosticsSource.Contains("SetNativePersistenceFingerprint") &&
               automationRuntimeDiagnosticsSource.Contains("SetGenericProxyFingerprint") &&
               automationRuntimeDiagnosticsSource.Contains("SetSwitchFailExpressionReadiness") &&
               automationRuntimeDiagnosticsSource.Contains("SwitchFailExpressionReady") &&
               automationRuntimeDiagnosticsSource.Contains("SwitchMaxVisibleInputs") &&
               automationRuntimeDiagnosticsSource.Contains("controller=\" + CleanSegment(controller.Label)") &&
               automationRuntimeDiagnosticsSource.Contains("CleanSegment(target.Label)") &&
               automationRuntimeDiagnosticsSource.Contains("StableHash64") &&
               automationRuntimeDiagnosticsSource.Contains("ComponentFingerprint") &&
               automationRuntimeDiagnosticsSource.Contains("WireFingerprints") &&
               automationRuntimeDiagnosticsSource.Contains("BreadboardComponentFamily") &&
               automationRuntimeDiagnosticsSource.Contains("Missile Breadboard advertised") &&
               automationRuntimeDiagnosticsSource.Contains("Linked ACB Controller") &&
               automationRuntimeDiagnosticsSource.Contains("Linked ACB target exposes ControlBlockData") &&
               automationRuntimeDiagnosticsSource.Contains("No linked world targets are present") &&
               automationRuntimeDiagnosticsSource.Contains("Runtime check: ") &&
               automationSessionSource.Contains("DrawRuntimeDiagnosticsPanel") &&
               automationSessionSource.Contains("RunAutomationRuntimeDiagnostics") &&
               automationSessionSource.Contains("Runtime checks") &&
               automationSessionSource.Contains("DrawRuntimeValidationEvidenceRows") &&
               automationSessionSource.Contains("DrawRuntimeEvidenceRow") &&
               automationSessionSource.Contains("Live evidence gates") &&
               automationSessionSource.Contains("Native graph fingerprint") &&
               automationSessionSource.Contains("Generic proxy fingerprint") &&
               automationSessionSource.Contains("Switch fail-expression readiness") &&
               automationSessionSource.Contains("DrawRuntimeValidationControls") &&
               automationSessionSource.Contains("Prepare validation graph") &&
               automationSessionSource.Contains("PrepareAutomationValidationGraph") &&
               automationSessionSource.Contains("ValidationOutputLink") &&
               automationSessionSource.Contains("RecipeIndexForLabel(\"Validation proof\")") &&
               automationSessionSource.Contains("Link a Breadboard-writable world target") &&
               automationSessionSource.Contains("Validation graph preparation failed") &&
               automationSessionSource.Contains("Validation graph prepared") &&
               automationSessionSource.Contains("Requires a writable linked target") &&
               automationSessionSource.Contains("creates expression-else Switch proof nodes") &&
               automationSessionSource.Contains("Complete all live evidence gates before capturing or comparing") &&
               automationSessionSource.Contains("Complete all live evidence gates before capturing a baseline") &&
               automationSessionSource.Contains("until all live evidence gates are OK") &&
               automationSessionSource.Contains("Capture baseline") &&
               automationSessionSource.Contains("Compare baseline") &&
               automationSessionSource.Contains("AutomationValidationBaseline") &&
               automationSessionSource.Contains("CaptureRuntimeValidationBaseline") &&
               automationSessionSource.Contains("CompareRuntimeValidationBaseline") &&
               automationSessionSource.Contains("Automation validation baseline match") &&
               automationSessionSource.Contains("same-value writes") &&
               automationSessionSource.Contains("EsuRuntimeLog.Info(\"Automation Editor\"") &&
               automationBreadboardInspectorSource.Contains("TryRewriteCurrentSettings") &&
               automationBreadboardInspectorSource.Contains("same-value probes") &&
               automationAcbInspectorSource.Contains("TryRewriteCurrentSettings") &&
               automationAcbInspectorSource.Contains("same-value probes") &&
               automationAcbControllerInspectorSource.Contains("TryRewriteCurrentButtonValues") &&
               automationAcbControllerInspectorSource.Contains("same-value probes") &&
               changeTestChecklistSource.Contains("## Automation Editor") &&
               changeTestChecklistSource.Contains("`Run checks`") &&
               changeTestChecklistSource.Contains("`Live evidence gates`") &&
               changeTestChecklistSource.Contains("native graph fingerprint") &&
               changeTestChecklistSource.Contains("Generic proxy fingerprint") &&
               changeTestChecklistSource.Contains("Switch fail-expression readiness") &&
               changeTestChecklistSource.Contains("`Controllers on craft`") &&
               changeTestChecklistSource.Contains("main") &&
               changeTestChecklistSource.Contains("subconstruct") &&
               changeTestChecklistSource.Contains("`Target search`") &&
               changeTestChecklistSource.Contains("role text") &&
               changeTestChecklistSource.Contains("`ACB Actions`") &&
               changeTestChecklistSource.Contains("`Breadboard Read`") &&
               changeTestChecklistSource.Contains("`Breadboard Write`") &&
               changeTestChecklistSource.Contains("matching role summaries") &&
               changeTestChecklistSource.Contains("rebind to the live target") &&
               changeTestChecklistSource.Contains("`(missing)`") &&
               changeTestChecklistSource.Contains("linked-target list rehydrates from persisted") &&
               changeTestChecklistSource.Contains("native proxies without manual relinking") &&
               changeTestChecklistSource.Contains("Inspect a linked target in the left panel") &&
               changeTestChecklistSource.Contains("category/roles/runtime") &&
               changeTestChecklistSource.Contains("reflective warning text") &&
               changeTestChecklistSource.Contains("`Open graph`") &&
               changeTestChecklistSource.Contains("edit a linked ACB node") &&
               changeTestChecklistSource.Contains("ACB Controller node's first buttons") &&
               changeTestChecklistSource.Contains("bottom strip reports page") &&
               changeTestChecklistSource.Contains("immediate native edit behavior") &&
               changeTestChecklistSource.Contains("Rule getter/setter proxies") &&
               changeTestChecklistSource.Contains("in-card proxy buttons") &&
               changeTestChecklistSource.Contains("auto-pick terms") &&
               changeTestChecklistSource.Contains("auto-select a likely FtD property") &&
               changeTestChecklistSource.Contains("Variable Reader") &&
               changeTestChecklistSource.Contains("Variable Writer") &&
               changeTestChecklistSource.Contains("`Shared variable bridge`") &&
               changeTestChecklistSource.Contains("Variable Reader/Writer bridge nodes") &&
               changeTestChecklistSource.Contains("extended component palette coverage") &&
               changeTestChecklistSource.Contains("property-picker enumeration") &&
               changeTestChecklistSource.Contains("`Prepare validation graph`") &&
               changeTestChecklistSource.Contains("unavailable or warns") &&
               changeTestChecklistSource.Contains("writable linked target") &&
               changeTestChecklistSource.Contains("`Validation proof`") &&
               changeTestChecklistSource.Contains("expression-else Switch proof nodes") &&
               changeTestChecklistSource.Contains("target-specific") &&
               changeTestChecklistSource.Contains("immediately refreshes the live evidence rows") &&
               changeTestChecklistSource.Contains("stay unavailable until all") &&
               changeTestChecklistSource.Contains("live evidence gates are OK") &&
               changeTestChecklistSource.Contains("`Capture baseline`") &&
               changeTestChecklistSource.Contains("`Compare baseline`") &&
               changeTestChecklistSource.Contains("Automation validation baseline") &&
               changeTestChecklistSource.Contains("Native persistence fingerprint") &&
               changeTestChecklistSource.Contains("Generic proxy fingerprint") &&
               changeTestChecklistSource.Contains("Switch") &&
               changeTestChecklistSource.Contains("fail-expression readiness") &&
               changeTestChecklistSource.Contains("linked identifier") &&
               changeTestChecklistSource.Contains("evaluator-safe names") &&
               changeTestChecklistSource.Contains("expression-else if/else recipe") &&
               changeTestChecklistSource.Contains("FtD exposes the needed Switch") &&
               changeTestChecklistSource.Contains("Code output target") &&
               changeTestChecklistSource.Contains("target's Generic Setter input") &&
               changeTestChecklistSource.Contains("auto-created proxy") &&
               changeTestChecklistSource.Contains("revert compile") &&
               changeTestChecklistSource.Contains("Save, reload, and confirm placed controllers"),
            "Automation Editor exposes in-game runtime diagnostics for live controller discovery, same-value native writes, proxy prerequisites, linked ACBs, and linked ACB Controllers.");
        Assert(selectionSource.Contains("build.BuildingWith?.Item") &&
               catalogSource.Contains("internal enum SmartBuildMaterial") &&
               catalogSource.Contains("BasicMaterials") &&
               catalogSource.Contains("TryCreateMaterialSource") &&
               catalogSource.Contains("FromMaterial") &&
               catalogSource.Contains("SmartBuildMaterial.Wood") &&
               catalogSource.Contains("SmartBuildMaterial.Stone") &&
               catalogSource.Contains("SmartBuildMaterial.Metal") &&
               catalogSource.Contains("SmartBuildMaterial.Alloy") &&
               catalogSource.Contains("SmartBuildMaterial.Glass") &&
               catalogSource.Contains("SmartBuildMaterial.Lead") &&
               catalogSource.Contains("SmartBuildMaterial.HeavyArmour") &&
               catalogSource.Contains("SmartBuildMaterial.Rubber") &&
               catalogSource.Contains("2d519ca8-1f12-4a8e-9340-aa6648b5e799") &&
               catalogSource.Contains("6c0bab88-aa88-4825-9cf5-55df36aa12b8") &&
               catalogSource.Contains("3cc75979-18ac-46c4-9a5b-25b327d99410") &&
               catalogSource.Contains("ShapeFamilies") &&
               catalogSource.Contains("AvailableShapeDescriptors") &&
               catalogSource.Contains("FamilyForShape") &&
               catalogSource.Contains("descriptor.IsGenerator") &&
               catalogSource.Contains("DiscoverStructuralFamilies") &&
               catalogSource.Contains("DownSlopeFromMaterial") &&
               catalogSource.Contains("AddItemDefinitions(container?.Components") &&
               catalogSource.Contains("m_AllCorrespondingItems") &&
               catalogSource.Contains("MatchesMaterial") &&
               catalogSource.Contains("ItemMaterialCode") &&
               catalogSource.Contains("GetInt(\"Material\", -1)") &&
               catalogSource.Contains("MaterialReferenceGuid") &&
               catalogSource.Contains("LastStructuralDiscoveryReport") &&
               catalogSource.Contains("DragSettings?.Geometry") &&
               catalogSource.Contains("SmartBuildShapeDescriptors.TryParseGeometry") &&
               catalogSource.Contains("TryLengthFromDownSlopeGeometry") &&
               catalogSource.Contains("bdafa446-f615-49cb-94f3-d7652dde6cec") &&
               catalogSource.Contains("ModificationComponentContainerItem") &&
               descriptorSource.Contains("FacingDownSlope1m_") &&
               descriptorSource.Contains("Beam slope L") &&
               descriptorSource.Contains("Square-backed L") &&
               descriptorSource.Contains("Inverted L") &&
               descriptorSource.Contains("SquareBacked") &&
               descriptorSource.Contains("InverseTransition") &&
               descriptorSource.Contains("Offset") &&
               descriptorSource.Contains("SmartBuildShapeCategory.Generated") &&
               descriptorSource.Contains("generated-circle") &&
               descriptorSource.Contains("generated-polygon") &&
               descriptorSource.Contains("generated-sphere") &&
               descriptorSource.Contains("generatorSidesPreset") &&
               committerSource.Contains("PlaceBlockCommand") &&
               committerSource.Contains("MirrorInfo.none") &&
               committerSource.Contains("plan.Construct"),
            "Smart Block Builder has an internal 8-material picker, keeps selected-item compatibility available, resolves FtD item definitions and structural geometry metadata, and commits vanilla block commands.");
        Assert(itemPreviewRendererSource.Contains("Definition?.GetMesh()") &&
               itemPreviewRendererSource.Contains("definition?.GetMaterial()") &&
               itemPreviewRendererSource.Contains("Graphics.DrawMesh") &&
               itemPreviewRendererSource.Contains("DrawPlacementWire") &&
               itemPreviewRendererSource.Contains("ConfigureTransparent") &&
               itemPreviewRendererSource.Contains("CandidateKey") &&
               itemPreviewRendererSource.Contains("GetCachedPreview") &&
               itemPreviewRendererSource.Contains("GetPreview(SmartBlockCandidate") &&
               itemPreviewRendererSource.Contains("ClearCache") &&
               itemPreviewRendererSource.Contains("HardEdgeDotThreshold") &&
               itemPreviewRendererSource.Contains("HardEdgesFor") &&
               itemPreviewRendererSource.Contains("EdgeAccumulator") &&
               itemPreviewRendererSource.Contains("ShouldDrawHardEdge") &&
               itemPreviewRendererSource.Contains("Func<Vector3, Vector3, bool> shouldDrawLocalEdge") &&
               itemPreviewRendererSource.Contains("localMatrix.MultiplyPoint3x4") &&
               itemPreviewRendererSource.Contains("Mathf.Abs(Vector3.Dot"),
            "Smart Builder item preview renderer uses FtD ItemDefinition meshes/materials for world ghosts and rotating palette thumbnails, caches preview-safe materials, and extracts filterable hard/boundary mesh edges instead of all triangle diagonals.");
        Assert(sessionSource.Contains("DrawShapePreviewGrid") &&
               sessionSource.Contains("DrawShapeListRows") &&
               sessionSource.Contains("FilterShapePaletteEntries") &&
               sessionSource.Contains("ShapePaletteViewportHeight") &&
               sessionSource.Contains("ClampShapePaletteScroll") &&
               sessionSource.Contains("DrawShapePreviewCard") &&
               sessionSource.Contains("DrawShapePaletteToolbar") &&
               !sessionSource.Contains("DrawShapeCategoryFilterButton(\"generated\", \"Generated\"") &&
               sessionSource.Contains("AvailablePaletteShapeDescriptors") &&
               sessionSource.Contains("AvailableGeneratorDescriptors") &&
               sessionSource.Contains("SmartRightPanelPage") &&
               sessionSource.Contains("DrawRightPanelPageTabs") &&
               sessionSource.Contains("DrawGeneratorBrowserSection") &&
               !sessionSource.Contains("DrawGeneratorPaletteSection") &&
               !sessionSource.Contains("GeneratorSectionHeight") &&
               !sessionSource.Contains("_showGeneratorSection") &&
               sessionSource.Contains("DrawGeneratorToolGrid") &&
               sessionSource.Contains("_shapePreviewGrid") &&
               sessionSource.Contains("_shapeCategoryFilter") &&
               sessionSource.Contains("_shapeFilter") &&
               sessionSource.Contains("ShapePreviewGridTexturePixels") &&
               sessionSource.Contains("DrawShapePaletteEmptyRow") &&
               sessionSource.Contains("No shapes match the current palette filter.") &&
               sessionSource.Contains("Filter shapes by label, item name, or FtD geometry name."),
            "Smart Builder shape palette is compact, searchable, categorized, scroll-clamped, supports list/grid mesh previews, and routes generated tools to a full right-panel generator page.");
        string drawShapePaletteSource = ExtractMethodSource(sessionSource, "DrawShapePalette");
        string shapePaletteViewportHeightSource = ExtractMethodSource(sessionSource, "ShapePaletteViewportHeight");
        Assert(drawShapePaletteSource.Contains("GUILayout.BeginScrollView") &&
               !drawShapePaletteSource.Contains("GUI.BeginGroup(viewport)") &&
               drawShapePaletteSource.Contains("DrawShapePreviewGrid(rows") &&
               drawShapePaletteSource.Contains("DrawShapeListRows(rows") &&
               shapePaletteViewportHeightSource.Contains("remainingHeight") &&
               !shapePaletteViewportHeightSource.Contains("contentHeight") &&
               !shapePaletteViewportHeightSource.Contains("430f"),
            "Smart Builder shape palette renders rows directly in an expanded panel-sized scroll view instead of clipping or content-clamping the list.");
        string drawShapeListRowsSource = ExtractMethodSource(sessionSource, "DrawShapeListRows");
        string drawShapePreviewGridSource = ExtractMethodSource(sessionSource, "DrawShapePreviewGrid");
        string shapePreviewGridLayoutSource = ExtractMethodSource(sessionSource, "ShapePreviewGridLayoutFor");
        string clampShapePaletteScrollSource = ExtractMethodSource(sessionSource, "ClampShapePaletteScroll");
        Assert(drawShapeListRowsSource.Contains("_hoveredShapeEntry = entry") &&
               drawShapeListRowsSource.Contains("row.Contains(Event.current.mousePosition)") &&
               sessionSource.Contains("DrawShapePreviewCard") &&
               drawShapePreviewGridSource.Contains("firstRow") &&
               drawShapePreviewGridSource.Contains("lastRow") &&
               drawShapePreviewGridSource.Contains("ShapePreviewGridLayoutFor(viewportWidth)") &&
               drawShapePreviewGridSource.Contains("layout.Columns") &&
               drawShapePreviewGridSource.Contains("layout.CardWidth") &&
               drawShapePreviewGridSource.Contains("layout.PreviewHeight") &&
               drawShapePreviewGridSource.Contains("GUILayoutUtility.GetRect") &&
               drawShapePreviewGridSource.Contains("canRenderPreview") &&
               drawShapePreviewGridSource.Contains("GetPreview(entry.Candidate") &&
               drawShapePreviewGridSource.Contains("GetCachedPreview(entry.Candidate") &&
               !drawShapePreviewGridSource.Contains("Descriptor?.IsGenerator") &&
               shapePreviewGridLayoutSource.Contains("viewportWidth - outerPadding * 2f") &&
               shapePreviewGridLayoutSource.Contains("(available + gap) / (minCardWidth + gap)") &&
               shapePreviewGridLayoutSource.Contains("cardWidth * ShapePreviewGridCardAspect") &&
               clampShapePaletteScrollSource.Contains("ShapePreviewGridLayoutFor(viewportWidth)") &&
               !sessionSource.Contains("ShapePreviewGridColumns") &&
               !sessionSource.Contains("ShapePreviewGridCardWidth =") &&
               !sessionSource.Contains("ShapePreviewGridCardHeight ="),
            "Smart Builder list rows drive hover preview cards, while 3D grid thumbnails are adaptive to the palette viewport and remain virtualized to visible cards with cached off-repaint previews.");
        string cycleShapeShortcutSource = ExtractMethodSource(sessionSource, "CycleShapeShortcut");
        Assert(cycleShapeShortcutSource.Contains("OneMetreShapeDescriptors()") &&
               cycleShapeShortcutSource.Contains("_selectedSlopeLength = 1") &&
               sessionSource.Contains("HasOneMetreShapeCandidate") &&
               sessionSource.Contains("PaletteCandidateForLength(descriptor, 1)") &&
               !cycleShapeShortcutSource.Contains("_tool == SmartBuildTool.Draw"),
            "Smart Builder 1-key shape cycling is independent of the active tool, filters to 1m-capable shapes, and forces the selected size back to 1m.");
        Assert(pieceSource.Contains("SmartBuildGeneratorFillMode") &&
               pieceSource.Contains("SmartBuildGeneratorSmoothingMode") &&
               pieceSource.Contains("CreateGeneratedShape") &&
               pieceSource.Contains("CreateGeneratedShapeForPreview") &&
               pieceSource.Contains("CreateGeneratedShapePreview") &&
               pieceSource.Contains("EnumerateGeneratedCircleCells") &&
               pieceSource.Contains("EnumerateGeneratedPolygonCells") &&
               pieceSource.Contains("EnumerateGeneratedSphereCells") &&
               pieceSource.Contains("BuildGeneratedSmoothingPlacements") &&
               pieceSource.Contains("GeneratorRoundLock") &&
               pieceSource.Contains("GeneratorControlBounds") &&
               pieceSource.Contains("StartGeneratedComponent") &&
               pieceSource.Contains("StartComponentCenteredOnBounds") &&
               sceneSource.Contains("GeneratedPattern") &&
               sceneSource.Contains("patternWarnings") &&
               sessionSource.Contains("DrawGeneratedPieceSettings") &&
               sessionSource.Contains("DrawGeneratorDescriptorPreview") &&
               sessionSource.Contains("DrawGeneratorCellSilhouette") &&
               sessionSource.Contains("DrawPolygonSideControls") &&
               sessionSource.Contains("SetGeneratedFillMode") &&
               sessionSource.Contains("ToggleGeneratedSmoothing") &&
               sessionSource.Contains("ToggleGeneratedRoundLock") &&
               sessionSource.Contains("SetGeneratedSides") &&
               sessionSource.Contains("_draft.IsGeneratedShape && _draft.GeneratorRoundLock && _dragAxes.Length > 1"),
            "Smart Builder generated block shapes are first-class pieces with palette wiring, selected-piece controls, planning warnings, and smoothing support.");
        Assert(sessionSource.Contains("ClearSmartPreviewRendererCache") &&
               sessionSource.Contains("_itemPreviewRenderer?.ClearCache()") &&
               ExtractMethodSource(sessionSource, "SetSelectedMaterial").Contains("ClearSmartPreviewRendererCache();") &&
               ExtractMethodSource(sessionSource, "RefreshSelection").Contains("ClearSmartPreviewRendererCache();") &&
               ExtractMethodSource(sessionSource, "RefreshSelection").Contains("HasActivePreviewScene") &&
               sessionSource.Contains("s_shapeStackBottomRatio") &&
               sessionSource.Contains("s_selectedSceneStackBottomRatio") &&
               sessionSource.Contains("DrawShapePanelStack") &&
               sessionSource.Contains("SplitSmartVerticalStack") &&
               sessionSource.Contains("HandleSmartStackDividerDrag") &&
               sessionSource.Contains("DrawSmartStackDividerGrip"),
            "Smart Builder material changes clear preview caches and the Shapes panel uses persisted Palette/Selected/Scene split dividers.");
        string drawPlacementPreviewSource = ExtractMethodSource(sessionSource, "DrawPlacementPreview");
        string drawPlanPlacementPreviewSource = ExtractMethodSource(sessionSource, "DrawPlanPlacementPreview");
        string drawPiecePreviewSource = ExtractMethodSource(sessionSource, "DrawPiecePreview");
        string drawVoxelOuterHullSource = ExtractMethodSource(sessionSource, "DrawVoxelOuterHull");
        string getGeneratedHullPreviewSource = ExtractMethodSource(sessionSource, "GetGeneratedHullPreview");
        string drawGeneratedHullPreviewSource = ExtractMethodSource(sessionSource, "DrawGeneratedHullPreview");
        string previewCellCountSource = ExtractMethodSource(sessionSource, "PreviewCellCount");
        string tryDrawFixedGeometryPiecePreviewSource = ExtractMethodSource(sessionSource, "TryDrawFixedGeometryPiecePreview");
        string drawSlopeStepHullSource = ExtractMethodSource(sessionSource, "DrawSlopeStepHull");
        string drawPlacementGhostSource = ExtractMethodSource(sessionSource, "DrawPlacementGhost");
        Assert(sessionSource.Contains("DrawPlanPlacementPreview") &&
               sessionSource.Contains("plan.Placements") &&
               sessionSource.Contains("DrawPlacementPreview(plan.Construct") &&
               sessionSource.Contains("drawWire: false") &&
               sessionSource.Contains("drewExactMaterialMeshes") &&
               sessionSource.Contains("DrawPiecePreview(piece, variant, invalid, drawn, drawMaterialFill: !drewExactMaterialMeshes)") &&
               draftSource.Contains("MaterialOnly") &&
               sessionSource.Contains("SmartBuildPreviewMode.MaterialOnly") &&
               sessionSource.Contains("PreviewModeStripButton(new Rect(x, y, EsuHudLayout.Scale(96f), buttonHeight), SmartBuildPreviewMode.MaterialOnly, \"Mat only\"") &&
               sessionSource.Contains("ShouldDrawMaterialPreview") &&
               sessionSource.Contains("ShouldDrawPreviewWire") &&
               drawPlanPlacementPreviewSource.Contains("plan.Placements.Count <= exact") &&
               drawPlanPlacementPreviewSource.Contains("_scene?.Pieces.Any(piece => piece.IsGeneratedShape)") &&
               drawPlanPlacementPreviewSource.Contains("DrawGeneratedPieceHull(") &&
               drawPlanPlacementPreviewSource.Contains("DrawVolumeFaces(fallbackCorners, fallbackMaterial)") &&
               drawPlanPlacementPreviewSource.Contains("if (drawWire && ShouldDrawPreviewWire())") &&
               drawPlacementPreviewSource.Contains("if (!drawWire || !ShouldDrawPreviewWire())") &&
               drawPlacementPreviewSource.Contains("ShouldDrawMaterialPreview()") &&
               drawPiecePreviewSource.IndexOf("piece.IsGeneratedShape", StringComparison.Ordinal) <
               drawPiecePreviewSource.IndexOf("piece.IsFixedGeometry", StringComparison.Ordinal) &&
               drawPiecePreviewSource.Contains("DrawGeneratedPieceHull(") &&
               drawVoxelOuterHullSource.Contains("GetGeneratedHullPreview(rawCells, null)") &&
               sessionSource.Contains("_generatedHullPreviewCache") &&
               sessionSource.Contains("GeneratedPreviewCacheLimit") &&
               sessionSource.Contains("MaxGeneratedPreviewWireEdges") &&
               sessionSource.Contains("MaxGeneratedPreviewMaterialFaces") &&
               sessionSource.Contains("SmartGeneratedHullPreview") &&
               sessionSource.Contains("BuildGeneratedHullPreview") &&
               sessionSource.Contains("MergeGeneratedHullEdges(edges)") &&
               sessionSource.Contains("GeneratedEdgeRunGroup") &&
               sessionSource.Contains("AddGeneratedVoxelFaceIfExposed") &&
               getGeneratedHullPreviewSource.Contains("_generatedHullPreviewCache.TryGetValue") &&
               getGeneratedHullPreviewSource.Contains("GeneratedPreviewCacheLimit") &&
               drawGeneratedHullPreviewSource.Contains("PreviewStride(preview.Faces.Count, MaxGeneratedPreviewMaterialFaces)") &&
               drawGeneratedHullPreviewSource.Contains("PreviewStride(preview.Edges.Count, MaxGeneratedPreviewWireEdges)") &&
               drawGeneratedHullPreviewSource.Contains("construct.SafeLocalToGlobal(edge.Start)") &&
               previewCellCountSource.Contains("if (_previewCells.Count > 0)") &&
               sessionSource.Contains("VoxelFaceEdgeSideOffsets") &&
               sessionSource.Contains("sideCell + normalOffset") &&
               tryDrawFixedGeometryPiecePreviewSource.Contains("if (ShouldDrawPreviewWire())") &&
               drawSlopeStepHullSource.Contains("if (!ShouldDrawPreviewWire())") &&
               sessionSource.Contains("PlacementMatrix") &&
               sessionSource.Contains("Matrix4x4.TRS(ToVector3(placement.Position), placement.Rotation") &&
               sessionSource.Contains("TryDrawFixedGeometryPiecePreview") &&
               sessionSource.Contains("BuildInternalPreviewFaces") &&
               sessionSource.Contains("ShouldDrawFixedGeometryPreviewEdge") &&
               sessionSource.Contains("SmartBuildInternalFace") &&
               sessionSource.Contains("PlacementLocalMatrix") &&
               sessionSource.Contains("SmartBuildPieceScene.MirrorPlacement") &&
               sceneSource.Contains("internal static SmartBuildPlacement MirrorPlacement") &&
               drawPiecePreviewSource.Contains("piece.IsFixedGeometry") &&
               tryDrawFixedGeometryPiecePreviewSource.Contains("BuildFixedPlacements") &&
               tryDrawFixedGeometryPiecePreviewSource.Contains("DrawPlacementWire(") &&
               tryDrawFixedGeometryPiecePreviewSource.Contains("ShouldDrawFixedGeometryPreviewEdge") &&
               tryDrawFixedGeometryPiecePreviewSource.Contains("MaxExactMeshPreviewPlacements") &&
               drawPlacementGhostSource.Contains("selectedDescriptor.IsGenerator") &&
               drawPlacementGhostSource.Contains("CreateGeneratedShapePreview") &&
               drawPlacementGhostSource.Contains("DrawGeneratedPieceHull(") &&
               drawPlacementGhostSource.Contains("ghost.BuildFixedPlacements") &&
               drawPlacementGhostSource.Contains("slopeGhost.BuildFixedPlacements") &&
               drawPlacementGhostSource.Contains("ShouldDrawPreviewWire()") &&
               itemPreviewRendererSource.Contains("mesh.triangles"),
            "Smart Builder mesh-accurate material previews, fixed-shape scene wires, and cached/budgeted generated hull previews are derived from commit placements, with shared-face filtering and a material-only preview mode that suppresses wire overlays.");
        string handleMouseSource = ExtractMethodSource(sessionSource, "HandleMouse");
        Assert(handleMouseSource.Contains("EndDrag(resetDraft: true)") &&
               handleMouseSource.Contains("CancelAddMode()") &&
               handleMouseSource.Contains("DeselectSmartBuilderPiece()") &&
               handleMouseSource.Contains("TryOpenPreviewContextMenu()") &&
               sessionSource.Contains("_scene.ClearSelection()") &&
               sceneSource.Contains("ClearSelection") &&
               sceneSource.Contains("HasSelection") &&
               sceneSource.Contains("selectedId >= 0"),
            "Smart Builder right-click cancels active edits before deselecting pieces, preserves no-selection preview scenes, and only then opens context menus for pointed pieces.");
        Assert(inputScopeSource.Contains("SmartBuildInputScope.SuppressBuildHud") &&
               tooltipSuppressorSource.Contains("DecorationEditorInputScope.Active || SmartBuildInputScope.Active") &&
               vanillaInputBridgeSource.Contains("cBuild.ToggleFreeze") &&
               vanillaInputBridgeSource.Contains("KeyInputsFtd.Freeze") &&
               readFreezeDownSource.Contains("return FtdKeyMap.Instance.Bool(") &&
               readFreezeDownSource.Contains("return Input.GetKeyDown(KeyCode.CapsLock);") &&
               !readFreezeDownSource.Contains("|| Input.GetKeyDown(KeyCode.CapsLock)") &&
               pluginSource.Contains("ResolveBuildFreezeTarget") &&
               smartInputScopeSource.Contains("ControlHeldWhileActive") &&
               smartInputScopeSource.Contains("DecoLimitLifter.EsuInputState.IsControlHeld()") &&
               smartInputScopeSource.Contains("ControlHeldWhileActive ||"),
            "Smart Block Builder shares the modal build/Ctrl input guard and ESU forwards vanilla freeze through the profiled key map with Caps Lock only as a profile-failure fallback.");
        Assert(geometrySource.Contains("internal Vector3[] GetLocalCorners()") &&
               geometrySource.Contains("internal static SmartBuildVolume FromBounds") &&
               !ExtractMethodSource(geometrySource, "GetWorldCorners").Contains("EnumerateCells"),
            "Smart Block Builder preview bounds expose local corners and compute world corners without enumerating every cell.");
        Assert(draftSource.Contains("SmartBuildDraft") &&
               draftSource.Contains("SmartBuildDrawPlane") &&
               draftSource.Contains("SmartBuildOccupancyMode") &&
               sessionSource.Contains("TrySeedFromDrawPlane") &&
               sessionSource.Contains("TrySeedFromPointedBlock") &&
               sessionSource.Contains("AdjacentCellFromSurfaceHit") &&
               sessionSource.Contains("localHit - center") &&
               !sessionSource.Contains("cell = hit.Anchor + SmartBuildAxisHelper.ToVector3i(axis, sign)") &&
               sessionSource.Contains("SmartBlockFamilyCatalog.TryCreateMaterialSource") &&
               !sessionSource.Contains("SmartBuildSelectionResolver.TryResolve") &&
               !sessionSource.Contains("MaterialButton()") &&
               sessionSource.Contains("ViewButton()") &&
               sessionSource.Contains("DrawViewModeMenu") &&
               sessionSource.Contains("DecorationEditorViewModeController") &&
               sessionSource.Contains("ApplyFocusView") &&
               sessionSource.Contains("RestoreFocusView") &&
               sessionSource.Contains("DrawMaterialSelector") &&
               sessionSource.Contains("CycleSelectedMaterial") &&
               sessionSource.Contains("SymmetryButton(DecorationEditAxis.X)") &&
               sessionSource.Contains("SymmetryButton(DecorationEditAxis.Y)") &&
               sessionSource.Contains("SymmetryButton(DecorationEditAxis.Z)") &&
               sessionSource.Contains("BuildPreviewSymmetrySets") &&
               sceneSource.Contains("BuildPlanFromCells") &&
               sceneSource.Contains("ISmartBuildPattern") &&
               pieceSource.Contains("SmartBuildShapeKind") &&
               pieceSource.Contains("CreateDownSlope") &&
               pieceSource.Contains("EnumerateDownSlopeSupportCells") &&
               sessionSource.Contains("RefreshPreviewVolumesOnly") &&
               sessionSource.Contains("BuildPreviewVolumesOnly") &&
               sessionSource.Contains("DrawDraftFaceHighlight") &&
               sessionSource.Contains("TryPickFace") &&
                sessionSource.Contains("TryPickFacePlane") &&
                draftSource.Contains("SmartBuildEditHandleMode") &&
                draftSource.Contains("SmartBuildPreviewMode") &&
                sessionSource.Contains("HandleModeStripButton(new Rect") &&
                sessionSource.Contains("SmartBuildEditHandleMode.Gizmo, \"Gizmo\"") &&
                sessionSource.Contains("SmartBuildEditHandleMode.Face, \"Face\"") &&
                sessionSource.Contains("SmartBuildEditHandleMode.Edge, \"Edge\"") &&
                sessionSource.Contains("SmartBuildEditHandleMode.Corner, \"Corner\"") &&
                sessionSource.Contains("PreviewModeStripButton(new Rect") &&
                sessionSource.Contains("SmartBuildPreviewMode.Wireframe, \"Wireframe\"") &&
                sessionSource.Contains("SmartBuildPreviewMode.Material, \"Material\"") &&
                sessionSource.Contains("SmartBuildPreviewMode.MaterialOnly, \"Mat only\"") &&
                sessionSource.Contains("TryPickEdge") &&
                sessionSource.Contains("TryPickCorner") &&
                sessionSource.Contains("TryGetEdgeWorldCorners") &&
                sessionSource.Contains("TryGetCornerWorld") &&
                sessionSource.Contains("TryGetFaceWorldCorners") &&
                sessionSource.Contains("_planDirty") &&
                !sessionSource.Contains("DrawDraftCells") &&
               !sessionSource.Contains("MaxPreviewCellsWithFaces") &&
               sessionSource.Contains("EveryPreviewSetTouchesExistingConstruct") &&
               sessionSource.Contains("Every symmetry preview must touch an existing block before Apply.") &&
               sessionSource.Contains("CreatePreviewAtSeed") &&
               sessionSource.Contains("SmartBuildPieceScene") &&
               sessionSource.Contains("TrySeedFromPreviewFace") &&
               sessionSource.Contains("TrySelectPreviewPieceAtPointer") &&
                sessionSource.Contains("TryOpenPreviewContextMenu") &&
                sessionSource.Contains("DrawPreviewContextMenu") &&
                sessionSource.Contains("DrawShapePanel") &&
                sessionSource.Contains("DrawShapePalette") &&
                sessionSource.Contains("DrawShapeButton(SmartBuildShapeDescriptor") &&
                sessionSource.Contains("DrawShapeSizeButton") &&
                sessionSource.Contains("CategoryLabel") &&
                sessionSource.Contains("SmartBuildShapeCategory.Basic") &&
                sessionSource.Contains("SmartBuildShapeCategory.Transitions") &&
                sessionSource.Contains("SmartBuildShapeCategory.Generated") &&
                sessionSource.Contains("length.ToString(CultureInfo.InvariantCulture)") &&
                sessionSource.Contains("length + \"m shape size.\"") &&
                sessionSource.Contains("ToolbarPanelToggle(\"settings\", \"Info\", ref _showLeftPanel") &&
                sessionSource.Contains("ToolbarPanelToggle(\"build\", \"Shapes\", ref _showRightPanel") &&
                !sessionSource.Contains("DrawPieceActionToolbar(budget.RightControlsWidth)") &&
                sessionSource.Contains("ToolButton(SmartBuildTool.Draw, \"create\", \"Add\"") &&
                sessionSource.Contains("_selectedShapeDescriptorKey") &&
                sessionSource.Contains("SelectedShapeDescriptor") &&
                sessionSource.Contains("SelectShapeDescriptor") &&
                sessionSource.Contains("AvailableShapeDescriptors") &&
                !sessionSource.Contains("\"S\" + length.ToString(CultureInfo.InvariantCulture)") &&
                descriptorSource.Contains("Place normal cuboids and beams.") &&
                descriptorSource.Contains("Place 1m to 4m down-slope ramp segments.") &&
                descriptorSource.Contains("\"Slope\"") &&
                descriptorSource.Contains("Beam slope L") &&
                descriptorSource.Contains("Triangle corner L") &&
                descriptorSource.Contains("slope-transition-1-2-left") &&
                descriptorSource.Contains("Place slope transition blocks.") &&
                descriptorSource.Contains("Generate a block circle or filled disk/cylinder.") &&
                descriptorSource.Contains("Generate a block sphere shell or solid sphere.") &&
                sessionSource.Contains("ArmAddMode") &&
                sessionSource.Contains("Scale mode active") &&
                sessionSource.Contains("DuplicateSelectedPiece") &&
                sessionSource.Contains("DeleteSelectedPiece") &&
                pieceSource.Contains("Duplicate(Vector3i offset)") &&
                pieceSource.Contains("CompactSceneLabel") &&
                pieceSource.Contains("EnumeratePlacementCells") &&
                pieceSource.Contains("CreateFixedShape") &&
                pieceSource.Contains("CreateGeneratedShape") &&
                pieceSource.Contains("BuildFixedGeometryPlacements") &&
                pieceSource.Contains("ApplyGeneratedBounds") &&
                pieceSource.Contains("RecenterGeneratedAround") &&
                pieceSource.Contains("EnumerateFixedGeometryCells") &&
                pieceSource.Contains("EnumerateSlopeLines") &&
                pieceSource.Contains("SmartBuildSlopeSupportMode") &&
                pieceSource.Contains("EnumerateDownSlopeStepSupportCells") &&
                !sessionSource.Contains("DrawSlopeLinePrism") &&
                sessionSource.Contains("DrawPlacementGhost") &&
                sessionSource.Contains("DrawSlopeStepHull") &&
                sessionSource.Contains("DrawDoubleSidedSlopeQuad") &&
                sessionSource.Contains("DrawSupportHulls") &&
                sessionSource.Contains("DrawVolumeFaces") &&
                sessionSource.Contains("SolidMaterialPreviewColor") &&
                !sessionSource.Contains("private Color MaterialPreviewColor(") &&
                sceneSource.Contains("FixedGeometryPattern") &&
                sceneSource.Contains("MirrorCandidate") &&
                sceneSource.Contains("MirrorRotation(placement.Rotation, variant)") &&
                sceneSource.Contains("SmartBuildShapeDescriptors.IsOddMirror") &&
                sceneSource.Contains("QuaternionFromBasis(right, up, forward)") &&
                pieceSource.Contains("SupportMode") &&
                pieceSource.Contains("SetSupportMode") &&
                sessionSource.Contains("ToggleSlopeSupportMode") &&
                sessionSource.Contains("Support: Step") &&
                sessionSource.Contains("DrawCellWire") &&
                sessionSource.Contains("private void YawQuickButton()") &&
                sessionSource.Contains("private void FlipQuickButton()") &&
               sessionSource.Contains("ShouldShowPieceActionButtons(rightControlsWidth)") &&
               sessionSource.Contains("rightControlsWidth >= EsuHudLayout.Scale") &&
               sessionSource.Contains("AttentionIconButton(\"save\", \"Apply\"") &&
               sessionSource.Contains("AttentionIconButton(\"cancel\", \"Cancel\"") &&
               sessionSource.Contains("IconButton(\"close\", \"Close\"") &&
               sessionSource.Contains("ToolButton(SmartBuildTool.Rotate") &&
               sessionSource.Contains("DrawRotateGizmo") &&
               sessionSource.Contains("DrawRotationRing") &&
               sessionSource.Contains("TryPickRotationRing") &&
               sessionSource.Contains("RotationPivotLocal") &&
               sessionSource.Contains("BeginRotateDrag") &&
               sessionSource.Contains("UpdateRotateDrag") &&
               sessionSource.Contains("EndRotateDrag") &&
               sessionSource.Contains("_rotateDragAccumulatedDegrees") &&
               sessionSource.Contains("Mathf.DeltaAngle(previousAngle, currentAngle)") &&
               !ExtractMethodSource(sessionSource, "UpdateRotateDrag").Contains("Vector2.SignedAngle") &&
               sessionSource.Contains("RotateSnapDegrees = 90f") &&
               sessionSource.Contains("TryPickDownSlopeFace") &&
               sessionSource.Contains("EnumerateDownSlopeHandleFaces") &&
               sessionSource.Contains("TryBuildSlopeStepHullLocal") &&
               sessionSource.Contains("piece.DropAxis") &&
               !sessionSource.Contains("WithY(") &&
               pieceSource.Contains("RecenterDownSlopeAround") &&
               pieceSource.Contains("NearestCellToCenter") &&
               sessionSource.Contains("UndoSceneEdit") &&
               sessionSource.Contains("RedoSceneEdit") &&
               sessionSource.Contains("SmartBuildSceneSnapshot") &&
               sceneSource.Contains("ReplaceWith") &&
               !sessionSource.Contains("ToolbarReservedSlot(") &&
               sessionSource.Contains("s_rightPanelRect") &&
               sessionSource.Contains("RotateYaw") &&
                sessionSource.Contains("CanPlaceDraftOrigin") &&
                sessionSource.Contains("_tool = SmartBuildTool.Draw") &&
                sessionSource.Contains("_tool = SmartBuildTool.Scale;") &&
                sessionSource.Contains("IsShiftHeld()") &&
                sessionSource.Contains("TrySeedFromPreviewFace(") &&
                sessionSource.Contains("inheritPreviewScale") &&
                sessionSource.Contains("SlabFromPreviewFace") &&
                sessionSource.Contains("CuboidSize") &&
                sessionSource.Contains("Smart Builder origin is occupied. Pick an empty cell.") &&
               sessionSource.Contains("HandleLeftPanelResize") &&
               sessionSource.Contains("EsuHudLayout.DrawResizeGrip(_leftPanelRect") &&
               sessionSource.Contains("MinLeftPanelWidth") &&
               sessionSource.Contains("private const float LeftPanelDefaultHeight = 507f") &&
               sessionSource.Contains("new Rect(18f, 110f, LeftPanelWidth, LeftPanelDefaultHeight)") &&
               sessionSource.Contains("EsuHudLayout.Scale(LeftPanelDefaultHeight)") &&
               sessionSource.Contains("DefaultLeftPanelRect") &&
               sessionSource.Contains("PlanTouchesExistingConstruct") &&
               committerSource.Contains("TryOrderPlacementsForCommit") &&
               committerSource.Contains("PlacementTouchesConstructOrPlacedCells") &&
               committerSource.Contains("remaining planned blocks are disconnected") &&
                sessionSource.Contains("SmartBuildOccupancyMode.SkipOccupied") &&
                sessionSource.Contains("Input.GetMouseButtonDown(2)") &&
                !ExtractMethodSource(sessionSource, "HandleMouse").Contains("CancelPreview()") &&
                !behaviourSource.Contains("AllGameControlsEnabled") &&
                sessionSource.Contains("DecorationEditorTheme") &&
                sessionSource.Contains("DrawSmartPanelHeader(\"Smart Block Builder\", \"build\", ref _showLeftPanel)") &&
                sessionSource.Contains("DrawSmartPanelHeader(RightPanelTitle(), \"build\", ref _showRightPanel)") &&
                sessionSource.Contains("RightPanelTitle()") &&
                sessionSource.Contains("DrawRightPanelPageTabs(pageTabs)") &&
               !sessionSource.Contains("new GUIContent(\" Smart Block Builder\", DecorationEditorIconCatalog.Get(\"build\"))") &&
                sessionSource.Contains("GUILayout.BeginHorizontal(GUILayout.Width(budget.LeftRailWidth))") &&
                sessionSource.Contains("EsuHudLayout.LocalPanelInnerRect(_toolbarRect.width, _toolbarRect.height)") &&
                sessionSource.Contains("private void ModeSwitchButton()") &&
                !sessionSource.Contains("IconButton(\"open\", \"Deco\"") &&
                sessionSource.Contains("EsuHudNotifications.DrawToolbarSlot(") &&
                sessionSource.Contains("new Vector2(_toolbarRect.x + frame.Rect.x") &&
               notificationSource.Contains("screenOrigin.x + rect.x") &&
               notificationSource.Contains("screenOrigin.y + rect.y") &&
               sessionSource.Contains("DecorationEditorOverlay.Quad") &&
               overlaySource.Contains("ScreenCullMarginPixels") &&
               overlaySource.Contains("IsScreenSegmentOutside") &&
               overlaySource.Contains("IsScreenQuadOutside") &&
               overlaySource.Contains("OverlayQuad"),
            "Smart Block Builder uses a runtime multi-piece scene, arms Add from the palette, switches to Scale after placement, has readable bottom-handle controls, draws placement ghosts and outer slope/support hulls, avoids right-click scene clearing, commits connected placements first, tolerates middle-mouse cursor input, and shares the ESU toolbar notification slot.");
        string smartStatusStripSource = ExtractMethodSource(sessionSource, "DrawStatusStrip");
        string smartStatusRightLabelSource = ExtractMethodSource(sessionSource, "DrawStatusRightLabel");
        string notifySmartPlanIssueSource = ExtractMethodSource(sessionSource, "NotifyPlanIssueIfNeeded");
        string smartPlanIssueMessageSource = ExtractMethodSource(sessionSource, "TryGetPlanIssueMessage");
        Assert(sessionSource.Contains("Mathf.Clamp(Screen.height * 0.13f") &&
               !sessionSource.Contains("private const float StatusHeight = 56f") &&
               smartStatusStripSource.Contains("DrawSmartBottomHeader") &&
               smartStatusStripSource.Contains("DrawCyanLine") &&
               smartStatusStripSource.Contains("DrawSmartBottomControls") &&
               !smartStatusStripSource.Contains("GUILayout.BeginHorizontal") &&
               sessionSource.Contains("_lastPlanIssueNotificationKey") &&
               notifySmartPlanIssueSource.Contains("EsuHudNotifications.ShowSystem(") &&
               notifySmartPlanIssueSource.Contains("\"Smart Builder\"") &&
               smartPlanIssueMessageSource.Contains("_plan.FailureReason.Trim()") &&
               smartPlanIssueMessageSource.Contains("Smart Builder skipped {0:N0} occupied preview cell{1}.") &&
               smartPlanIssueMessageSource.Contains("EsuHudNotificationKind.Warning") &&
               smartStatusRightLabelSource.Contains("text = \"Blocked\"") &&
               !smartStatusRightLabelSource.Contains("_plan.FailureReason") &&
               sessionSource.Contains("\"Smart Block Builder\"") &&
               sessionSource.Contains("\"Mode: Smart | Tab to Automation when clean\""),
            "Smart Builder bottom bar uses the shared Deco/Surface-style fixed panel rhythm, keeps plan warnings in the shared notification/log flow, and does not clip full failure reasons into the status label.");
        Assert(smartInputScopeSource.Contains("ClaimCameraInputForFrames") &&
               smartInputScopeSource.Contains("ClaimMouseWheelInputForFrames") &&
               smartInputScopeSource.Contains("GuiDisplayBase.MouseWheelInUse.Now()") &&
               smartInputScopeSource.Contains("SuppressCameraInput() =>") &&
               !smartInputScopeSource.Contains("MouseOverUi ||") &&
               !smartInputScopeSourceNormalized.Contains("ControlHeldWhileActive ||\n            OwnsCameraInputThisFrame") &&
               !sessionSource.Contains("if (_draft != null)\r\n                SmartBuildInputScope.ClaimBuildInputForFrames") &&
               sessionSource.Contains("RefreshMouseOverUiFromCurrentPointer();") &&
               sessionSource.Contains("private void RefreshMouseOverUiFromCurrentPointer()") &&
               sessionSource.Contains("EsuHudNotifications.ContainsMouse(mouse)") &&
               sessionSource.Contains("_viewModeMenuOpen && ViewModeMenuRect(_toolbarRect).Contains(mouse)") &&
               sessionSource.Contains("SmartBuildInputScope.ClaimMouseWheelInputForFrames();") &&
               sessionSource.Contains("ShouldConsumeGuiEvent(Event.current)") &&
               sessionSource.Contains("SwitchToAutomationEditRequested") &&
               sessionSource.Contains("CanSwitchToAutomationEdit"),
            "Smart Block Builder leaves camera/WASD live while idle and only suppresses camera input for handle drags or panel scrolls.");
        Assert(readmeDocumentationSource.Contains("SmartBuildDraft") &&
               readmeDocumentationSource.Contains("Middle mouse may") &&
               readmeDocumentationSource.Contains("show the FTD cursor without closing Smart Builder") &&
               readmeDocumentationSource.Contains("Right click cancels") &&
               readmeDocumentationSource.Contains("RGB rotation rings") &&
               readmeDocumentationSource.Contains("Support: Full/Step") &&
               !readmeDocumentationSource.Contains("green yaw ring") &&
               !readmeDocumentationSource.Contains("Support: Full/Flat") &&
               !readmeDocumentationSource.Contains("changed to **Flat**") &&
               smartBuilderHudDocSource.Contains("Right click cancels") &&
               smartBuilderHudDocSource.Contains("Support: Full/Step") &&
               !smartBuilderHudDocSource.Contains("Support: Full/Flat") &&
               !smartBuilderHudDocSource.Contains("Flat support") &&
               smartBuilderHudDocSource.Contains("Wireframe") &&
               smartBuilderHudDocSource.Contains("context menu") &&
               smartBuilderHudDocSource.Contains("wide slope") &&
               inGameTestPlanSource.Contains("click empty space") &&
               inGameTestPlanSource.Contains("sloped placement ghost") &&
               inGameTestPlanSource.Contains("Support: Step") &&
               inGameTestPlanSource.Contains("middle mouse shows") &&
               inGameTestPlanSource.Contains("without closing"),
            "Smart Block Builder editable-preview workflow is documented for implementation notes and in-game smoke testing.");
        Assert(!registrationSource.Contains("ChatGUI.Instance"),
            "Smart Block Builder hotkey/open guards do not construct ChatGUI during boot.");
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
               manifest.Contains("\"version\": \"1.0.7\"") &&
               manifest.Contains("\"workshop_id\": 3755667314") &&
               manifest.Contains("EndlessShapesUnlimited.dll") &&
               manifest.Contains("\"DecoLimitLifter\"") &&
               manifest.Contains("\"EndlessShapes2\""),
            "Package manifest has the combined identity and standalone-mod conflicts.");
        var workshopHeader = new FileInfo(Path.Combine(package, "header.jpg"));
        Assert(workshopHeader.Exists &&
               workshopHeader.Length > 0 &&
               workshopHeader.Length < 1_000_000,
            "Steam Workshop preview header.jpg is packaged and below the 1 MB upload limit.");
        Assert(!File.Exists(Path.Combine(package, "SMART_BUILDER_HUD.md")),
            "Developer-only Smart Builder HUD notes stay outside the shippable mod package.");

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

    private static string ReadDocumentationText(string root, params string[] relativeParts)
    {
        var builder = new StringBuilder();
        AppendOptionalFile(builder, Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
        AppendOptionalFile(builder, Path.Combine(root, "README.md"));
        AppendOptionalFile(builder, Path.Combine(root, "EndlessShapesUnlimited", "README.md"));
        return builder.ToString();
    }

    private static void AppendOptionalFile(StringBuilder builder, string path)
    {
        if (!File.Exists(path))
            return;

        if (builder.Length > 0)
            builder.AppendLine();
        builder.Append(File.ReadAllText(path));
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
        byte objectIdBytes,
        uint headerId = 123U,
        byte dataOffset = 0)
    {
        SuperSaver saver = NewSaver(headerCount, dataBytes);
        if (headerCount > 0U)
            ByteConversion.ConvertIn(saver.Header, 0U, 3, headerId);
        int bytesToWrite = checked((int)dataBytes);
        for (int i = 0; i < bytesToWrite; i++)
            saver.DataSorted[i] = (byte)(dataOffset + i + 1);

        SuperSerialisationLayout layout =
            SuperSerialisationLayout.Create(headerCount, dataBytes, objectIdBytes);
        var bytes = new byte[layout.TotalBytes];
        uint cursor = 0U;
        saver.Serialise(bytes, ref cursor, objectId, objectIdBytes);
        if (cursor != (uint)bytes.Length)
            throw new InvalidOperationException("Fixture serializer did not fill the expected byte count.");
        return bytes;
    }

    private sealed class VerificationVector3Converter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Vector3);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var vector = (Vector3)value;
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(vector.x);
            writer.WritePropertyName("y");
            writer.WriteValue(vector.y);
            writer.WritePropertyName("z");
            writer.WriteValue(vector.z);
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
            throw new NotSupportedException();
    }

    private sealed class VerificationVector4Converter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Vector4);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var vector = (Vector4)value;
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(vector.x);
            writer.WritePropertyName("y");
            writer.WriteValue(vector.y);
            writer.WritePropertyName("z");
            writer.WriteValue(vector.z);
            writer.WritePropertyName("w");
            writer.WriteValue(vector.w);
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
            throw new NotSupportedException();
    }

    private sealed class VerificationQuaternionConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Quaternion);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var quaternion = (Quaternion)value;
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(quaternion.x);
            writer.WritePropertyName("y");
            writer.WriteValue(quaternion.y);
            writer.WritePropertyName("z");
            writer.WriteValue(quaternion.z);
            writer.WritePropertyName("w");
            writer.WriteValue(quaternion.w);
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
            throw new NotSupportedException();
    }

    private sealed class VerificationColorConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(Color);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var color = (Color)value;
            writer.WriteStartObject();
            writer.WritePropertyName("r");
            writer.WriteValue(color.r);
            writer.WritePropertyName("g");
            writer.WriteValue(color.g);
            writer.WritePropertyName("b");
            writer.WriteValue(color.b);
            writer.WritePropertyName("a");
            writer.WriteValue(color.a);
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
            throw new NotSupportedException();
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
