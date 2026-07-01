using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Assets.Scripts.Persistence;
using BrilliantSkies.Core.FilesAndFolders;
using BrilliantSkies.Core.JsonPlus.Converters;
using BrilliantSkies.Core.Logger;
using HarmonyLib;
using Newtonsoft.Json;

namespace DecoLimitLifter.Patches
{
    internal static class BlueprintJsonStreamingSave
    {
        internal const long StreamingThresholdBytes = 64L * 1024L * 1024L;

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly FieldInfo BaseFileSourceField =
            AccessTools.Field(typeof(BaseFile), "_fileSource");
        private static readonly FieldInfo BlueprintFileBlockCountField =
            AccessTools.Field(typeof(BlueprintFile), "<BlockCount>k__BackingField");
        private static readonly FieldInfo BlueprintFileMaterialCostField =
            AccessTools.Field(typeof(BlueprintFile), "<MaterialCost>k__BackingField");
        private static readonly FieldInfo BlueprintFileContainedMaterialCostField =
            AccessTools.Field(typeof(BlueprintFile), "<ContainedMaterialCost>k__BackingField");
        private static readonly FieldInfo BlueprintFileVersionField =
            AccessTools.Field(typeof(BlueprintFile), "<Version>k__BackingField");

        internal static bool TryHandleBlueprintFileSave(
            BlueprintFile file,
            Blueprint blueprint,
            out bool result)
        {
            result = false;
            if (!Enabled || file == null || blueprint == null)
                return false;

            BlueprintFileModel model;
            string path = BlueprintFilePath(file);
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                model = BlueprintFileModelHelp.GenerateBlueprintFileModel(blueprint, file.Name);
            }
            catch (Exception exception)
            {
                LogException("prepare a blueprint file save for streamed JSON routing", exception);
                return false;
            }

            if (!TryShouldStream(
                    model,
                    Formatting.None,
                    PreserveReferencesHandling.None,
                    out long estimatedBytes))
            {
                return false;
            }

            try
            {
                StreamSave(
                    path,
                    model,
                    Formatting.None,
                    PreserveReferencesHandling.None,
                    estimatedBytes);
                UpdateBlueprintFileMetadata(file, model);
                result = true;
            }
            catch (Exception exception)
            {
                LogException(
                    "stream a large blueprint JSON save to " + path,
                    exception);
                result = false;
            }

            return true;
        }

        internal static void WrapBlueprintFileManager(
            FileManager<int, global::Blueprint, BlueprintFileModel> fileManager)
        {
            if (fileManager?.SaveFunction == null)
                return;

            var vanillaSave = fileManager.SaveFunction;
            fileManager.SaveFunction = (model, path) =>
            {
                if (TryHandleFileManagerSave(path, model))
                    return;

                vanillaSave(model, path);
            };
        }

        internal static bool TryHandleFileManagerSave(
            string path,
            BlueprintFileModel model)
        {
            if (!TryShouldStream(
                    model,
                    Formatting.None,
                    PreserveReferencesHandling.None,
                    out long estimatedBytes))
            {
                return false;
            }

            try
            {
                StreamSave(
                    path,
                    model,
                    Formatting.None,
                    PreserveReferencesHandling.None,
                    estimatedBytes);
                return true;
            }
            catch (Exception exception)
            {
                LogException(
                    "stream a large campaign blueprint JSON save to " + path,
                    exception);
                throw;
            }
        }

        internal static bool TryShouldStream(
            BlueprintFileModel model,
            Formatting formatting,
            PreserveReferencesHandling referencesHandling,
            out long estimatedBytes)
        {
            return TryShouldStream(
                model,
                formatting,
                referencesHandling,
                StreamingThresholdBytes,
                Enabled,
                out estimatedBytes);
        }

        internal static bool ShouldStreamForVerification(
            BlueprintFileModel model,
            Formatting formatting,
            PreserveReferencesHandling referencesHandling,
            long thresholdBytes,
            bool enabled,
            out long estimatedBytes,
            JsonConverter[] converters = null)
        {
            return TryShouldStream(
                model,
                formatting,
                referencesHandling,
                thresholdBytes,
                enabled,
                out estimatedBytes,
                converters);
        }

