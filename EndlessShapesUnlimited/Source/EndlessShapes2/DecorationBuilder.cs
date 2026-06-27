using System;
using System.Collections.Generic;
using System.IO;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Timing;
using BrilliantSkies.Core.Types;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.Build.UndoRedo;
using BrilliantSkies.Ftd.Constructs.Connections;
using BrilliantSkies.Ftd.Constructs.Modules.All.Decorations;
using BrilliantSkies.Modding;
using BrilliantSkies.Modding.Containers;
using BrilliantSkies.Modding.Types;
using BrilliantSkies.Ui.Special.InfoStore;
using BrilliantSkies.Ui.Tips;
using EndlessShapes2.Polygon;
using EndlessShapes2.UI;
using UnityEngine;

namespace EndlessShapes2
{
    public class DecorationBuilder : Block
    {
        private const long MaxTextureBytes = 64L * 1024L * 1024L;

        private readonly List<PolygonData> _polygonDataList = new List<PolygonData>();
        private readonly List<Color> _colorPalette = new List<Color>(32);
        private readonly List<Decoration> _createdDuringRun = new List<Decoration>();
        private readonly List<PlaceBlockCommand> _placedDuringRun = new List<PlaceBlockCommand>();

        private Texture2D _texture;
        private AllConstruct _myAllConstruct;
        private AllConstructDecorations _decorations;
        private ItemDefinition _itemDefinition;
        private bool _itemDefinitionFound;
        private List<PolygonData> _buildQueue;
        private float _buildCredit;
        private bool _buildRegistered;
        private bool _generationActive;
        private ConnectionRules _connectionRules;
        private bool _previousMasterSwitch;
        private bool _previousRequestSwitch;

        public DecorationBuilderData Data { get; set; } = new DecorationBuilderData(1U);

        public List<OBJ_Mesh> Meshes { get; } = new List<OBJ_Mesh>();

        public List<Vector3> Vertices { get; } = new List<Vector3>();

        public List<Vector2> UVs { get; } = new List<Vector2>();

        public OBJ_Mesh SelectMesh { get; private set; }

        public int PolygonDataListCount => _polygonDataList.Count;

        protected override void AppendToolTip(ProTip tip)
        {
            base.AppendToolTip(tip);
            tip.Add(new ProTipSegment_Text(
                200,
                "Imports OBJ geometry and converts the selected mesh into decorations."));
        }

        public override void Secondary(Transform transform)
        {
            new DBUI_SettingWindows(this).ActivateGui();
        }

        public void Load()
        {
            CancelActiveGeneration(rollback: true);
            ClearLoadedModel();

            try
            {
                ObjParseResult parsed = ObjParser.ParseFile(Data.OBJ_FilePath.Us);
                Meshes.AddRange(parsed.Meshes);
                foreach (ObjVector3 vertex in parsed.Vertices)
                    Vertices.Add(new Vector3(-vertex.X, vertex.Y, vertex.Z));
                foreach (ObjVector2 uv in parsed.TextureCoordinates)
                    UVs.Add(new Vector2(uv.X, uv.Y));

                InfoStore.Add(
                    $"Loaded {Meshes.Count:N0} OBJ mesh group(s) and {Vertices.Count:N0} vertices.");
            }
            catch (Exception exception)
            {
                ClearLoadedModel();
                ReportFailure("OBJ import failed", exception);
            }
        }

