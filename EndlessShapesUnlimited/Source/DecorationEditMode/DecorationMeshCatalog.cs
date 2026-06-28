using System;
using System.Collections.Generic;
using System.Linq;
using BrilliantSkies.Blocks.Decorative;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Containers;
using BrilliantSkies.Modding.Types;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationMeshCatalogEntry
    {
        internal DecorationMeshCatalogEntry(ModComponentAbstract component, string kind)
        {
            Component = component ?? throw new ArgumentNullException(nameof(component));
            Kind = kind ?? string.Empty;
            Name = component.ComponentId.Name ?? string.Empty;
            Guid = component.ComponentId.Guid;
            SearchText = (Name + " " + Guid + " " + component.ComponentId.RuntimeId)
                .ToLowerInvariant();
        }

        internal ModComponentAbstract Component { get; }

        internal string Kind { get; }

        internal string Name { get; }

        internal Guid Guid { get; }

        internal string SearchText { get; }

        internal bool TryGetMesh(out MeshDefinition mesh)
        {
            try
            {
                return MimicHelp.TryGetMesh(Component, out mesh) &&
                       mesh?.SafeMesh != null;
            }
            catch
            {
                mesh = null;
                return false;
            }
        }
    }

    internal static class DecorationMeshCatalog
    {
        internal static List<DecorationMeshCatalogEntry> Build()
        {
            var entries = new List<DecorationMeshCatalogEntry>();
            try
            {
                entries.AddRange(Configured.i
                    .Get<ModificationComponentContainerItem>()
                    .Components
                    .Where(component => component != null && component.MeshReference.IsValidReference)
                    .Select(component => new DecorationMeshCatalogEntry(component, "item")));
            }
            catch
            {
                // Missing containers should not stop the editor opening.
            }

            try
            {
                entries.AddRange(Configured.i
                    .Get<BrilliantSkies.Modding.Containers.Objects>()
                    .Components
                    .Where(component => component != null && component.MeshReference.IsValidReference)
                    .Select(component => new DecorationMeshCatalogEntry(component, "object")));
            }
            catch
            {
                // Missing containers should not stop the editor opening.
            }

            return entries
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Guid)
                .ToList();
        }
    }
}