        internal static void StreamSaveForVerification(
            string path,
            BlueprintFileModel model,
            Formatting formatting,
            PreserveReferencesHandling referencesHandling,
            JsonConverter[] converters = null)
        {
            StreamSave(
                path,
                model,
                formatting,
                referencesHandling,
                MeasureJsonBytesForVerification(
                    model,
                    formatting,
                    referencesHandling,
                    converters ?? JsonConverters.Converters),
                converters ?? JsonConverters.Converters);
        }

        private static bool TryShouldStream(
            BlueprintFileModel model,
            Formatting formatting,
            PreserveReferencesHandling referencesHandling,
            long thresholdBytes,
            bool enabled,
            out long estimatedBytes,
            JsonConverter[] converters = null)
        {
            estimatedBytes = 0L;
            if (!enabled || model == null)
                return false;

            try
            {
                estimatedBytes = converters == null
                    ? MeasureJsonBytes(model, formatting, referencesHandling)
                    : MeasureJsonBytesForVerification(model, formatting, referencesHandling, converters);
                return estimatedBytes >= thresholdBytes;
            }
            catch (Exception exception)
            {
                LogException("preflight a blueprint JSON save", exception);
                return false;
            }
        }

        internal static long MeasureJsonBytes(
            BlueprintFileModel model,
            Formatting formatting,
            PreserveReferencesHandling referencesHandling)
        {
            return MeasureJsonBytesForVerification(
                model,
                formatting,
                referencesHandling,
                JsonConverters.Converters);
        }

        internal static long MeasureJsonBytesForVerification(
            BlueprintFileModel model,
            Formatting formatting,
            PreserveReferencesHandling referencesHandling,
            JsonConverter[] converters)
        {
            var writer = new CountingTextWriter(Utf8NoBom);
            using (var json = CreateJsonTextWriter(writer, formatting))
            {
                CreateSerializer(formatting, referencesHandling, converters).Serialize(json, model);
                json.Flush();
            }
            return writer.BytesWritten;
        }