        public void SetSelectMesh(OBJ_Mesh mesh)
        {
            SelectMesh = mesh;
            _polygonDataList.Clear();

            if (mesh == null || !Meshes.Contains(mesh))
            {
                InfoStore.Add("Select a mesh from the loaded OBJ file.");
                return;
            }

            AllConstruct construct = GetC();
            if (construct == null)
            {
                InfoStore.Add("Decoration Builder is not attached to a construct.");
                return;
            }

            try
            {
                var transformedVertices = new List<Vector3>(Vertices.Count);
                bool isMain = construct.IsMain;
                Vector3 subConstructOffset = Vector3.zero;
                Quaternion subConstructRotation = Quaternion.identity;

                if (!isMain)
                {
                    Transform constructTransform = construct.myTransform;
                    Transform mainTransform = construct.Main.myTransform;
                    Quaternion mainInverse = Quaternion.Inverse(mainTransform.rotation);
                    subConstructOffset = mainInverse *
                                         (constructTransform.position - mainTransform.position);
                    subConstructRotation = Quaternion.Inverse(
                        mainInverse * constructTransform.rotation);
                }

                for (int index = 0; index < Vertices.Count; index++)
                {
                    Vector3 vertex = Vector3.Scale(Vertices[index], Data.Scaling.Us);
                    vertex = Quaternion.Euler(Data.Orientation.Us) * vertex;

                    if (Data.LocalOrigin)
                    {
                        vertex += Data.Positioning.Us;
                        if (!isMain)
                            vertex = subConstructRotation * (vertex - subConstructOffset);
                    }
                    else
                    {
                        vertex += Data.Positioning.Us + LocalPosition;
                    }

                    transformedVertices.Add(vertex);
                }

                PolygonDataControl.PolygonClassify(
                    _polygonDataList,
                    mesh.FaceDatas,
                    mesh.LineDatas,
                    transformedVertices,
                    UVs);

                InfoStore.Add(
                    $"Selected '{mesh.Name}': {_polygonDataList.Count:N0} decorations will be generated.");
            }
            catch (Exception exception)
            {
                _polygonDataList.Clear();
                ReportFailure("OBJ mesh conversion failed", exception);
            }
        }

        public void Start()
        {
            if (_generationActive)
            {
                InfoStore.Add("A decoration build is already in progress.");
                return;
            }

            bool synchronous = !Data.BuildAnimation.Us;
            bool success = false;
            try
            {
                if (!BeginGeneration())
                    return;

                if (!synchronous)
                {
                    _buildQueue = new List<PolygonData>(_polygonDataList);
                    _buildCredit = 0f;
                    MainConstruct.SchedulerRestricted.RegisterForFixedUpdate(BuildAnimation);
                    _buildRegistered = true;
                    return;
                }

                foreach (PolygonData polygonData in _polygonDataList)
                {
                    if (!Generate(polygonData))
                        throw new InvalidOperationException(
                            "The game rejected a block or decoration during generation.");
                }

                success = true;
                InfoStore.Add($"Generated {_createdDuringRun.Count:N0} decorations.");
            }
            catch (Exception exception)
            {
                ReportFailure("Decoration generation failed", exception);
                if (_generationActive)
                    CompleteGeneration(success: false);
                else
                    ClearFailedPreparation();
            }
            finally
            {
                if (synchronous && _generationActive)
                    CompleteGeneration(success);
            }
        }

        private bool BeginGeneration()
        {
            if (SelectMesh == null || _polygonDataList.Count == 0)
            {
                InfoStore.Add("Load an OBJ file and select a non-empty mesh first.");
                return false;
            }

            _myAllConstruct = GetC();
            _decorations = _myAllConstruct?.Decorations as AllConstructDecorations;
            if (_decorations == null)
            {
                InfoStore.Add("The construct decoration manager is unavailable.");
                return false;
            }

            long requestedTotal = (long)_decorations.DecorationCount + _polygonDataList.Count;
            if (requestedTotal > AllConstructDecorations._limitPerPacketManager)
            {
                InfoStore.Add(
                    $"This mesh needs {_polygonDataList.Count:N0} decorations, but only " +
                    $"{Math.Max(0, AllConstructDecorations._limitPerPacketManager - _decorations.DecorationCount):N0} remain.");
                return false;
            }

            _connectionRules = _myAllConstruct.Main.ConnectionRules as ConnectionRules;
            if (_connectionRules == null)
            {
                InfoStore.Add("The construct connection rules are unavailable.");
                return false;
            }

            _createdDuringRun.Clear();
            _placedDuringRun.Clear();
            _itemDefinition = null;
            _itemDefinitionFound = false;
            if (Data.TP_BlockPlacement.Us &&
                Guid.TryParse(Data.TP_BlockGUID.Us, out Guid guid))
            {
                _itemDefinition = Configured.i
                    .Get<ModificationComponentContainerItem>()
                    .Find(guid, out _itemDefinitionFound);
            }

            PreparePaletteAndTexture();

            MADCD_PolygonInput.NormalReversal = false;
            MADCD_PolygonInput.FaceThickness = Data.FaceThickness.Us;
            MADCD_PolygonInput.LineThickness = Data.LineThickness.Us;
            MADCD_PolygonInput.SBType = Data.SBType.Us;
            MADCD_PolygonInput.ColorSetting = ColorSetting;

            _previousMasterSwitch = _connectionRules.Data.MasterSwitch.Us;
            _previousRequestSwitch = _connectionRules.Data.RequestSwitch.Us;
            _connectionRules.Data.MasterSwitch.Us = false;
            _connectionRules.Data.RequestSwitch.Us = false;
            _generationActive = true;
            return true;
        }

