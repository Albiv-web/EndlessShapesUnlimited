using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using BrilliantSkies.Core.Serialisation.Bytes;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.DataManagement.Serialisation.VariableTypes;
using DecoLimitLifter;
using DecoLimitLifter.ExtendedSerialization;
using DecoLimitLifter.Patches;
using EndlessShapes2;
using EndlessShapes2.Polygon;
using HarmonyLib;
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
            VerifyFlexibleFloatParsing();
            VerifyImagePreflight();
            VerifyGeometryClassification();
            VerifyGeometryRejectionAndLimit();
            VerifyLargeGeometryRun();
            VerifyTetherMath();
            VerifyTetherTransaction();
            VerifyExporterFormatting();
            VerifyUiPatchTarget();
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
            "DecoLimitLifter.Patches.SuperSaver_Serialise_Patch"
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