        internal static string SerializeToStringForVerification(
            BlueprintFileModel model,
            Formatting formatting,
            PreserveReferencesHandling referencesHandling,
            JsonConverter[] converters = null)
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            using (var json = CreateJsonTextWriter(writer, formatting))
            {
                CreateSerializer(
                    formatting,
                    referencesHandling,
                    converters ?? JsonConverters.Converters).Serialize(json, model);
                json.Flush();
                return writer.ToString();
            }
        }

        private static bool Enabled
        {
            get
            {
                try
                {
                    return SerializationHud.SerializationHudProfile
                        .Data
                        .StreamLargeBlueprintJsonSaves;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static string BlueprintFilePath(BlueprintFile file)
        {
            var source = BaseFileSourceField?.GetValue(file) as IFileSource;
            return source?.FilePath;
        }

        private static void UpdateBlueprintFileMetadata(
            BlueprintFile file,
            BlueprintFileModel model)
        {
            BlueprintFileBlockCountField?.SetValue(file, model.SavedTotalBlockCount);
            BlueprintFileMaterialCostField?.SetValue(file, (double)model.SavedMaterialCost);
            BlueprintFileContainedMaterialCostField?.SetValue(file, (double)model.ContainedMaterialCost);
            BlueprintFileVersionField?.SetValue(file, model.Version);
        }

        private static void StreamSave(
            string path,
            BlueprintFileModel model,
            Formatting formatting,
            PreserveReferencesHandling referencesHandling,
            long estimatedBytes)
        {
            StreamSave(
                path,
                model,
                formatting,
                referencesHandling,
                estimatedBytes,
                JsonConverters.Converters);
        }

        private static void StreamSave(
            string path,
            BlueprintFileModel model,
            Formatting formatting,
            PreserveReferencesHandling referencesHandling,
            long estimatedBytes,
            JsonConverter[] converters)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Blueprint save path is empty.", nameof(path));

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string fileName = Path.GetFileName(path);
            string tempDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
            string tempPath = Path.Combine(
                tempDirectory,
                fileName + ".esu-stream-" + Guid.NewGuid().ToString("N") + ".Temp");

            bool tempOwned = false;
            try
            {
                using (var stream = new FileStream(
                           tempPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           64 * 1024))
                using (var writer = new StreamWriter(stream, Utf8NoBom, 64 * 1024))
                using (var json = CreateJsonTextWriter(writer, formatting))
                {
                    tempOwned = true;
                    CreateSerializer(formatting, referencesHandling, converters).Serialize(json, model);
                    json.Flush();
                }

                ReplaceWithTemp(path, tempPath);
                tempOwned = false;
                LogInfo(
                    "Streamed large blueprint JSON save " +
                    FormatBytes(estimatedBytes) +
                    " to " +
                    path);
            }
            finally
            {
                if (tempOwned)
                    TryDelete(tempPath);
            }
        }

        private static void ReplaceWithTemp(string path, string tempPath)
        {
            if (File.Exists(path))
            {
                File.Replace(
                    tempPath,
                    path,
                    path + "_backup",
                    ignoreMetadataErrors: true);
                return;
            }

            File.Move(tempPath, path);
        }

        private static JsonTextWriter CreateJsonTextWriter(
            TextWriter writer,
            Formatting formatting)
        {
            return new JsonTextWriter(writer)
            {
                Formatting = formatting,
                CloseOutput = false
            };
        }

        private static JsonSerializer CreateSerializer(
            Formatting formatting,
            PreserveReferencesHandling referencesHandling)
        {
            return CreateSerializer(
                formatting,
                referencesHandling,
                JsonConverters.Converters);
        }

        private static JsonSerializer CreateSerializer(
            Formatting formatting,
            PreserveReferencesHandling referencesHandling,
            JsonConverter[] converters)
        {
            var serializer = new JsonSerializer
            {
                Formatting = formatting,
                PreserveReferencesHandling = referencesHandling,
                TypeNameHandling = TypeNameHandling.None,
                DefaultValueHandling = DefaultValueHandling.Include,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
                NullValueHandling = NullValueHandling.Include
            };

            foreach (JsonConverter converter in converters ?? Array.Empty<JsonConverter>())
                serializer.Converters.Add(converter);
            return serializer;
        }

        private static string FormatBytes(long bytes)
        {
            const double mebibyte = 1024d * 1024d;
            return (bytes / mebibyte).ToString("0.# MiB", CultureInfo.InvariantCulture);
        }

        private static void LogInfo(string message)
        {
            try
            {
                AdvLogger.LogInfo("[EndlessShapes Unlimited] " + message);
            }
            catch
            {
            }
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

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private sealed class CountingTextWriter : TextWriter
        {
            private readonly Encoding _encoding;

            internal CountingTextWriter(Encoding encoding)
            {
                _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            }

            public override Encoding Encoding => _encoding;

            internal long BytesWritten { get; private set; }

            public override void Write(char value)
            {
                BytesWritten += value <= 0x7F
                    ? 1
                    : _encoding.GetByteCount(new[] { value });
            }

            public override void Write(char[] buffer, int index, int count)
            {
                if (buffer == null || count <= 0)
                    return;
                BytesWritten += _encoding.GetByteCount(buffer, index, count);
            }

            public override void Write(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return;
                BytesWritten += _encoding.GetByteCount(value);
            }
        }
    }

    [HarmonyPatch(typeof(BlueprintFile), nameof(BlueprintFile.Save))]
    internal static class BlueprintFile_Save_BlueprintJsonStreaming_Patch
    {
        private static bool Prefix(
            BlueprintFile __instance,
            [HarmonyArgument(0)] Blueprint blueprint,
            ref bool __result)
        {
            if (!BlueprintJsonStreamingSave.TryHandleBlueprintFileSave(
                    __instance,
                    blueprint,
                    out bool result))
            {
                return true;
            }

            __result = result;
            return false;
        }
    }

    [HarmonyPatch(typeof(FileManagerMaker), nameof(FileManagerMaker.CreateBlueprintFileModelSaver))]
    internal static class FileManagerMaker_CreateBlueprintFileModelSaver_BlueprintJsonStreaming_Patch
    {
        private static void Postfix(
            ref FileManager<int, global::Blueprint, BlueprintFileModel> __result)
        {
            BlueprintJsonStreamingSave.WrapBlueprintFileManager(__result);
        }
    }
}