        private void PreparePaletteAndTexture()
        {
            _colorPalette.Clear();
            for (int index = 0; index < 32; index++)
                _colorPalette.Add(_myAllConstruct.Main.ColorsRestricted.GetColor(index));

            DestroyTexture();
            string texturePath = Data.TexturePath.Us;
            if (string.IsNullOrWhiteSpace(texturePath))
                return;

            var file = new FileInfo(texturePath);
            if (!file.Exists)
                throw new FileNotFoundException("The selected texture file does not exist.", texturePath);
            if (file.Length > MaxTextureBytes)
                throw new InvalidDataException(
                    $"Texture is {file.Length:N0} bytes; the limit is {MaxTextureBytes:N0} bytes.");

            _texture = new Texture2D(2, 2);
            if (!ImageConversion.LoadImage(_texture, File.ReadAllBytes(texturePath)))
                throw new InvalidDataException("Unity could not decode the selected texture.");
            _texture.Compress(false);
        }

        private void BuildAnimation(ITimeStep timeStep)
        {
            try
            {
                float speed = Math.Max(0.1f, Data.BA_Speed.Us);
                _buildCredit += timeStep.DeltaTime * speed;
                int count = Mathf.FloorToInt(_buildCredit);
                _buildCredit -= count;

                for (int index = 0; index < count && _buildQueue.Count > 0; index++)
                {
                    PolygonData next = _buildQueue[0];
                    _buildQueue.RemoveAt(0);
                    if (!Generate(next))
                        throw new InvalidOperationException(
                            "The game rejected a block or decoration during animated generation.");
                }

                if (_buildQueue.Count == 0)
                {
                    InfoStore.Add($"Generated {_createdDuringRun.Count:N0} decorations.");
                    CompleteGeneration(success: true);
                }
            }
            catch (Exception exception)
            {
                ReportFailure("Animated decoration generation failed", exception);
                CompleteGeneration(success: false);
            }
        }

        private bool Generate(PolygonData polygonData)
        {
            var decorationData = new MimicAndDecorationCommonData();
            MADCD_PolygonInput.Start(decorationData, polygonData);

            Vector3i tetherPosition = LocalPosition;
            if (Data.TP_AutoTetherPoint.Us)
            {
                Vector3 roundedPosition = Vector3Int.RoundToInt(decorationData.Positioning);
                if (Data.TP_NormalOffset.Us)
                {
                    Vector3 original = roundedPosition;
                    Vector3Int roundedOriginal = Vector3Int.RoundToInt(original);
                    roundedPosition = original -
                                      polygonData.NormalVector * Data.TP_DistanceToShift.Us;

                    if (roundedOriginal.x == 0 ||
                        Mathf.Sign(original.x) != Mathf.Sign(roundedPosition.x))
                        roundedPosition.x = 0;
                    if (roundedOriginal.y == 0 ||
                        Mathf.Sign(original.y) != Mathf.Sign(roundedPosition.y))
                        roundedPosition.y = 0;
                    if (roundedOriginal.z == 0 ||
                        Mathf.Sign(original.z) != Mathf.Sign(roundedPosition.z))
                        roundedPosition.z = 0;
                }

                tetherPosition = (Vector3i)roundedPosition;
            }

            decorationData.Positioning -= tetherPosition;

            if (_itemDefinitionFound &&
                _myAllConstruct.AllBasics.GetBlockViaLocalPosition(tetherPosition) == null)
            {
                var placeCommand = new PlaceBlockCommand(
                    _myAllConstruct,
                    tetherPosition,
                    Quaternion.identity,
                    _itemDefinition,
                    0,
                    MirrorInfo.none);
                placeCommand.Apply();
                if (!placeCommand.Success)
                    return false;
                _placedDuringRun.Add(placeCommand);
            }

            Decoration decoration = _decorations.NewDecoration(
                tetherPosition,
                force: true,
                playSound: false,
                forceEvenIfMaxReached: true);
            if (decoration == null)
                return false;

            _createdDuringRun.Add(decoration);
            MimicAndDecorationCommonData.Copy(
                decorationData,
                new MimicAndDecorationCommonData(decoration));
            return true;
        }

