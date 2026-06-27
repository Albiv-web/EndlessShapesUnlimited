using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.DataManagement.Serialisation;
using BrilliantSkies.DataManagement.Serialisation.VariableTypes;
using BrilliantSkies.Ftd.Constructs.UI;
using BrilliantSkies.Modding;
using EndlessShapes2;
using HarmonyLib;

namespace DecoLimitLifter
{
    public sealed class Plugin : GamePlugin_PostLoad
    {
        private const string HarmonyId = "alb.endlessshapesunlimited";

        public string name => "EndlessShapes Unlimited";
        public Version version => new Version(1, 0, 0, 0);

        public void OnLoad()
        {
            Harmony harmony = null;
            var startup = new StartupTransaction(
                () => harmony?.UnpatchAll(HarmonyId));
            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());

                Patches.ByteStorePatch.EnsureMegaBytes();
                Patches.SuperSaverBuffersPatch.OnBootEnsurePools();
                VerifyRequiredPatches();
                ExtendedSerialization.DestinationBuffer.VerifyAutoSyncTarget();
                int previousDecorationLimit = Patches.DecoLimitsPatch.ApplyLimit();
                startup.TrackDecorationLimit(
                    previousDecorationLimit,
                    Patches.DecoLimitsPatch.RestoreLimit);
                startup.Commit();
            }
            catch (Exception exception)
            {
                // Rollback actions are independent and cannot hide the startup failure.
                IReadOnlyList<Exception> rollbackErrors = startup.Rollback();
                Exception logged = rollbackErrors.Count == 0
                    ? exception
                    : new AggregateException(
                        "Startup failed and rollback encountered additional errors.",
                        new[] { exception }.Concat(rollbackErrors));
                try
                {
                    AdvLogger.LogException(
                        "[EndlessShapes Unlimited] Failed to install required patches",
                        logged,
                        LogOptions._AlertDevAndCustomerInGame);
                }
                catch
                {
                    // Logging must never leave a failed startup partially installed.
                }
                return;
            }

            try
            {
                AdvLogger.LogInfo(
                    $"[EndlessShapes Unlimited] v{version} loaded. " +
                    $"Decoration limit={DecoLimits.MaxDecorations}; OBJ tools active.");
            }
            catch (Exception exception)
            {
                try
                {
                    AdvLogger.LogException(
                        "[EndlessShapes Unlimited] Loaded, but the success message could not be written",
                        exception,
                        LogOptions._AlertDevInGame);
                }
                catch
                {
                    // Startup is already committed; logging cannot change that state.
                }
            }
        }

        private static void VerifyRequiredPatches()
        {
            var required = new List<MethodBase>
            {
                AccessTools.Method(typeof(SuperSaver), nameof(SuperSaver.Serialise)),
                AccessTools.Method(typeof(SuperSaver), nameof(SuperSaver.ConvertToReader)),
                AccessTools.Method(typeof(SuperSaver), nameof(SuperSaver.WriteHeader)),
                AccessTools.Constructor(typeof(SuperSaver), Type.EmptyTypes),
                EndlessShapes2Patch.ResolveTarget()
            };

            required.AddRange(typeof(SuperLoader)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(method => method.Name == nameof(SuperLoader.Deserialise))
                .Where(method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length == 3 &&
                           parameters[0].ParameterType == typeof(byte[]) &&
                           parameters[1].ParameterType == typeof(uint).MakeByRefType() &&
                           parameters[2].ParameterType == typeof(byte);
                }));

            var writeMap = typeof(SuperSaver).GetInterfaceMap(typeof(IVariableWriteHelp));
            for (int i = 0; i < writeMap.InterfaceMethods.Length; i++)
            {
                var method = writeMap.InterfaceMethods[i];
                var parameters = method.GetParameters();
                if (method.Name == nameof(IVariableWriteHelp.ByIdHelpWrite) &&
                    parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(uint) &&
                    parameters[1].ParameterType == typeof(uint))
                {
                    required.Add(writeMap.TargetMethods[i]);
                    break;
                }
            }

            var missing = required
                .Where(method => method == null ||
                    Harmony.GetPatchInfo(method)?.Owners?.Contains(HarmonyId) != true)
                .Select(method => method == null
                    ? "<unresolved target>"
                    : $"{method.DeclaringType?.FullName}.{method.Name}")
                .ToArray();

            if (missing.Length > 0)
                throw new InvalidOperationException(
                    "Required Harmony patches are missing: " + string.Join(", ", missing));

            MethodBase constructor = AccessTools.Constructor(typeof(SuperSaver), Type.EmptyTypes);
            VerifyExactPatch(
                constructor,
                AccessTools.Method(
                    typeof(Patches.SuperSaverBuffersPatch),
                    "ConstructorPrefix"),
                prefix: true);
            VerifyExactPatch(
                constructor,
                AccessTools.Method(
                    typeof(Patches.ByteStorePatch),
                    "AfterSuperSaverConstructor"),
                prefix: false);
        }

        private static void VerifyExactPatch(MethodBase target, MethodInfo patchMethod, bool prefix)
        {
            HarmonyLib.Patches patchInfo = target == null ? null : Harmony.GetPatchInfo(target);
            IEnumerable<Patch> patches = prefix ? patchInfo?.Prefixes : patchInfo?.Postfixes;
            if (target == null || patchMethod == null ||
                patches?.Any(patch => patch.owner == HarmonyId && patch.PatchMethod == patchMethod) != true)
            {
                throw new InvalidOperationException(
                    $"Required Harmony {(prefix ? "prefix" : "postfix")} is missing: " +
                    $"{patchMethod?.DeclaringType?.FullName}.{patchMethod?.Name}");
            }
        }

        public bool AfterAllPluginsLoaded() => true;
        public void OnSave() { }
    }
}
