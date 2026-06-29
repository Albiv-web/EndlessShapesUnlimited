using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BrilliantSkies.Core.Logger;
using DecoLimitLifter.SmartBuildMode;
using HarmonyLib;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class DecorationTooltipSuppressor
    {
        private static bool _installed;
        private static int _patchedBlockTooltipColorCheckCount;

        internal static int PatchedTipSetterCount => 0;
        internal static int PatchedBlockTooltipColorCheckCount => _patchedBlockTooltipColorCheckCount;

        internal static bool EsuOwnsEditorView =>
            DecorationEditorInputScope.Active || SmartBuildInputScope.Active;

        internal static MethodBase ResolveBlockGetToolTipTarget()
        {
            Type blockType = AccessTools.TypeByName("Block");
            Type settingsType = AccessTools.TypeByName("IInteractionSettings");
            if (blockType == null || settingsType == null)
                return null;

            return AccessTools.Method(
                blockType,
                "GetToolTip",
                new[] { settingsType });
        }

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (_installed)
                return;

            MethodBase target = ResolveBlockGetToolTipTarget();
            MethodInfo transpiler = AccessTools.Method(
                typeof(DecorationTooltipSuppressor),
                nameof(Transpiler));
            if (target == null || transpiler == null)
                throw new InvalidOperationException(
                    "Unable to resolve FTD Block.GetToolTip Tip_Colored paint-tooltip suppression target.");

            _patchedBlockTooltipColorCheckCount = 0;
            harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
            if (_patchedBlockTooltipColorCheckCount != 2)
                throw new InvalidOperationException(
                    "FTD Block.GetToolTip paint-tooltip patch expected exactly 2 color checks, patched " +
                    _patchedBlockTooltipColorCheckCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ".");

            _installed = true;
            LogInfo("Scoped Tip_Colored paint-hover tooltip suppression installed for Block.GetToolTip.");
        }

        internal static bool ShouldIncludeVanillaPaintTooltipLine(int color)
        {
            return color != 0 && !EsuOwnsEditorView;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            FieldInfo colorField = ResolveBlockColorField();
            MethodInfo shouldInclude = AccessTools.Method(
                typeof(DecorationTooltipSuppressor),
                nameof(ShouldIncludeVanillaPaintTooltipLine));
            if (colorField == null || shouldInclude == null)
                throw new InvalidOperationException(
                    "Unable to resolve FTD Block.color Tip_Colored paint-tooltip suppression members.");

            int outerChecks = 0;
            int innerChecks = 0;
            for (int i = 0; i < codes.Count - 2; i++)
            {
                if (!LoadsBlockColor(codes[i], colorField))
                    continue;

                if (IsTruthyBranch(codes[i + 1]))
                {
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, shouldInclude));
                    outerChecks++;
                    i++;
                    continue;
                }

                if (i + 2 < codes.Count &&
                    IsLdcI4Zero(codes[i + 1]) &&
                    codes[i + 2].opcode == OpCodes.Cgt_Un)
                {
                    codes[i + 1].opcode = OpCodes.Call;
                    codes[i + 1].operand = shouldInclude;
                    codes[i + 2].opcode = OpCodes.Nop;
                    codes[i + 2].operand = null;
                    innerChecks++;
                    i++;
                }
            }

            _patchedBlockTooltipColorCheckCount = outerChecks + innerChecks;
            if (outerChecks != 1 || innerChecks != 1)
                throw new InvalidOperationException(
                    "Expected one outer and one inner Block.color check in Block.GetToolTip; patched " +
                    outerChecks.ToString(System.Globalization.CultureInfo.InvariantCulture) + " outer and " +
                    innerChecks.ToString(System.Globalization.CultureInfo.InvariantCulture) + " inner checks.");

            return codes;
        }

        private static FieldInfo ResolveBlockColorField()
        {
            Type blockType = AccessTools.TypeByName("Block");
            return blockType == null ? null : AccessTools.Field(blockType, "color");
        }

        private static bool LoadsBlockColor(CodeInstruction instruction, FieldInfo colorField)
        {
            return instruction.opcode == OpCodes.Ldfld &&
                   Equals(instruction.operand, colorField);
        }

        private static bool IsTruthyBranch(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Brtrue ||
                   instruction.opcode == OpCodes.Brtrue_S;
        }

        private static bool IsLdcI4Zero(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldc_I4_0 ||
                   (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int value && value == 0) ||
                   (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte shortValue && shortValue == 0);
        }

        internal static bool IsLegacyPaintHoverMessage(object[] arguments)
        {
            return false;
        }

        internal static void ClearActiveTooltipState(bool force = false)
        {
        }

        private static void LogInfo(string message)
        {
            try
            {
                AdvLogger.LogInfo("[EndlessShapes Unlimited] " + message);
            }
            catch
            {
                // Diagnostics must not block startup or editor input.
            }
        }
    }
}