        private int ColorSetting(PolygonData polygonData)
        {
            int colorIndex = Mathf.Clamp(Data.DefaultColorIndex.Us, 0, 31);
            if (_texture == null || polygonData.PolyType == PolygonType.Line)
                return colorIndex;

            Color pixel = _texture.GetPixelBilinear(polygonData.UV.x, polygonData.UV.y);
            float difference = float.MaxValue;
            for (int index = 0; index < _colorPalette.Count; index++)
            {
                Color palette = _colorPalette[index];
                float candidate = new Vector3(
                        palette.r - pixel.r,
                        palette.g - pixel.g,
                        palette.b - pixel.b).magnitude +
                    Math.Abs(palette.a - pixel.a);
                if (candidate < difference)
                {
                    difference = candidate;
                    colorIndex = index;
                }
            }

            return colorIndex;
        }

        private void CompleteGeneration(bool success)
        {
            if (_buildRegistered)
            {
                _myAllConstruct?.Main?.SchedulerRestricted.UnregisterForFixedUpdate(BuildAnimation);
                _buildRegistered = false;
            }

            if (!success)
                RollbackGeneratedContent();

            if (_connectionRules != null)
            {
                _connectionRules.Data.MasterSwitch.Us = _previousMasterSwitch;
                _connectionRules.Data.RequestSwitch.Us = _previousRequestSwitch;
            }

            DestroyTexture();
            _createdDuringRun.Clear();
            _placedDuringRun.Clear();
            _buildQueue = null;
            _connectionRules = null;
            _decorations = null;
            _myAllConstruct = null;
            _generationActive = false;
        }

        private void RollbackGeneratedContent()
        {
            for (int index = _createdDuringRun.Count - 1; index >= 0; index--)
            {
                Decoration decoration = _createdDuringRun[index];
                if (decoration != null && !decoration.IsDeleted)
                    decoration.Delete();
            }

            for (int index = _placedDuringRun.Count - 1; index >= 0; index--)
            {
                PlaceBlockCommand command = _placedDuringRun[index];
                if (command?.Success == true)
                    command.Undo();
            }
        }

        private void CancelActiveGeneration(bool rollback)
        {
            if (_generationActive)
                CompleteGeneration(success: !rollback);
        }

        private void ClearFailedPreparation()
        {
            DestroyTexture();
            _createdDuringRun.Clear();
            _placedDuringRun.Clear();
            _buildQueue = null;
            _connectionRules = null;
            _decorations = null;
            _myAllConstruct = null;
        }

        private void ClearLoadedModel()
        {
            SelectMesh = null;
            _polygonDataList.Clear();
            Meshes.Clear();
            Vertices.Clear();
            UVs.Clear();
        }

        private void DestroyTexture()
        {
            if (_texture == null)
                return;
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
        }

        private static void ReportFailure(string context, Exception exception)
        {
            InfoStore.Add($"{context}: {exception.Message}");
            AdvLogger.LogException(
                $"[EndlessShapes Unlimited] {context}",
                exception,
                LogOptions._AlertDevAndCustomerInGame);
        }

        public void OnDestroy()
        {
            CancelActiveGeneration(rollback: true);
            DestroyTexture();
        }
    }
}
