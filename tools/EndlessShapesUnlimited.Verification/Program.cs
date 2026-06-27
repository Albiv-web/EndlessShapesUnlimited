using System;
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
using DecoLimitLifter.ExtendedSerialization;
using EndlessShapes2;
using HarmonyLib;

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
            VerifyOversizedPacketRejected();
            VerifyTruncatedPacketRejected();
            VerifyInvalidHeaderLengthRejected();
            VerifyInvalidHeaderOffsetRejected();
            VerifyConfiguredSerializerCeilings();
            VerifyObjectIdWidthValidation();
            VerifyObjParser();
            VerifyObjParserLimits();
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
