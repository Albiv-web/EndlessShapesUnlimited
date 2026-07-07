using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using UnityEngine;

namespace DecoLimitLifter.AutomationEditMode
{
    internal enum AutomationTargetCategory
    {
        All,
        Controllers,
        AcbActions,
        BreadboardReadable,
        BreadboardWritable,
        Movement,
        Subobjects,
        Utility,
        Media,
        Spinblocks,
        TurretsWeapons,
        Propulsion,
        Pistons,
        Pumps,
        ControlSurfaces,
        Ai,
        Missiles,
        Lights,
        ShieldsDefence,
        Detection,
        DoorsDocking,
        SoundDisplay,
        ResourcePower,
        Other
    }

    internal sealed class AutomationTarget
    {
        internal AutomationTarget(
            AllConstruct construct,
            Block block,
            Vector3i localPosition,
            AutomationTargetCategory category,
            string label,
            AutomationControllerDescriptor controller = null)
        {
            Construct = construct;
            Block = block;
            LocalPosition = localPosition;
            Category = category;
            Label = string.IsNullOrWhiteSpace(label) ? "Block" : label;
            Controller = controller;
        }

        internal AllConstruct Construct { get; }

        internal Block Block { get; }

        internal Vector3i LocalPosition { get; }

        internal AutomationTargetCategory Category { get; }

        internal string Label { get; }

        internal AutomationControllerDescriptor Controller { get; }

        internal bool IsController => Controller != null;

        internal string RuntimeType => Block == null ? string.Empty : Block.GetType().Name;

        internal string StableKey =>
            ConstructKey(Construct) + "|" +
            LocalPosition.x.ToString(CultureInfo.InvariantCulture) + "," +
            LocalPosition.y.ToString(CultureInfo.InvariantCulture) + "," +
            LocalPosition.z.ToString(CultureInfo.InvariantCulture) + "|" +
            RuntimeType;

        internal string PersistenceKey =>
            AutomationTargetCatalog.PersistenceKeyFor(this);

        internal Vector3 WorldCenter
        {
            get
            {
                if (Construct == null)
                    return new Vector3(LocalPosition.x, LocalPosition.y, LocalPosition.z);

                try
                {
                    return Construct.SafeLocalToGlobal(
                        new Vector3(LocalPosition.x, LocalPosition.y, LocalPosition.z));
                }
                catch
                {
                    return Construct.myTransform == null
                        ? new Vector3(LocalPosition.x, LocalPosition.y, LocalPosition.z)
                        : Construct.myTransform.TransformPoint(
                            new Vector3(LocalPosition.x, LocalPosition.y, LocalPosition.z));
                }
            }
        }

        private static string ConstructKey(AllConstruct construct)
        {
            if (construct == null)
                return "null";

            return construct.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }
    }

    internal static class AutomationTargetCatalog
    {
        private static readonly AutomationTargetCategory[] s_filterOrder =
        {
            AutomationTargetCategory.All,
            AutomationTargetCategory.Controllers,
            AutomationTargetCategory.AcbActions,
            AutomationTargetCategory.BreadboardReadable,
            AutomationTargetCategory.BreadboardWritable,
            AutomationTargetCategory.Movement,
            AutomationTargetCategory.Propulsion,
            AutomationTargetCategory.TurretsWeapons,
            AutomationTargetCategory.Ai,
            AutomationTargetCategory.Detection,
            AutomationTargetCategory.Missiles,
            AutomationTargetCategory.Subobjects,
            AutomationTargetCategory.Utility,
            AutomationTargetCategory.Media,
            AutomationTargetCategory.Spinblocks,
            AutomationTargetCategory.Pistons,
            AutomationTargetCategory.Pumps,
            AutomationTargetCategory.ControlSurfaces,
            AutomationTargetCategory.Lights,
            AutomationTargetCategory.ShieldsDefence,
            AutomationTargetCategory.DoorsDocking,
            AutomationTargetCategory.SoundDisplay,
            AutomationTargetCategory.ResourcePower,
            AutomationTargetCategory.Other
        };

