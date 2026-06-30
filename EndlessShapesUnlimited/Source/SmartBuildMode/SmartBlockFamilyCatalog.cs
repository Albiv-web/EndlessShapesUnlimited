using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Containers;
using BrilliantSkies.Modding.Types;

namespace DecoLimitLifter.SmartBuildMode
{
    internal enum SmartBuildMaterial
    {
        Wood,
        Stone,
        Metal,
        Alloy,
        Glass,
        Lead,
        HeavyArmour,
        Rubber
    }

    internal sealed class SmartBuildSource
    {
        internal SmartBuildSource(
            ItemDefinition item,
            Guid itemGuid,
            string displayName,
            Vector3i dimensions,
            SmartBlockFamily family,
            SmartBlockFamily downSlopeFamily = null)
        {
            Item = item;
            ItemGuid = itemGuid;
            DisplayName = displayName;
            Dimensions = dimensions;
            Family = family;
            DownSlopeFamily = downSlopeFamily ??
                              SmartBlockFamily.Unsupported(
                                  displayName + " down slopes",
                                  displayName + " down slopes are unavailable in this FtD install.");
        }

        internal ItemDefinition Item { get; }

        internal Guid ItemGuid { get; }

        internal string DisplayName { get; }

        internal Vector3i Dimensions { get; }

        internal SmartBlockFamily Family { get; }

        internal SmartBlockFamily DownSlopeFamily { get; }

        internal bool HasDownSlopeLength(IEnumerable<int> lengths)
        {
            if (DownSlopeFamily == null || !DownSlopeFamily.IsSupported)
                return false;

            var available = new HashSet<int>(
                DownSlopeFamily.Candidates.Select(candidate => candidate.Length));
            foreach (int length in lengths ?? Array.Empty<int>())
            {
                if (!available.Contains(length))
                    return false;
            }

            return true;
        }
    }

    internal static class SmartBlockFamilyCatalog
    {
        private static readonly SmartBuildMaterial[] BasicMaterialOrder =
        {
            SmartBuildMaterial.Wood,
            SmartBuildMaterial.Stone,
            SmartBuildMaterial.Metal,
            SmartBuildMaterial.Alloy,
            SmartBuildMaterial.Glass,
            SmartBuildMaterial.Lead,
            SmartBuildMaterial.HeavyArmour,
            SmartBuildMaterial.Rubber
        };

        private static readonly Guid[] AlloyFamily = Family(
            "3cc75979-18ac-46c4-9a5b-25b327d99410",
            "8f9dbf41-6c2d-4e7b-855d-b2432c6942a2",
            "649f2aec-6f59-4157-ac01-0122ce2e6dad",
            "9411e401-27da-4546-b805-3334f200f055");

        private static readonly Guid[] HeavyArmourFamily = Family(
            "0c03433e-8947-4e7d-9dec-793526fe06d1",
            "242e07fa-399f-4caa-bfc2-1b77bd2bd538",
            "49714981-369a-4158-aff6-e562ee5f98d5",
            "867cea4e-6ea4-4fe2-a4a1-b6230308f8f1");

        private static readonly Guid[] LeadFamily = Family(
            "e71e6f97-fbe8-4bf5-9645-d15179ba0c17",
            "d5e50322-fbc0-4e09-bfab-050f431146a9",
            "19ee2ba3-9443-4a44-97fd-bad9b1443895",
            "f5d2db25-114e-473a-8313-96831ccd011e");

        private static readonly Guid[] MetalFamily = Family(
            "ab699540-efc8-4592-bc97-204f6a874b3a",
            "2a22f176-01c2-42f2-a7d2-2c7054504aa9",
            "46f54639-5f91-4731-93eb-e5c0a7460538",
            "a7f5d8de-4882-4111-9d01-436493e5b2d8");

