using System;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Modding.Types;
using BrilliantSkies.Ui.Special.InfoStore;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed partial class DecorationEditSession
    {
        internal enum NativeBlockPaletteInputAction
        {
            None,
            DelegateVanillaPlacement,
            SelectPointedBlock
        }

        private bool _nativeBlockPaletteMode;
        private ItemDefinition _nativeBlockPaletteItem;
        private DecorationMeshCatalogEntry _nativeBlockPalettePreviousMesh;
        private DecorationEditorViewMode _nativeBlockPalettePreviousView;
        private bool _nativeBlockPaletteChangedView;
        private string _nativeBlockPalettePreviousKindFilter;
        private int _nativeBlockPaletteProbeFrame = -1;
        private Vector3 _nativeBlockPaletteProbeMouse;
        private Camera _nativeBlockPaletteProbeCamera;
        private Vector3 _nativeBlockPaletteProbeRayOrigin;
        private Vector3 _nativeBlockPaletteProbeRayDirection;
        private Vector3 _nativeBlockPaletteProbeConstructRayOrigin;
        private Vector3 _nativeBlockPaletteProbeConstructRayDirection;
        private AllConstruct _nativeBlockPaletteProbeConstruct;
        private DecorationPointerHit _nativeBlockPaletteProbeHit;
        private bool _nativeBlockPaletteProbeResolved;

        internal static NativeBlockPaletteInputAction ResolveNativeBlockPaletteWorldInput(
            bool buildMode,
            bool overUi,
            int mouseButton)
        {
            if (!buildMode || overUi)
                return NativeBlockPaletteInputAction.None;

            if (mouseButton == 0)
                return NativeBlockPaletteInputAction.DelegateVanillaPlacement;
            if (mouseButton == 1)
                return NativeBlockPaletteInputAction.SelectPointedBlock;
            return NativeBlockPaletteInputAction.None;
        }

        internal static bool IsNativeBlockPaletteEntry(DecorationMeshCatalogEntry entry)
        {
            if (!(entry?.Component is ItemDefinition definition))
                return false;

            try
            {
                return IsNativePlaceableBlockDefinition(definition) &&
                       definition.DisplayOnInventory &&
                       definition.InventoryTabOrVariantId.IsValidReference &&
                       definition.ComponentId != null &&
                       definition.ComponentId.Guid != Guid.Empty &&
                       definition.ComponentId.Guid == entry.Guid;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsNativePlaceableBlockDefinition(ItemDefinition definition)
        {
            if (definition == null)
                return false;

            try
            {
                return definition.ItemType == enumItemType.block &&
                       definition.GetInventoryObjectType() == InventoryObjectType.Item &&
                       definition.SizeInfo != null &&
                       definition.SizeInfo.ArrayPositionsUsed > 0;
            }
            catch
            {
                return false;
            }
        }

        private void ToggleNativeBlockPaletteMode() =>
            SetNativeBlockPaletteMode(!_nativeBlockPaletteMode);

        private void SetNativeBlockPaletteMode(bool enabled, bool notify = true)
        {
            InvalidateNativeBlockPaletteCursorProbe();
            if (_nativeBlockPaletteMode == enabled)
            {
                DecorationEditorInputScope.SetNativeBlockPaletteMode(enabled);
                DecorationEditorInputScope.SetNativeBlockPaletteCursorPreviewUpdater(
                    enabled ? PrepareNativeBlockPaletteCursorPreview : null);
                if (!enabled)
                    HideNativeBlockPaletteCursorPreview();
                return;
            }

            if (enabled)
            {
                if (DecoLimitLifter.EsuSymmetry.PendingAxis != DecorationEditAxis.None)
                    DecoLimitLifter.EsuSymmetry.CancelPending();
                if (_placingMesh != null)
                    CancelPlacement();
                CancelBoxSelection();
                CloseSurfacePointContextMenu();
                CloseDecorationContextMenu();
                _nativeBlockPalettePreviousMesh = _selectedMesh;
                _nativeBlockPalettePreviousKindFilter = _meshKindFilter;
                if (string.Equals(
                        _meshKindFilter,
                        "object",
                        StringComparison.OrdinalIgnoreCase))
                {
                    _meshKindFilter = "item";
                }
                _nativeBlockPalettePreviousView = _viewMode;
                _nativeBlockPaletteChangedView =
                    _viewMode == DecorationEditorViewMode.DecorationOnly;
                if (_nativeBlockPaletteChangedView)
                {
                    _viewMode = DecorationEditorViewMode.Mixed;
                    ApplyEditorViewMode();
                }
            }
            else
            {
                HideNativeBlockPaletteCursorPreview();
                _nativeBlockPaletteItem = null;
                _selectedMesh = _selected != null && !_selected.IsDeleted &&
                                _meshByGuid.TryGetValue(
                                    _selected.MeshGuid.Us,
                                    out DecorationMeshCatalogEntry selectedEntry)
                    ? selectedEntry
                    : _nativeBlockPalettePreviousMesh;
                if (_nativeBlockPaletteChangedView)
                {
                    _viewMode = _nativeBlockPalettePreviousView;
                    ApplyEditorViewMode();
                }
                _nativeBlockPalettePreviousMesh = null;
                if (!string.IsNullOrWhiteSpace(_nativeBlockPalettePreviousKindFilter))
                    _meshKindFilter = _nativeBlockPalettePreviousKindFilter;
                _nativeBlockPalettePreviousKindFilter = null;
                _nativeBlockPaletteChangedView = false;
            }

            _nativeBlockPaletteMode = enabled;
            try
            {
                _build.H2P = enumH2P.idle;
            }
            catch
            {
                // Native placement still remains click-driven if FtD changes this field.
            }
            DecorationEditorInputScope.SetNativeBlockPaletteMode(enabled);
            DecorationEditorInputScope.SetNativeBlockPaletteCursorPreviewUpdater(
                enabled ? PrepareNativeBlockPaletteCursorPreview : null);
            DecorationEditorInputScope.SetNativeBlockPalettePlacementArmed(false);
            DecorationEditorInputScope.ClaimBuildInputForFrames();
            DecorationEditorInputScope.ClaimCameraInputForFrames();
            if (notify)
            {
                InfoStore.Add(enabled
                    ? "Block Palette enabled. Choose an item, then use cursor-following native add placement; right-click copies a craft block. Simple-mode Shift replacement is disabled for safety."
                    : "Block Palette disabled. Mesh choices create decorations again.");
            }
        }

        private void ActivateMeshPaletteEntry(DecorationMeshCatalogEntry entry)
        {
            if (_nativeBlockPaletteMode)
            {
                TrySelectNativeBlockEntry(entry);
                return;
            }

            StartMeshPlacement(entry);
        }

        private bool TryConsumeNativeBlockPaletteSecondaryClick(Rect rect)
        {
            if (!_nativeBlockPaletteMode || !GUI.enabled)
                return false;

            Event current = Event.current;
            if (current == null ||
                !rect.Contains(current.mousePosition) ||
                !(current.type == EventType.ContextClick ||
                  (current.type == EventType.MouseDown && current.button == 1)))
            {
                return false;
            }

            DecorationEditorInputScope.ClaimBuildInputForFrames();
            DecorationEditorInputScope.ClaimCameraInputForFrames();
            try
            {
                _build.H2P = enumH2P.idle;
            }
            catch
            {
                // Dynamic UI ownership still blocks both native loops.
            }
            current.Use();
            return true;
        }

        private bool TrySelectNativeBlockEntry(DecorationMeshCatalogEntry entry)
        {
            if (!IsNativeBlockPaletteEntry(entry) ||
                !(entry.Component is ItemDefinition definition))
            {
                InfoStore.Add("Only native item blocks can be selected in Block Palette mode.");
                return false;
            }

            if (!IsNativeBlockPaletteEntryAvailable(entry))
            {
                InfoStore.Add("That native block is locked or unavailable on the focused construct.");
                return false;
            }

            if (!TrySetNativeBlockSelection(
                    definition,
                    null,
                    entry.Name,
                    requireInventoryAcceptance: true))
                return false;

            _selectedMesh = entry;
            PushRecentMesh(entry.Guid);
            return true;
        }

        private bool HandleNativeBlockPaletteSceneInput()
        {
            if (!_nativeBlockPaletteMode)
                return false;

            int mouseButton = Input.GetMouseButtonDown(1)
                ? 1
                : Input.GetMouseButtonDown(0)
                    ? 0
                    : -1;
            NativeBlockPaletteInputAction action = ResolveNativeBlockPaletteWorldInput(
                buildMode: true,
                overUi: false,
                mouseButton);
            if (action == NativeBlockPaletteInputAction.SelectPointedBlock)
            {
                DecorationEditorInputScope.ClaimCameraInputForFrames();
                DecorationEditorInputScope.ClaimBuildInputForFrames();
                try
                {
                    _build.H2P = enumH2P.idle;
                }
                catch
                {
                    // Raw RMB remains synchronously gated in both native loops.
                }
                TrySelectPointedNativeBlock();
            }

            // Left mouse is deliberately untouched. cBuild receives it and owns
            // native snapping, footprint, rotation, mirror, resources, undo, and RPCs.
            return true;
        }

        private bool TryBlurNativeBlockPaletteTextInputForWorldClick()
        {
            if (!_nativeBlockPaletteMode ||
                !_textInputFocused ||
                (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)))
            {
                return false;
            }

            GUIUtility.keyboardControl = 0;
            _textInputFocused = false;
            DecorationEditorInputScope.RequireNativeBlockPaletteMouseRelease();
            DecorationEditorInputScope.SetNativeBlockPaletteAuxiliaryInputOwnership(
                DecoLimitLifter.EsuInputState.IsTextInputActive(),
                _unappliedClosePromptOpen ||
                _gizmoSettingsOpen ||
                _viewModeMenuOpen ||
                _anchorMenuOpen ||
                ForegroundContextMenuOpen());
            DecorationEditorInputScope.ClaimBuildInputForFrames();
            DecorationEditorInputScope.ClaimCameraInputForFrames();
            try
            {
                _build.H2P = enumH2P.idle;
            }
            catch
            {
                // The input-scope release latch remains authoritative.
            }
            return true;
        }

        private bool TrySelectPointedNativeBlock()
        {
            if (!_pointerProbe.TryProbe(out DecorationPointerHit hit) ||
                hit?.Construct == null)
            {
                InfoStore.Add("Right-click a real craft block to copy its native block type.");
                return false;
            }

            Block block;
            try
            {
                block = hit.Construct.AllBasics?.GetBlockViaLocalPosition(hit.Anchor);
            }
            catch
            {
                block = null;
            }

            ItemDefinition definition = null;
            try
            {
                definition = block?.item;
            }
            catch
            {
                // Unusual modded blocks may not expose a usable item definition.
            }

            if (block == null ||
                block.IsDeleted ||
                !IsNativePlaceableBlockDefinition(definition))
            {
                InfoStore.Add("The pointed craft block has no selectable native item definition.");
                return false;
            }

            Quaternion? copiedRotation = null;
            try
            {
                copiedRotation = block.LocalRotation;
            }
            catch
            {
                // Keep the current native rotation if a modded block omits it.
            }
            string name = NativeBlockName(definition);
            if (!TrySetNativeBlockSelection(
                    definition,
                    copiedRotation,
                    name,
                    requireInventoryAcceptance: false))
                return false;

            Guid guid = NativeBlockGuid(definition);
            if (guid != Guid.Empty &&
                _meshByGuid.TryGetValue(guid, out DecorationMeshCatalogEntry entry) &&
                IsNativeBlockPaletteEntry(entry))
            {
                _selectedMesh = entry;
                PushRecentMesh(guid);
            }
            else
            {
                _selectedMesh = null;
            }

            InfoStore.Add(copiedRotation.HasValue
                ? "Selected native block " + name + " and copied its rotation."
                : "Selected native block " + name + "; FtD kept the current build rotation.");
            return true;
        }

        private bool TrySetNativeBlockSelection(
            ItemDefinition definition,
            Quaternion? copiedRotation,
            string displayName,
            bool requireInventoryAcceptance)
        {
            if (definition == null)
                return false;

            ItemDefinition previousItem = null;
            BuildingWithMode previousMode = BuildingWithMode.Item;
            SavedSubObject previousPrefab = null;
            SavedSubObject previousSubconstructable = null;
            Quaternion previousRotation = Quaternion.identity;
            try
            {
                previousItem = _build.BuildingWith?.Item;
                previousMode = _build.BuildingWith?.Mode ?? BuildingWithMode.Item;
                previousPrefab = _build.BuildingWith?.Prefab;
                previousSubconstructable =
                    _build.BuildingWith?.SubconstructableToLoad;
                previousRotation = _build.GetBuildMarkerLocalRotation();
            }
            catch
            {
                // A rejected selection still attempts the guarded restore below.
            }

            try
            {
                bool inventoryAvailable = false;
                bool inventoryAccepted = false;
                try
                {
                    InventoryGUI inventory = InventoryGUI.Instance;
                    inventoryAvailable = inventory != null;
                    if (inventoryAvailable)
                        inventoryAccepted = inventory.SetToItemNumber(definition, _build);
                }
                catch
                {
                    inventoryAvailable = false;
                }

                if (inventoryAvailable && !inventoryAccepted)
                {
                    RestoreNativeBlockSelection(
                        previousItem,
                        previousMode,
                        previousPrefab,
                        previousSubconstructable,
                        previousRotation);
                    InfoStore.Add("FtD's native inventory rejected that exact block selection.");
                    return false;
                }

                if (requireInventoryAcceptance && !inventoryAvailable)
                {
                    if (!NativeBlockDefinitionAvailableOnFocusedConstruct(definition))
                    {
                        RestoreNativeBlockSelection(
                            previousItem,
                            previousMode,
                            previousPrefab,
                            previousSubconstructable,
                            previousRotation);
                        InfoStore.Add("That native block is unavailable on the focused construct.");
                        return false;
                    }
                }

                _build.SetBlockToPlace(definition);
                ItemDefinition selected = _build.BuildingWith?.Item;
                if (_build.BuildingWith?.Mode != BuildingWithMode.Item ||
                    !ReferenceEquals(selected, definition))
                {
                    RestoreNativeBlockSelection(
                        previousItem,
                        previousMode,
                        previousPrefab,
                        previousSubconstructable,
                        previousRotation);
                    InfoStore.Add("FtD rejected the exact native block definition selected from the palette.");
                    return false;
                }

                if (copiedRotation.HasValue)
                    _build.SetBuildMarkerLocalRotation(copiedRotation.Value);

                _nativeBlockPaletteItem = definition;
                HideNativeBlockPaletteCursorPreview();
                DecorationEditorInputScope.SetNativeBlockPaletteExpectedItem(
                    definition);
                if (!string.IsNullOrWhiteSpace(displayName))
                    InfoStore.Add("Native block selected: " + displayName + ".");
                return true;
            }
            catch (Exception exception)
            {
                RestoreNativeBlockSelection(
                    previousItem,
                    previousMode,
                    previousPrefab,
                    previousSubconstructable,
                    previousRotation);
                InfoStore.Add("Native block selection failed: " + exception.Message);
                return false;
            }
        }

        private bool IsNativeBlockPaletteEntryAvailable(
            DecorationMeshCatalogEntry entry)
        {
            return IsNativeBlockPaletteEntry(entry) &&
                   entry.Component is ItemDefinition definition &&
                   NativeBlockDefinitionAvailableOnFocusedConstruct(definition);
        }

        private bool NativeBlockDefinitionAvailableOnFocusedConstruct(
            ItemDefinition definition)
        {
            if (!IsNativePlaceableBlockDefinition(definition))
                return false;

            try
            {
                if (!definition.DisplayOnInventory ||
                    !definition.InventoryTabOrVariantId.IsValidReference ||
                    _build.GetC() == null ||
                    !definition.CanYouPlaceOnThis(_build.GetC()))
                {
                    return false;
                }

                return definition.Unlock == null ||
                       definition.Unlock.Check().status;
            }
            catch
            {
                return false;
            }
        }

        private void RestoreNativeBlockSelection(
            ItemDefinition previousItem,
            BuildingWithMode previousMode,
            SavedSubObject previousPrefab,
            SavedSubObject previousSubconstructable,
            Quaternion previousRotation)
        {
            try
            {
                switch (previousMode)
                {
                    case BuildingWithMode.Paint:
                        _build.BeginFreePaint();
                        break;
                    case BuildingWithMode.Prefab when previousPrefab != null:
                        _build.SetLoadPrefab(previousPrefab);
                        break;
                    case BuildingWithMode.SubObject
                        when previousSubconstructable != null:
                        _build.SetLoadConstructable(previousSubconstructable);
                        break;
                    default:
                        // FtD treats a null item as its native Wood fallback, so
                        // this never restores an active Item mode with no item.
                        _build.SetBlockToPlace(previousItem);
                        break;
                }
                _build.SetBuildMarkerLocalRotation(previousRotation);
            }
            catch
            {
                // Selection failure is already reported; restoration is best effort.
            }
        }

        private void SyncNativeBlockPaletteSelection()
        {
            if (!_nativeBlockPaletteMode)
                return;

            ItemDefinition current = null;
            try
            {
                if (_build.BuildingWith?.Mode == BuildingWithMode.Item)
                    current = _build.BuildingWith.Item;
            }
            catch
            {
                // A missing exact item keeps native placement disarmed.
            }

            bool armed = IsNativePlaceableBlockDefinition(current);
            _nativeBlockPaletteItem = armed ? current : null;
            DecorationEditorInputScope.SetNativeBlockPaletteExpectedItem(
                armed ? current : null);
            if (!armed)
            {
                _selectedMesh = null;
                return;
            }

            Guid guid = NativeBlockGuid(current);
            _selectedMesh = guid != Guid.Empty &&
                            _meshByGuid.TryGetValue(
                                guid,
                                out DecorationMeshCatalogEntry entry) &&
                            IsNativeBlockPaletteEntry(entry)
                ? entry
                : null;
        }

        private static string NativeBlockName(ItemDefinition definition)
        {
            try
            {
                string name = definition?.GetInventoryNameConsideringVariants();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch
            {
                // Component GUID fallback below is stable.
            }

            Guid guid = NativeBlockGuid(definition);
            return guid == Guid.Empty ? "selected block" : guid.ToString("D");
        }

        private static Guid NativeBlockGuid(ItemDefinition definition)
        {
            try
            {
                return definition?.ComponentId?.Guid ?? Guid.Empty;
            }
            catch
            {
                return Guid.Empty;
            }
        }

        private bool PrepareNativeBlockPaletteCursorPreview(cBuild build)
        {
            if (!_nativeBlockPaletteMode ||
                !ReferenceEquals(build, _build) ||
                build == null ||
                !DecorationEditorInputScope.NativeBlockPaletteBuildContextValid(
                    build) ||
                build.buildMode != enumBuildMode.active ||
                DecorationEditorInputScope.MouseOverEditorUi ||
                _textInputFocused ||
                _unappliedClosePromptOpen ||
                _gizmoSettingsOpen ||
                _viewModeMenuOpen ||
                _anchorMenuOpen ||
                ForegroundContextMenuOpen())
            {
                HideNativeBlockPaletteCursorPreview();
                return false;
            }

            ItemDefinition selectedItem;
            try
            {
                selectedItem = build.BuildingWith?.Mode == BuildingWithMode.Item
                    ? build.BuildingWith.Item
                    : null;
            }
            catch
            {
                selectedItem = null;
            }

            AllConstruct focusedConstruct;
            try
            {
                focusedConstruct = build.GetC();
            }
            catch
            {
                focusedConstruct = null;
            }

            if (_nativeBlockPaletteItem == null ||
                !ReferenceEquals(selectedItem, _nativeBlockPaletteItem) ||
                !IsNativePlaceableBlockDefinition(selectedItem) ||
                build.buildMarker == null ||
                focusedConstruct == null ||
                !TryProbeNativeBlockPaletteCursor(
                    focusedConstruct,
                    out DecorationPointerHit hit) ||
                hit?.Construct == null ||
                !ReferenceEquals(hit.Construct, focusedConstruct) ||
                !TryResolveNativeBlockCursorPosition(
                    hit,
                    build.GetBuildMarkerLocalRotation(),
                    selectedItem,
                    out Vector3i markerPosition))
            {
                HideNativeBlockPaletteCursorPreview();
                return false;
            }

            try
            {
                build.buildMarker.transform.localPosition = markerPosition;
                build.buildMarker.SetBlockBeingBuiltOn(null);

                // Complex mode consumes the marker transform instead of replacing
                // it with FtD's camera-centre ray. It also runs the exact native
                // collision, long-block, resource, board, and item checks used by
                // placement and updates the marker's validity colour.
                build.DoBuildModeComplex();

                // Mouse-following placement uses the full native footprint. FtD's
                // keyboard marker normally shrinks with orbit distance, which is
                // useful for cursor-key building but misleading under a mouse.
                build.buildMarker.SetScale(1f);
                build.SetVisualSize();
                build.buildMarker.SetVisibilityOfMainMarkerAndVehicleDirectionArrows(true);
                bool ready = IsNativeBlockPalettePlacementReady(
                    build.GetAddRemove());
                if (!ready)
                {
                    NeutralizeNativeBlockPalettePlacement(build);
                    build.buildMarker.DisableTheRotationMarkers();
                }
                return ready;
            }
            catch
            {
                HideNativeBlockPaletteCursorPreview();
                return false;
            }
        }

        private bool TryProbeNativeBlockPaletteCursor(
            AllConstruct focusedConstruct,
            out DecorationPointerHit hit)
        {
            int frame = Time.frameCount;
            Vector3 mouse = Input.mousePosition;
            Camera camera = Camera.main ?? Camera.current;
            Vector3 rayOrigin = Vector3.zero;
            Vector3 rayDirection = Vector3.zero;
            Vector3 constructRayOrigin = Vector3.zero;
            Vector3 constructRayDirection = Vector3.zero;
            if (camera != null)
            {
                try
                {
                    Ray ray = camera.ScreenPointToRay(mouse);
                    rayOrigin = ray.origin;
                    rayDirection = ray.direction;
                    if (TryWorldToLocal(
                            focusedConstruct,
                            rayOrigin,
                            out Vector3 localOrigin) &&
                        TryWorldToLocal(
                            focusedConstruct,
                            rayOrigin + rayDirection,
                            out Vector3 localTarget))
                    {
                        constructRayOrigin = localOrigin;
                        constructRayDirection = localTarget - localOrigin;
                    }
                }
                catch
                {
                    camera = null;
                }
            }

            bool cached =
                _nativeBlockPaletteProbeFrame == frame &&
                ReferenceEquals(_nativeBlockPaletteProbeCamera, camera) &&
                ReferenceEquals(
                    _nativeBlockPaletteProbeConstruct,
                    focusedConstruct) &&
                (_nativeBlockPaletteProbeMouse - mouse).sqrMagnitude <= 0.0001f &&
                (_nativeBlockPaletteProbeRayOrigin - rayOrigin).sqrMagnitude <= 0.0000001f &&
                (_nativeBlockPaletteProbeRayDirection - rayDirection).sqrMagnitude <= 0.0000001f &&
                (_nativeBlockPaletteProbeConstructRayOrigin - constructRayOrigin).sqrMagnitude <= 0.0000001f &&
                (_nativeBlockPaletteProbeConstructRayDirection - constructRayDirection).sqrMagnitude <= 0.0000001f;
            if (!cached)
            {
                _nativeBlockPaletteProbeFrame = frame;
                _nativeBlockPaletteProbeMouse = mouse;
                _nativeBlockPaletteProbeCamera = camera;
                _nativeBlockPaletteProbeRayOrigin = rayOrigin;
                _nativeBlockPaletteProbeRayDirection = rayDirection;
                _nativeBlockPaletteProbeConstructRayOrigin = constructRayOrigin;
                _nativeBlockPaletteProbeConstructRayDirection = constructRayDirection;
                _nativeBlockPaletteProbeConstruct = focusedConstruct;
                _nativeBlockPaletteProbeHit = null;
                try
                {
                    _nativeBlockPaletteProbeResolved =
                        _pointerProbe.TryProbe(
                            DecorationPointerProbe.ProbeOptions.MeshPlacement,
                            out _nativeBlockPaletteProbeHit);
                }
                catch
                {
                    _nativeBlockPaletteProbeResolved = false;
                    _nativeBlockPaletteProbeHit = null;
                }
            }

            hit = _nativeBlockPaletteProbeHit;
            return _nativeBlockPaletteProbeResolved && hit != null;
        }

        private void InvalidateNativeBlockPaletteCursorProbe()
        {
            _nativeBlockPaletteProbeFrame = -1;
            _nativeBlockPaletteProbeCamera = null;
            _nativeBlockPaletteProbeConstruct = null;
            _nativeBlockPaletteProbeHit = null;
            _nativeBlockPaletteProbeResolved = false;
        }

        private static bool TryResolveNativeBlockCursorPosition(
            DecorationPointerHit hit,
            Quaternion rotation,
            ItemDefinition definition,
            out Vector3i markerPosition)
        {
            markerPosition = default;
            if (hit?.Construct == null || definition?.SizeInfo == null)
                return false;

            Vector3 outward;
            if (!DecorationPointerProbe.TryGetLocalFaceNormal(
                    hit.Anchor,
                    hit.LocalHit,
                    out outward))
            {
                try
                {
                    outward = hit.Construct.myTransform != null
                        ? hit.Construct.myTransform.InverseTransformDirection(hit.WorldNormal)
                        : hit.WorldNormal;
                }
                catch
                {
                    outward = Vector3.zero;
                }
            }

            if (!TryCardinalDirection(outward, out Vector3i outwardCell))
                return false;

            Vector3i sizePos;
            Vector3i sizeNeg;
            try
            {
                sizePos = definition.SizeInfo.SizePos;
                sizeNeg = definition.SizeInfo.SizeNeg;
            }
            catch
            {
                return false;
            }

            var inwardCell = new Vector3i(
                -outwardCell.x,
                -outwardCell.y,
                -outwardCell.z);
            Vector3 itemDirection = Quaternion.Inverse(rotation) * new Vector3(
                inwardCell.x,
                inwardCell.y,
                inwardCell.z);
            if (!TryCardinalDirection(itemDirection, out Vector3i localDirection))
                return false;

            return TryResolveNativeBlockCursorMarkerPosition(
                hit.Anchor,
                outwardCell,
                localDirection,
                sizePos,
                sizeNeg,
                out markerPosition);
        }

        internal static bool TryResolveNativeBlockCursorMarkerPosition(
            Vector3i hitAnchor,
            Vector3i outwardCell,
            Vector3i itemLocalInwardDirection,
            Vector3i sizePos,
            Vector3i sizeNeg,
            out Vector3i markerPosition)
        {
            markerPosition = default;
            if (!TryCardinalDirection(
                    new Vector3(outwardCell.x, outwardCell.y, outwardCell.z),
                    out outwardCell) ||
                !TryCardinalDirection(
                    new Vector3(
                        itemLocalInwardDirection.x,
                        itemLocalInwardDirection.y,
                        itemLocalInwardDirection.z),
                    out Vector3i localDirection))
            {
                return false;
            }

            var inwardCell = new Vector3i(
                -outwardCell.x,
                -outwardCell.y,
                -outwardCell.z);

            int extent;
            if (localDirection.x > 0)
                extent = sizePos.x;
            else if (localDirection.x < 0)
                extent = sizeNeg.x;
            else if (localDirection.y > 0)
                extent = sizePos.y;
            else if (localDirection.y < 0)
                extent = sizeNeg.y;
            else if (localDirection.z > 0)
                extent = sizePos.z;
            else if (localDirection.z < 0)
                extent = sizeNeg.z;
            else
                return false;

            extent = Math.Max(0, extent);
            var outsideCell = new Vector3i(
                hitAnchor.x + outwardCell.x,
                hitAnchor.y + outwardCell.y,
                hitAnchor.z + outwardCell.z);
            markerPosition = new Vector3i(
                outsideCell.x - inwardCell.x * extent,
                outsideCell.y - inwardCell.y * extent,
                outsideCell.z - inwardCell.z * extent);
            return true;
        }

        private static bool TryCardinalDirection(
            Vector3 direction,
            out Vector3i cardinal)
        {
            cardinal = default;
            if (!DecorationEditMath.IsFinite(direction) ||
                direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float x = Mathf.Abs(direction.x);
            float y = Mathf.Abs(direction.y);
            float z = Mathf.Abs(direction.z);
            if (x >= y && x >= z)
                cardinal = new Vector3i(direction.x >= 0f ? 1 : -1, 0, 0);
            else if (y >= z)
                cardinal = new Vector3i(0, direction.y >= 0f ? 1 : -1, 0);
            else
                cardinal = new Vector3i(0, 0, direction.z >= 0f ? 1 : -1);
            return true;
        }

        private void HideNativeBlockPaletteCursorPreview()
        {
            try
            {
                if (_build == null)
                    return;

                NeutralizeNativeBlockPalettePlacement(_build);
                if (_build.buildMarker == null)
                    return;
                _build.buildMarker.SetBlockBeingBuiltOn(null);
                _build.buildMarker.DisableTheRotationMarkers();
                _build.buildMarker.SetVisibilityOfMainMarkerAndVehicleDirectionArrows(false);
            }
            catch
            {
                // The next native build update restores its marker after ESU exits.
            }
        }

        internal static void NeutralizeNativeBlockPalettePlacement(cBuild build)
        {
            if (build == null)
                return;

            build.addRemove = enumAddRemove.neither;
            build.H2P = enumH2P.idle;
        }

        internal static bool IsNativeBlockPalettePlacementReady(
            enumAddRemove addRemove) =>
            addRemove == enumAddRemove.add;
    }
}