        internal static IReadOnlyList<AutomationTargetCategory> FilterOrder => s_filterOrder;

        internal static IReadOnlyList<AutomationTarget> Capture(cBuild build)
        {
            var results = new List<AutomationTarget>(512);
            MainConstruct main = build?.GetCC();
            if (main == null)
                return results;

            var constructs = new List<AllConstruct>();
            try
            {
                main.AllBasicsRestricted.GetAllConstructsBelowUsAndIncludingUs(constructs);
            }
            catch
            {
                AllConstruct current = build.GetC();
                if (current != null)
                    constructs.Add(current);
            }

            for (int constructIndex = 0; constructIndex < constructs.Count; constructIndex++)
                AddConstructTargets(constructs[constructIndex], results);

            return results
                .GroupBy(target => target.StableKey)
                .Select(group => group.First())
                .OrderBy(target => CategoryLabel(target.Category), StringComparer.OrdinalIgnoreCase)
                .ThenBy(target => target.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static bool TryTargetFromHit(
            DecorationEditMode.DecorationPointerHit hit,
            out AutomationTarget target)
        {
            target = null;
            if (hit?.Construct == null)
                return false;

            Block block = null;
            try
            {
                block = hit.Construct.AllBasics?.GetBlockViaLocalPosition(hit.Anchor);
            }
            catch
            {
                return false;
            }

            if (!IsUsableBlock(block))
                return false;

            target = CreateTarget(hit.Construct, block);
            return target != null;
        }

        internal static bool PassesFilter(
            AutomationTarget target,
            AutomationTargetCategory filter)
        {
            if (target == null)
                return false;
            if (filter == AutomationTargetCategory.All)
                return true;
            if (filter == AutomationTargetCategory.Controllers)
                return target.IsController;
            if (target.Category == filter)
                return true;

            switch (filter)
            {
                case AutomationTargetCategory.AcbActions:
                    return IsAcbActionTarget(target);
                case AutomationTargetCategory.BreadboardReadable:
                    return IsBreadboardReadableTarget(target);
                case AutomationTargetCategory.BreadboardWritable:
                    return IsBreadboardWritableTarget(target);
                case AutomationTargetCategory.Movement:
                    return IsMovementTarget(target);
                case AutomationTargetCategory.Subobjects:
                    return IsSubobjectTarget(target);
                case AutomationTargetCategory.Utility:
                    return IsUtilityTarget(target);
                case AutomationTargetCategory.Media:
                    return target.Category == AutomationTargetCategory.Lights ||
                           target.Category == AutomationTargetCategory.SoundDisplay;
                default:
                    return false;
            }
        }

        internal static bool MatchesSearch(
            AutomationTarget target,
            string searchText)
        {
            if (target == null)
                return false;
            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            string[] terms = searchText
                .Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
                return true;

            string haystack = SearchTextFor(target);
            for (int index = 0; index < terms.Length; index++)
            {
                if (haystack.IndexOf(terms[index], StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }

        internal static string SearchTextFor(AutomationTarget target)
        {
            if (target == null)
                return string.Empty;

            AutomationControllerDescriptor controller = target.Controller;
            string controllerText = controller == null
                ? string.Empty
                : controller.Label + " " +
                  controller.ShortLabel + " " +
                  controller.ClassName + " " +
                  controller.Kind + " " +
                  controller.ItemGuid.ToString("D");
            string blockType = target.Block?.GetType().FullName ?? target.RuntimeType;
            string roleText = RoleSearchTextFor(target);
            return
                target.Label + " " +
                target.RuntimeType + " " +
                blockType + " " +
                CategoryLabel(target.Category) + " " +
                target.Category + " " +
                roleText + " " +
                target.LocalPosition.x.ToString(CultureInfo.InvariantCulture) + " " +
                target.LocalPosition.y.ToString(CultureInfo.InvariantCulture) + " " +
                target.LocalPosition.z.ToString(CultureInfo.InvariantCulture) + " " +
                target.StableKey + " " +
                controllerText;
        }

        internal static string PersistenceKeyFor(AutomationTarget target)
        {
            if (target == null)
                return string.Empty;

            string role = target.Controller == null
                ? target.Category.ToString()
                : target.Controller.Kind.ToString();
            string blockName = AutomationControllerCatalog.VanillaBlockNameFilter(target.Block);
            return
                target.LocalPosition.x.ToString(CultureInfo.InvariantCulture) + "," +
                target.LocalPosition.y.ToString(CultureInfo.InvariantCulture) + "," +
                target.LocalPosition.z.ToString(CultureInfo.InvariantCulture) + "|" +
                (target.RuntimeType ?? string.Empty) + "|" +
                role + "|" +
                (blockName ?? string.Empty);
        }

        internal static string RoleLabel(AutomationTarget target)
        {
            if (target == null)
                return string.Empty;

            var roles = new List<string>(4);
            if (IsAcbActionTarget(target))
                roles.Add("ACB action");
            if (IsBreadboardReadableTarget(target))
                roles.Add("Breadboard read");
            if (IsBreadboardWritableTarget(target))
                roles.Add("Breadboard write");
            if (IsSubobjectTarget(target))
                roles.Add("Subobject");

            return roles.Count == 0
                ? "Generic target"
                : string.Join(", ", roles.ToArray());
        }

        internal static string RoleSummary(AutomationTarget target)
        {
            if (target == null)
                return string.Empty;

            var roles = new List<string>(4);
            if (IsAcbActionTarget(target))
                roles.Add("ACB");
            if (IsBreadboardReadableTarget(target))
                roles.Add("R");
            if (IsBreadboardWritableTarget(target))
                roles.Add("W");
            if (IsSubobjectTarget(target))
                roles.Add("Sub");

            return string.Join("/", roles.ToArray());
        }

        internal static string CategoryLabel(AutomationTargetCategory category)
        {
            switch (category)
            {
                case AutomationTargetCategory.Controllers:
                    return "Controllers";
                case AutomationTargetCategory.AcbActions:
                    return "ACB Actions";
                case AutomationTargetCategory.BreadboardReadable:
                    return "Breadboard Read";
                case AutomationTargetCategory.BreadboardWritable:
                    return "Breadboard Write";
                case AutomationTargetCategory.Movement:
                    return "Movement";
                case AutomationTargetCategory.Subobjects:
                    return "Subobjects";
                case AutomationTargetCategory.Utility:
                    return "Utility";
                case AutomationTargetCategory.Media:
                    return "Media";
                case AutomationTargetCategory.Spinblocks:
                    return "Spinblocks";
                case AutomationTargetCategory.TurretsWeapons:
                    return "Turrets/Weapons";
                case AutomationTargetCategory.Propulsion:
                    return "Propulsion";
                case AutomationTargetCategory.Pistons:
                    return "Pistons";
                case AutomationTargetCategory.Pumps:
                    return "Pumps";
                case AutomationTargetCategory.ControlSurfaces:
                    return "Control Surfaces";
                case AutomationTargetCategory.Ai:
                    return "AI";
                case AutomationTargetCategory.Missiles:
                    return "Missiles";
                case AutomationTargetCategory.Lights:
                    return "Lights";
                case AutomationTargetCategory.ShieldsDefence:
                    return "Shields/Defence";
                case AutomationTargetCategory.Detection:
                    return "Detection";
                case AutomationTargetCategory.DoorsDocking:
                    return "Doors/Docking";
                case AutomationTargetCategory.SoundDisplay:
                    return "Sound/Display";
                case AutomationTargetCategory.ResourcePower:
                    return "Resource/Power";
                case AutomationTargetCategory.Other:
                    return "Other";
                default:
                    return "All";
            }
        }

        private static void AddConstructTargets(
            AllConstruct construct,
            ICollection<AutomationTarget> results)
        {
            if (construct?.AllBasics?.AliveAndDead == null)
                return;

            try
            {
                int count = construct.AllBasics.AliveAndDead.Count;
                for (int index = 0; index < count; index++)
                {
                    Block block = construct.AllBasics.AliveAndDead[index];
                    AutomationTarget target = CreateTarget(construct, block);
                    if (target != null)
                        results.Add(target);
                }
            }
            catch
            {
                // Target refresh is best-effort; pointer picking remains available.
            }
        }

        private static AutomationTarget CreateTarget(AllConstruct construct, Block block)
        {
            if (!IsUsableBlock(block))
                return null;

            AutomationControllerDescriptor controller = null;
            AutomationTargetCategory category;
            if (AutomationControllerCatalog.TryClassify(block, out controller))
                category = AutomationTargetCategory.Controllers;
            else
                category = Classify(block);

            string label = AutomationControllerCatalog.BlockDisplayName(block);
            return new AutomationTarget(
                construct,
                block,
                block.LocalPosition,
                category,
                label,
                controller);
        }

        private static bool IsUsableBlock(Block block)
        {
            try
            {
                return block != null &&
                       !block.IsDeleted &&
                       block.IsAlive &&
                       block.OnPlayerTeam;
            }
            catch
            {
                return false;
            }
        }

        private static AutomationTargetCategory Classify(Block block)
        {
            string text = AutomationControllerCatalog.RuntimeTypeText(block)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToLowerInvariant();

            if (ContainsAny(text, "spinblock", "dediblade", "turretbase"))
                return AutomationTargetCategory.Spinblocks;
            if (ContainsAny(text, "turret", "weapon", "cannon", "laser", "missilelauncher", "cram", "aps", "particlecannon", "lams"))
                return AutomationTargetCategory.TurretsWeapons;
            if (ContainsAny(text, "propulsion", "propeller", "thruster", "jet", "helium", "sail", "steamvalve"))
                return AutomationTargetCategory.Propulsion;
            if (ContainsAny(text, "piston"))
                return AutomationTargetCategory.Pistons;
            if (ContainsAny(text, "pump", "airpump"))
                return AutomationTargetCategory.Pumps;
            if (ContainsAny(text, "controlsurface", "hydrofoil", "aileron", "rudder"))
                return AutomationTargetCategory.ControlSurfaces;
            if (ContainsAny(text, "mainframe", "aicard", "behaviour", "maneuver", "manoeuvre", "aimpoint"))
                return AutomationTargetCategory.Ai;
            if (ContainsAny(text, "missile", "torpedo"))
                return AutomationTargetCategory.Missiles;
            if (ContainsAny(text, "light", "spotlight", "lamp"))
                return AutomationTargetCategory.Lights;
            if (ContainsAny(text, "shield", "smoke", "chaff", "decoy", "scrambler", "detector", "interceptor"))
                return AutomationTargetCategory.ShieldsDefence;
            if (ContainsAny(text, "radar", "sonar", "camera", "tracker", "sensor", "detector", "wireless"))
                return AutomationTargetCategory.Detection;
            if (ContainsAny(text, "door", "docking", "dock", "clamp", "separator", "seperator", "tractor"))
                return AutomationTargetCategory.DoorsDocking;
            if (ContainsAny(text, "sound", "hologram", "video", "display"))
                return AutomationTargetCategory.SoundDisplay;
            if (ContainsAny(text, "engine", "boiler", "battery", "electric", "fuel", "refinery", "ammo", "material", "resource"))
                return AutomationTargetCategory.ResourcePower;

            return AutomationTargetCategory.Other;
        }

        internal static bool IsAcbActionTarget(AutomationTarget target)
        {
            if (target == null)
                return false;

            if (target.Controller?.Kind == AutomationControllerKind.Acb)
                return true;

            switch (target.Category)
            {
                case AutomationTargetCategory.Controllers:
                case AutomationTargetCategory.Spinblocks:
                case AutomationTargetCategory.TurretsWeapons:
                case AutomationTargetCategory.Propulsion:
                case AutomationTargetCategory.Pistons:
                case AutomationTargetCategory.Pumps:
                case AutomationTargetCategory.ControlSurfaces:
                case AutomationTargetCategory.Ai:
                case AutomationTargetCategory.Missiles:
                case AutomationTargetCategory.Lights:
                case AutomationTargetCategory.ShieldsDefence:
                case AutomationTargetCategory.Detection:
                case AutomationTargetCategory.DoorsDocking:
                case AutomationTargetCategory.SoundDisplay:
                case AutomationTargetCategory.ResourcePower:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsBreadboardReadableTarget(AutomationTarget target) =>
            target?.Block != null;

        internal static bool IsBreadboardWritableTarget(AutomationTarget target)
        {
            if (target?.Block == null)
                return false;

            if (target.Controller?.Kind == AutomationControllerKind.Acb ||
                target.Controller?.Kind == AutomationControllerKind.AcbController)
            {
                return true;
            }

            switch (target.Category)
            {
                case AutomationTargetCategory.Spinblocks:
                case AutomationTargetCategory.TurretsWeapons:
                case AutomationTargetCategory.Propulsion:
                case AutomationTargetCategory.Pistons:
                case AutomationTargetCategory.Pumps:
                case AutomationTargetCategory.ControlSurfaces:
                case AutomationTargetCategory.Ai:
                case AutomationTargetCategory.Missiles:
                case AutomationTargetCategory.Lights:
                case AutomationTargetCategory.ShieldsDefence:
                case AutomationTargetCategory.Detection:
                case AutomationTargetCategory.DoorsDocking:
                case AutomationTargetCategory.SoundDisplay:
                case AutomationTargetCategory.ResourcePower:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsMovementTarget(AutomationTarget target) =>
            target != null &&
            (target.Category == AutomationTargetCategory.Spinblocks ||
             target.Category == AutomationTargetCategory.Propulsion ||
             target.Category == AutomationTargetCategory.Pistons ||
             target.Category == AutomationTargetCategory.Pumps ||
             target.Category == AutomationTargetCategory.ControlSurfaces);

        private static bool IsSubobjectTarget(AutomationTarget target) =>
            target != null &&
            (target.Category == AutomationTargetCategory.Spinblocks ||
             target.Category == AutomationTargetCategory.Pistons ||
             target.Category == AutomationTargetCategory.TurretsWeapons ||
             target.Category == AutomationTargetCategory.DoorsDocking);

        private static bool IsUtilityTarget(AutomationTarget target) =>
            target != null &&
            (target.IsController ||
             target.Category == AutomationTargetCategory.Pumps ||
             target.Category == AutomationTargetCategory.ShieldsDefence ||
             target.Category == AutomationTargetCategory.Detection ||
             target.Category == AutomationTargetCategory.ResourcePower ||
             target.Category == AutomationTargetCategory.Other);

        private static string RoleSearchTextFor(AutomationTarget target)
        {
            if (target == null)
                return string.Empty;

            var terms = new List<string>(16);
            if (IsAcbActionTarget(target))
                terms.Add("acb action affect condition");
            if (IsBreadboardReadableTarget(target))
                terms.Add("breadboard readable read getter generic getter");
            if (IsBreadboardWritableTarget(target))
                terms.Add("breadboard writable write setter generic setter");
            if (IsMovementTarget(target))
                terms.Add("movement motion drive control");
            if (IsSubobjectTarget(target))
                terms.Add("subobject subconstruct spinblock piston turret");
            if (IsUtilityTarget(target))
                terms.Add("utility support control resource");
            if (target.Category == AutomationTargetCategory.Lights ||
                target.Category == AutomationTargetCategory.SoundDisplay)
            {
                terms.Add("media visual sound display");
            }

            return string.Join(" ", terms.ToArray());
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            if (string.IsNullOrEmpty(text) || needles == null)
                return false;

            for (int index = 0; index < needles.Length; index++)
            {
                if (!string.IsNullOrEmpty(needles[index]) &&
                    text.IndexOf(needles[index], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