        private static readonly Guid[] ReinforcedWoodFamily = Family(
            "2f7f61ae-79f1-4139-a790-3f2c26bda4e4",
            "d92c5b73-d0fd-423e-98fc-76b1cd91b524",
            "50bdd099-dd8d-43f8-b43d-dd14c60be096",
            "6e2afb0f-97b6-4017-b14c-158146da6854");

        private static readonly Guid[] StoneFamily = Family(
            "710ee212-563b-42f8-acd1-57515479524d",
            "6cd6c6bd-da8b-483f-ace2-fa427a07d91a",
            "d47815a1-9052-4885-8d17-8c9cb3eab72b",
            "c7a19161-b361-4074-8c51-2398a0a70d1b");

        private static readonly Guid[] WoodFamily = Family(
            "9a0ae372-beb4-4009-b14e-36ed0715af73",
            "de36c624-8c78-4b52-8d86-431cec16a306",
            "39553630-8281-40e4-96fb-b01c1f3537e6",
            "05475442-0e52-4e0b-9fbb-2715f0e54f97");

        private static readonly Guid[] GlassFamily = Family(
            "2d519ca8-1f12-4a8e-9340-aa6648b5e799");

        private static readonly Guid[] RubberFamily = Family(
            "6c0bab88-aa88-4825-9cf5-55df36aa12b8");

        private static readonly Guid[] WoodDownSlopeFamily = Family(
            "bdafa446-f615-49cb-94f3-d7652dde6cec",
            "b88679fb-0325-4c85-942f-ad9c6ed6545b",
            "caec26b3-847c-4876-80e1-e6206003ecb5",
            "3296c67d-6ace-44dd-8e86-335b9a90ad80");

        private static readonly Guid[] StoneDownSlopeFamily = Family(
            "11fcac17-e3b9-47d5-aeb8-2224d86b2f1d",
            "66aa8853-094a-41ef-aa96-a2d658b21305",
            "9e204cce-876c-4d9d-af0e-65ec39cf1ba4",
            "cf8b2e90-abe7-4a4f-9596-253364004394");

        private static readonly Guid[] MetalDownSlopeFamily = Family(
            "5548037e-8428-43f8-bcb6-d730dbcd0a79",
            "8477bbec-974c-45bf-a1ce-49a48d5b5307",
            "a09be1c6-93fd-4b54-b9ca-62e60efbc818",
            "db9ed060-d556-435b-945c-19c923e233d3");

        private static readonly Guid[] AlloyDownSlopeFamily = Family(
            "911fe222-f9b2-4892-9cd6-8b154d55b2aa",
            "c6176cb5-0a32-4d68-a749-8ee33b2230c1",
            "a3ea61a8-018c-4277-afd9-ac0a34faa759",
            "2a3905ff-2030-421d-a2bf-90fba71c1c5e");

        private static readonly Guid[] GlassDownSlopeFamily = Family(
            "174b5b41-b70e-485d-b00a-a61cc9826b2c",
            "69feebc3-e241-4dff-a000-736def80c851",
            "b148f5b4-e237-41a0-a1a3-30c455321149",
            "c4ef6072-822a-4fa5-b618-e7fc0508c499");

        private static readonly Guid[] LeadDownSlopeFamily = Family(
            "df61d4c4-a514-4f23-baab-4da8fce066a3",
            "354bc2b0-de06-4948-ad1e-2fbcef516cb7",
            "7579ca39-af82-4a2c-83f6-a2090ae50d6b",
            "97ea8dab-dc9b-4056-9532-78533e3b82bf");

        private static readonly Guid[] HeavyArmourDownSlopeFamily = Family(
            "78b81c0a-44df-4c24-b2a5-5d273737da60",
            "525d85fc-f4d4-49ea-bebd-dc51bc562adf",
            "98467918-ec0c-47e1-8ce6-55949326eb4f",
            "983ebe9d-535e-4bdb-a37f-6b681a96f5a3");

