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
            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());

                Patches.ByteStorePatch.EnsureMegaBytes();
                Patches.SuperSaverBuffersPatch.OnBootEnsurePools();
                VerifyRequiredPatches();
                Patches.DecoLimitsPatch.ApplyLimit();

                AdvLogger.LogInfo(
                    $"[EndlessShapes Unlimited] v{version} loaded. " +
                    $"Decoration limit={DecoLimits.MaxDecorations}; OBJ tools active.");
            }
            catch (Exception exception)
            {
                // Do not leave a partially patched serializer active.
                try { harmony?.UnpatchAll(HarmonyId); }
                catch { /* preserve the original startup failure */ }

                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Failed to install required patches",
                    exception,
                    LogOptions._AlertDevAndCustomerInGame);
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
        }

        public bool AfterAllPluginsLoaded() => true;
        public void OnSave() { }
    }
}