        private static readonly Guid[] RubberDownSlopeFamily = Family(
            "552d8144-11c0-46e6-8607-927f825b18be",
            "d204f301-7ea9-4bcf-b9e8-34ed899de190",
            "d62bca2a-ffe5-4316-8c43-269e5393db6e",
            "2a31679e-2d6e-4598-95e2-4016ed834e9c");

        private static readonly Guid[][] ArmorFamilies =
        {
            AlloyFamily,
            HeavyArmourFamily,
            LeadFamily,
            MetalFamily,
            ReinforcedWoodFamily,
            StoneFamily,
            WoodFamily,
            GlassFamily,
            RubberFamily
        };

        internal static IReadOnlyList<SmartBuildMaterial> BasicMaterials => BasicMaterialOrder;

        internal static bool TryCreateMaterialSource(
            SmartBuildMaterial material,
            out SmartBuildSource source,
            out string reason)
        {
            source = null;
            string displayName = MaterialDisplayName(material);
            SmartBlockFamily family = FromMaterial(material);
            if (!family.IsSupported)
            {
                reason = family.UnsupportedReason ??
                         displayName + " is unavailable in this FtD install.";
                return false;
            }

            SmartBlockCandidate seed = family.Candidates
                .OrderBy(candidate => candidate.Length)
                .FirstOrDefault();
            if (seed?.Definition == null)
            {
                reason = displayName + " is unavailable in this FtD install.";
                return false;
            }

            Guid itemGuid = Guid.Empty;
            try
            {
                itemGuid = seed.Definition.ComponentId.Guid;
            }
            catch
            {
                // The source remains valid for planning/commit through the resolved definition.
            }

            source = new SmartBuildSource(
                seed.Definition,
                itemGuid,
                displayName,
                new Vector3i(1, 1, 1),
                family,
                DownSlopeFromMaterial(material));
            reason = null;
            return true;
        }

        internal static SmartBlockFamily FromMaterial(SmartBuildMaterial material)
        {
            string displayName = MaterialDisplayName(material);
            Guid[] family = FamilyForMaterial(material);
            List<SmartBlockCandidate> candidates = ResolveFamilyCandidates(family);
            if (candidates.Count > 0)
                return new SmartBlockFamily(displayName, candidates);

            return SmartBlockFamily.Unsupported(
                displayName,
                displayName + " is unavailable in this FtD install.");
        }

        internal static SmartBlockFamily DownSlopeFromMaterial(SmartBuildMaterial material)
        {
            string displayName = MaterialDisplayName(material) + " down slopes";
            var candidates = DiscoverDownSlopeCandidates(material);
            var lengths = new HashSet<int>(candidates.Select(candidate => candidate.Length));
            foreach (SmartBlockCandidate fallback in ResolveFamilyCandidates(
                         DownSlopeFamilyForMaterial(material),
                         SmartBuildShapeKind.DownSlope))
            {
                if (lengths.Add(fallback.Length))
                    candidates.Add(fallback);
            }

            if (candidates.Count > 0)
                return new SmartBlockFamily(displayName, candidates);

            return SmartBlockFamily.Unsupported(
                displayName,
                displayName + " are unavailable in this FtD install.");
        }

        internal static string MaterialDisplayName(SmartBuildMaterial material)
        {
            switch (material)
            {
                case SmartBuildMaterial.Stone:
                    return "Stone";
                case SmartBuildMaterial.Metal:
                    return "Metal";
                case SmartBuildMaterial.Alloy:
                    return "Alloy";
                case SmartBuildMaterial.Glass:
                    return "Glass";
                case SmartBuildMaterial.Lead:
                    return "Lead";
                case SmartBuildMaterial.HeavyArmour:
                    return "Heavy armour";
                case SmartBuildMaterial.Rubber:
                    return "Rubber";
                default:
                    return "Wood";
            }
        }

        internal static SmartBlockFamily FromSelected(
            ItemDefinition selected,
            Guid selectedGuid,
            string displayName,
            Vector3i dimensions)
        {
            if (selected == null)
                return SmartBlockFamily.Unsupported(displayName, "No selected build item is available.");

            Guid[] family = FindArmorFamily(selectedGuid);
            if (family != null)
            {
                List<SmartBlockCandidate> candidates = ResolveFamilyCandidates(family);
                if (candidates.Count > 0)
                    return new SmartBlockFamily(displayName, candidates);
            }

            if (dimensions.x == 1 && dimensions.y == 1 && dimensions.z == 1)
            {
                return new SmartBlockFamily(
                    displayName,
                    new[]
                    {
                        new SmartBlockCandidate(displayName, 1, selected)
                    });
            }

            return SmartBlockFamily.Unsupported(
                displayName,
                "V1 only supports 1x1x1 seeds and known armor beam families.");
        }

        private static Guid[] FindArmorFamily(Guid guid)
        {
            for (int familyIndex = 0; familyIndex < ArmorFamilies.Length; familyIndex++)
            {
                Guid[] family = ArmorFamilies[familyIndex];
                for (int index = 0; index < family.Length; index++)
                {
                    if (family[index] == guid)
                        return family;
                }
            }

            return null;
        }

        private static Guid[] FamilyForMaterial(SmartBuildMaterial material)
        {
            switch (material)
            {
                case SmartBuildMaterial.Stone:
                    return StoneFamily;
                case SmartBuildMaterial.Metal:
                    return MetalFamily;
                case SmartBuildMaterial.Alloy:
                    return AlloyFamily;
                case SmartBuildMaterial.Glass:
                    return GlassFamily;
                case SmartBuildMaterial.Lead:
                    return LeadFamily;
                case SmartBuildMaterial.HeavyArmour:
                    return HeavyArmourFamily;
                case SmartBuildMaterial.Rubber:
                    return RubberFamily;
                default:
                    return WoodFamily;
            }
        }

        private static Guid[] DownSlopeFamilyForMaterial(SmartBuildMaterial material)
        {
            switch (material)
            {
                case SmartBuildMaterial.Stone:
                    return StoneDownSlopeFamily;
                case SmartBuildMaterial.Metal:
                    return MetalDownSlopeFamily;
                case SmartBuildMaterial.Alloy:
                    return AlloyDownSlopeFamily;
                case SmartBuildMaterial.Glass:
                    return GlassDownSlopeFamily;
                case SmartBuildMaterial.Lead:
                    return LeadDownSlopeFamily;
                case SmartBuildMaterial.HeavyArmour:
                    return HeavyArmourDownSlopeFamily;
                case SmartBuildMaterial.Rubber:
                    return RubberDownSlopeFamily;
                default:
                    return WoodDownSlopeFamily;
            }
        }

        private static List<SmartBlockCandidate> ResolveFamilyCandidates(Guid[] family)
        {
            return ResolveFamilyCandidates(family, SmartBuildShapeKind.Cuboid);
        }

        private static List<SmartBlockCandidate> ResolveFamilyCandidates(
            Guid[] family,
            SmartBuildShapeKind shapeKind)
        {
            var candidates = new List<SmartBlockCandidate>();
            if (family == null)
                return candidates;

            for (int index = 0; index < family.Length; index++)
            {
                ItemDefinition definition = ResolveItemDefinition(family[index]);
                if (definition == null)
                    continue;

                object geometry = GeometryFor(definition);
                candidates.Add(new SmartBlockCandidate(
                    ItemName(definition),
                    index + 1,
                    definition,
                    shapeKind,
                    geometry));
            }

            return candidates;
        }

        private static List<SmartBlockCandidate> DiscoverDownSlopeCandidates(SmartBuildMaterial material)
        {
            var candidates = new List<SmartBlockCandidate>();
            foreach (ItemDefinition item in LoadedItemDefinitions())
            {
                object geometry = GeometryFor(item);
                string geometryName = geometry?.ToString();
                if (!TryLengthFromDownSlopeGeometry(geometryName, out int length))
                    continue;
                if (!LooksLikeMaterial(item, material))
                    continue;

                candidates.Add(new SmartBlockCandidate(
                    ItemName(item),
                    length,
                    item,
                    SmartBuildShapeKind.DownSlope,
                    geometry));
            }

            return candidates
                .GroupBy(candidate => candidate.Length)
                .Select(group => group.First())
                .ToList();
        }

        private static IEnumerable<ItemDefinition> LoadedItemDefinitions()
        {
            try
            {
                ModificationComponentContainerItem container = Configured.i
                    .Get<ModificationComponentContainerItem>();
                FieldInfo field = typeof(ModificationComponentContainerItem)
                    .GetField(
                        "m_AllCorrespondingItems",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(container) is IEnumerable<ItemDefinition> items)
                    return items.Where(item => item != null).ToArray();
            }
            catch
            {
                // GUID fallbacks below keep Smart Builder usable when reflection changes.
            }

            return Array.Empty<ItemDefinition>();
        }

        private static object GeometryFor(ItemDefinition item)
        {
            try
            {
                return item?.DragSettings?.Geometry;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryLengthFromDownSlopeGeometry(string geometryName, out int length)
        {
            length = 0;
            if (string.IsNullOrWhiteSpace(geometryName) ||
                !geometryName.StartsWith("DownSlope", StringComparison.OrdinalIgnoreCase) ||
                !geometryName.EndsWith("m", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string number = geometryName
                .Substring("DownSlope".Length)
                .TrimEnd('m', 'M');
            return int.TryParse(number, out length) &&
                   length >= 1 &&
                   length <= 4;
        }

        private static bool LooksLikeMaterial(ItemDefinition item, SmartBuildMaterial material)
        {
            string text = (ItemName(item) + " " + ComponentText(item))
                .Replace("_", " ")
                .Replace("-", " ");
            string lowered = text.ToLowerInvariant();
            switch (material)
            {
                case SmartBuildMaterial.HeavyArmour:
                    return lowered.Contains("heavy") || lowered.Contains("ha ");
                case SmartBuildMaterial.Alloy:
                    return lowered.Contains("alloy");
                case SmartBuildMaterial.Glass:
                    return lowered.Contains("glass");
                case SmartBuildMaterial.Lead:
                    return lowered.Contains("lead");
                case SmartBuildMaterial.Metal:
                    return lowered.Contains("metal");
                case SmartBuildMaterial.Rubber:
                    return lowered.Contains("rubber");
                case SmartBuildMaterial.Stone:
                    return lowered.Contains("stone");
                default:
                    return lowered.Contains("wood");
            }
        }

        private static string ComponentText(ItemDefinition item)
        {
            try
            {
                object componentId = item?.ComponentId;
                if (componentId == null)
                    return string.Empty;

                string reflectedName = componentId
                    .GetType()
                    .GetProperty(
                        "Name",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(componentId, null) as string;
                if (!string.IsNullOrWhiteSpace(reflectedName))
                    return reflectedName;

                return item.ComponentId.Guid.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static ItemDefinition ResolveItemDefinition(Guid guid)
        {
            try
            {
                ItemDefinition item = Configured.i
                    .Get<ModificationComponentContainerItem>()
                    .Find(guid, out bool found);
                return found ? item : null;
            }
            catch
            {
                return null;
            }
        }

        private static Guid[] Family(params string[] values)
        {
            var result = new Guid[values.Length];
            for (int index = 0; index < values.Length; index++)
                result[index] = new Guid(values[index]);
            return result;
        }

        internal static string ItemName(ItemDefinition item)
        {
            if (item == null)
                return "Selected block";

            try
            {
                string name = item.GetInventoryNameConsideringVariants();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch
            {
                // Fall through to the next name source.
            }

            try
            {
                string name = item.GetInventoryName();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch
            {
                // Fall through to the component id.
            }

            try
            {
                return item.ComponentId.Guid.ToString();
            }
            catch
            {
                return "Selected block";
            }
        }
    }
}
