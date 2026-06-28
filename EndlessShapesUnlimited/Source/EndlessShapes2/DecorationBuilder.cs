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
        private const float MinimumScale = 0.000001f;

        private readonly List<PolygonData> _polygonDataList = new List<PolygonData>();
        private readonly List<Decoration> _createdDuringRun = new List<Decoration>();
        private readonly List<PlaceBlockCommand> _placedDuringRun = new List<PlaceBlockCommand>();

        private AllConstruct _myAllConstruct;
        private AllConstructDecorations _decorations;
        private GenerationRun _run;
        private GenerationLease _generationLease;
        private List<PolygonData> _buildQueue;
        private int _buildIndex;
        private float _buildCredit;
        private bool _buildRegistered;
        private bool _generationActive;

        public DecorationBuilderData Data { get; set; } = new DecorationBuilderData(1U);

        public List<OBJ_Mesh> Meshes { get; } = new List<OBJ_Mesh>();

        public List<Vector3> Vertices { get; } = new List<Vector3>();

        public List<Vector2> UVs { get; } = new List<Vector2>();

        public OBJ_Mesh SelectMesh { get; private set; }

        public int PolygonDataListCount => _polygonDataList.Count;

        public bool IsGenerationActive => _generationActive;

        public float GenerationProgress
        {
            get
            {
                if (!_generationActive || _polygonDataList.Count == 0)
                    return 0f;
                int completed = _buildQueue == null ? _createdDuringRun.Count : _buildIndex;
                return Mathf.Clamp01(completed / (float)_polygonDataList.Count);
            }
        }

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
                string objPath = FilePathInput.Normalize(Data.OBJ_FilePath.Us);
                Data.OBJ_FilePath.Us = objPath;
                ObjParseResult parsed = ObjParser.ParseFile(objPath);
                Meshes.AddRange(parsed.Meshes);
                foreach (ObjVector3 vertex in parsed.Vertices)
                    Vertices.Add(new Vector3(-vertex.X, vertex.Y, vertex.Z));
                foreach (ObjVector2 uv in parsed.TextureCoordinates)
                    UVs.Add(new Vector2(uv.X, uv.Y));

                InfoStore.Add(
                    $"Loaded {Meshes.Count:N0} OBJ mesh group(s) and {Vertices.Count:N0} vertices.");
            }
            catch (Exception exception) when (FilePathInput.IsExpectedInputFailure(exception))
            {
                ClearLoadedModel();
                ReportInputFailure(
                    "OBJ import failed",
                    exception,
                    FilePathInput.Normalize(Data.OBJ_FilePath.Us),
                    "OBJ");
            }
            catch (Exception exception)
            {
                ClearLoadedModel();
                ReportFailure("OBJ import failed", exception);
            }
        }

        public void SetSelectMesh(OBJ_Mesh mesh)
        {
            if (_generationActive)
            {
                InfoStore.Add("Cancel the active decoration build before selecting another mesh.");
                return;
            }

            SelectMesh = null;
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
                ValidateTransformSettings();
                var transformedVertices = TransformVertices(construct);
                PolygonDataControl.PolygonClassify(
                    _polygonDataList,
                    mesh.FaceDatas,
                    mesh.LineDatas,
                    transformedVertices,
                    UVs,
                    mesh.FaceSourceLines,
                    mesh.LineSourceLines,
                    AllConstructDecorations._limitPerPacketManager);

                if (_polygonDataList.Count == 0)
                    throw new InvalidDataException("The selected mesh produces no decorations.");

                SelectMesh = mesh;
                InfoStore.Add(
                    $"Selected '{mesh.Name}': {_polygonDataList.Count:N0} decorations will be generated.");
            }
            catch (Exception exception)
            {
                SelectMesh = null;
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

                synchronous = !_run.BuildAnimation;
                if (!synchronous)
                {
                    _buildQueue = new List<PolygonData>(_polygonDataList);
                    _buildIndex = 0;
                    _buildCredit = 0f;
                    MainConstruct.SchedulerRestricted.RegisterForFixedUpdate(BuildAnimation);
                    _buildRegistered = true;
                    InfoStore.Add($"Animated generation started: {_buildQueue.Count:N0} decorations queued.");
                    return;
                }

                foreach (PolygonData polygonData in _polygonDataList)
                {
                    if (!Generate(polygonData))
                    {
                        throw new InvalidOperationException(
                            "The game rejected a block or decoration during generation.");
                    }
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

        public void CancelGeneration()
        {
            if (!_generationActive)
            {
                InfoStore.Add("No decoration build is active.");
                return;
            }

            int generated = _createdDuringRun.Count;
            CompleteGeneration(success: false);
            InfoStore.Add($"Decoration build cancelled; rolled back {generated:N0} decorations.");
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

            var connectionRules = _myAllConstruct.Main.ConnectionRules as ConnectionRules;
            if (connectionRules == null)
            {
                InfoStore.Add("The construct connection rules are unavailable.");
                return false;
            }

            _createdDuringRun.Clear();
            _placedDuringRun.Clear();
            _run = CreateGenerationRun();

            if (!GenerationLease.TryAcquire(
                    _myAllConstruct.Main,
                    connectionRules,
                    out _generationLease))
            {
                _run.Dispose();
                _run = null;
                InfoStore.Add("Another Decoration Builder is already running on this main construct.");
                return false;
            }

            _generationActive = true;
            return true;
        }

        private GenerationRun CreateGenerationRun()
        {
            ValidateGenerationSettings();

            ItemDefinition itemDefinition = null;
            if (Data.TP_BlockPlacement.Us)
            {
                if (!Guid.TryParse(Data.TP_BlockGUID.Us, out Guid guid))
                    throw new InvalidDataException("Tether block GUID is not a valid GUID.");
                itemDefinition = Configured.i
                    .Get<ModificationComponentContainerItem>()
                    .Find(guid, out bool found);
                if (!found || itemDefinition == null)
                    throw new InvalidDataException("The configured tether block GUID is unavailable.");
            }

            var palette = new Color[32];
            for (int index = 0; index < palette.Length; index++)
                palette[index] = _myAllConstruct.Main.ColorsRestricted.GetColor(index);

            Texture2D texture = null;
            try
            {
                texture = LoadTexture(Data.TexturePath.Us);
                return new GenerationRun(
                    new PolygonDecorationSettings(
                        normalReversal: false,
                        Data.FaceThickness.Us,
                        Data.LineThickness.Us,
                        Data.SBType.Us),
                    Mathf.Clamp(Data.DefaultColorIndex.Us, 0, 31),
                    Data.TP_AutoTetherPoint.Us,
                    Data.TP_NormalOffset.Us,
                    Data.TP_DistanceToShift.Us,
                    Data.TP_BlockPlacement.Us,
                    itemDefinition,
                    Data.BuildAnimation.Us,
                    Data.BA_Speed.Us,
                    palette,
                    texture);
            }
            catch
            {
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
                throw;
            }
        }

        private static Texture2D LoadTexture(string texturePath)
        {
            texturePath = FilePathInput.Normalize(texturePath);
            if (string.IsNullOrWhiteSpace(texturePath))
                return null;

            var file = new FileInfo(texturePath);
            if (!file.Exists)
                throw new FileNotFoundException("The selected texture file does not exist.", texturePath);
            if (file.Length > MaxTextureBytes)
            {
                throw new InvalidDataException(
                    $"Texture is {file.Length:N0} bytes; the limit is {MaxTextureBytes:N0} bytes.");
            }

            ImageDimensions dimensions = ImagePreflight.ReadAndValidate(texturePath);
            var texture = new Texture2D(2, 2);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(texturePath)))
                    throw new InvalidDataException("Unity could not decode the selected texture.");
                if (texture.width != dimensions.Width || texture.height != dimensions.Height)
                    throw new InvalidDataException("Decoded texture dimensions do not match its file header.");
                return texture;
            }
            catch
            {
                UnityEngine.Object.Destroy(texture);
                throw;
            }
        }

        private void BuildAnimation(ITimeStep timeStep)
        {
            try
            {
                double credit = _buildCredit + (double)timeStep.DeltaTime * _run.AnimationSpeed;
                if (double.IsNaN(credit) || double.IsInfinity(credit) || credit < 0d)
                    throw new InvalidOperationException("Animated generation produced invalid build timing.");
                int remaining = _buildQueue.Count - _buildIndex;
                int count = credit >= remaining
                    ? remaining
                    : Math.Max(0, (int)Math.Floor(credit));
                _buildCredit = count == remaining ? 0f : (float)(credit - count);

                int stop = Math.Min(_buildIndex + count, _buildQueue.Count);
                while (_buildIndex < stop)
                {
                    PolygonData next = _buildQueue[_buildIndex++];
                    if (!Generate(next))
                    {
                        throw new InvalidOperationException(
                            "The game rejected a block or decoration during animated generation.");
                    }
                }

                if (_buildIndex == _buildQueue.Count)
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
            MADCD_PolygonInput.Start(
                decorationData,
                polygonData,
                _run.PolygonSettings,
                _run.ColorFor(polygonData));

            Vector3i tetherPosition = LocalPosition;
            if (_run.AutoTetherPoint)
            {
                Vector3 roundedPosition = Vector3Int.RoundToInt(decorationData.Positioning);
                if (_run.NormalOffset)
                {
                    Vector3 original = roundedPosition;
                    Vector3Int roundedOriginal = Vector3Int.RoundToInt(original);
                    roundedPosition = original - polygonData.NormalVector * _run.DistanceToShift;

                    if (roundedOriginal.x == 0 || Mathf.Sign(original.x) != Mathf.Sign(roundedPosition.x))
                        roundedPosition.x = 0;
                    if (roundedOriginal.y == 0 || Mathf.Sign(original.y) != Mathf.Sign(roundedPosition.y))
                        roundedPosition.y = 0;
                    if (roundedOriginal.z == 0 || Mathf.Sign(original.z) != Mathf.Sign(roundedPosition.z))
                        roundedPosition.z = 0;
                }

                EnsureFinite(roundedPosition, "automatic tether position");
                tetherPosition = (Vector3i)roundedPosition;
            }

            decorationData.Positioning -= tetherPosition;
            EnsureFinite(decorationData.Positioning, "generated decoration positioning");

            if (_run.BlockPlacement &&
                _myAllConstruct.AllBasics.GetBlockViaLocalPosition(tetherPosition) == null)
            {
                var placeCommand = new PlaceBlockCommand(
                    _myAllConstruct,
                    tetherPosition,
                    Quaternion.identity,
                    _run.ItemDefinition,
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

        private void CompleteGeneration(bool success)
        {
            if (!_generationActive && _generationLease == null && _run == null)
                return;

            _generationActive = false;
            var errors = new List<Exception>();

            if (_buildRegistered)
            {
                try
                {
                    _myAllConstruct?.Main?.SchedulerRestricted.UnregisterForFixedUpdate(BuildAnimation);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                _buildRegistered = false;
            }

            if (!success)
                RollbackGeneratedContent(errors);

            try
            {
                _generationLease?.Release(errors);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                _run?.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            _createdDuringRun.Clear();
            _placedDuringRun.Clear();
            _buildQueue = null;
            _buildIndex = 0;
            _generationLease = null;
            _run = null;
            _decorations = null;
            _myAllConstruct = null;

            if (errors.Count > 0)
                ReportCleanupFailures(errors);
        }

        private void RollbackGeneratedContent(ICollection<Exception> errors)
        {
            for (int index = _createdDuringRun.Count - 1; index >= 0; index--)
            {
                try
                {
                    Decoration decoration = _createdDuringRun[index];
                    if (decoration != null && !decoration.IsDeleted)
                        decoration.Delete();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            for (int index = _placedDuringRun.Count - 1; index >= 0; index--)
            {
                try
                {
                    PlaceBlockCommand command = _placedDuringRun[index];
                    if (command?.Success == true)
                        command.Undo();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
        }

        private void CancelActiveGeneration(bool rollback)
        {
            if (_generationActive)
                CompleteGeneration(success: !rollback);
        }

        private void ClearFailedPreparation()
        {
            var errors = new List<Exception>();
            try
            {
                _generationLease?.Release(errors);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
            try
            {
                _run?.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            _createdDuringRun.Clear();
            _placedDuringRun.Clear();
            _buildQueue = null;
            _generationLease = null;
            _run = null;
            _decorations = null;
            _myAllConstruct = null;
            if (errors.Count > 0)
                ReportCleanupFailures(errors);
        }

        private List<Vector3> TransformVertices(AllConstruct construct)
        {
            var transformed = new List<Vector3>(Vertices.Count);
            bool isMain = construct.IsMain;
            Vector3 subConstructOffset = Vector3.zero;
            Quaternion subConstructRotation = Quaternion.identity;

            if (!isMain)
            {
                Transform constructTransform = construct.myTransform;
                Transform mainTransform = construct.Main.myTransform;
                Quaternion mainInverse = Quaternion.Inverse(mainTransform.rotation);
                subConstructOffset = mainInverse * (constructTransform.position - mainTransform.position);
                subConstructRotation = Quaternion.Inverse(mainInverse * constructTransform.rotation);
            }

            for (int index = 0; index < Vertices.Count; index++)
            {
                Vector3 vertex = Vector3.Scale(Vertices[index], Data.Scaling.Us);
                vertex = Quaternion.Euler(Data.Orientation.Us) * vertex;
                if (Data.LocalOrigin.Us)
                {
                    vertex += Data.Positioning.Us;
                    if (!isMain)
                        vertex = subConstructRotation * (vertex - subConstructOffset);
                }
                else
                {
                    vertex += Data.Positioning.Us + LocalPosition;
                }

                EnsureFinite(vertex, "transformed vertex");
                transformed.Add(vertex);
            }
            return transformed;
        }

        private void ValidateTransformSettings()
        {
            EnsureFinite(Data.Positioning.Us, "positioning");
            EnsureFinite(Data.Scaling.Us, "scaling");
            EnsureFinite(Data.Orientation.Us, "orientation");
            Vector3 scale = Data.Scaling.Us;
            if (Mathf.Abs(scale.x) < MinimumScale ||
                Mathf.Abs(scale.y) < MinimumScale ||
                Mathf.Abs(scale.z) < MinimumScale)
            {
                throw new InvalidDataException("Scaling cannot collapse an axis to zero.");
            }
        }

        private void ValidateGenerationSettings()
        {
            new PolygonDecorationSettings(
                false,
                Data.FaceThickness.Us,
                Data.LineThickness.Us,
                Data.SBType.Us).Validate(0);
            if (!FlexibleFloatParser.IsFinite(Data.TP_DistanceToShift.Us) ||
                Data.TP_DistanceToShift.Us < 0f)
            {
                throw new InvalidDataException("Tether normal-offset distance must be finite and non-negative.");
            }
            if (!FlexibleFloatParser.IsFinite(Data.BA_Speed.Us) || Data.BA_Speed.Us <= 0f)
                throw new InvalidDataException("Build animation speed must be finite and greater than zero.");
        }

        private static void EnsureFinite(Vector3 value, string name)
        {
            if (!FlexibleFloatParser.IsFinite(value.x) ||
                !FlexibleFloatParser.IsFinite(value.y) ||
                !FlexibleFloatParser.IsFinite(value.z))
            {
                throw new InvalidDataException($"{name} must contain only finite numbers.");
            }
        }

        private void ClearLoadedModel()
        {
            SelectMesh = null;
            _polygonDataList.Clear();
            Meshes.Clear();
            Vertices.Clear();
            UVs.Clear();
        }

        private static void ReportFailure(string context, Exception exception)
        {
            InfoStore.Add($"{context}: {exception.Message}");
            AdvLogger.LogException(
                $"[EndlessShapes Unlimited] {context}",
                exception,
                LogOptions._AlertDevAndCustomerInGame);
        }

        private static void ReportInputFailure(
            string context,
            Exception exception,
            string path,
            string kind)
        {
            string message = exception is FileNotFoundException
                ? FilePathInput.MissingFileMessage(kind, path)
                : exception.Message;
            InfoStore.Add($"{context}: {message}");
            AdvLogger.LogInfo($"[EndlessShapes Unlimited] {context}: {message}");
        }

        private static void ReportCleanupFailures(IReadOnlyCollection<Exception> errors)
        {
            var aggregate = new AggregateException("One or more cleanup operations failed.", errors);
            InfoStore.Add("Decoration cleanup encountered errors; see the log for details.");
            AdvLogger.LogException(
                "[EndlessShapes Unlimited] Decoration cleanup failed",
                aggregate,
                LogOptions._AlertDevAndCustomerInGame);
        }

        public override void PrepForDelete()
        {
            try
            {
                CancelActiveGeneration(rollback: true);
                ClearFailedPreparation();
            }
            catch (Exception exception)
            {
                ReportFailure("Decoration Builder deletion cleanup failed", exception);
            }
            finally
            {
                base.PrepForDelete();
            }
        }

        private sealed class GenerationRun : IDisposable
        {
            private Texture2D _texture;

            internal GenerationRun(
                PolygonDecorationSettings polygonSettings,
                int defaultColorIndex,
                bool autoTetherPoint,
                bool normalOffset,
                float distanceToShift,
                bool blockPlacement,
                ItemDefinition itemDefinition,
                bool buildAnimation,
                float animationSpeed,
                Color[] palette,
                Texture2D texture)
            {
                PolygonSettings = polygonSettings;
                DefaultColorIndex = defaultColorIndex;
                AutoTetherPoint = autoTetherPoint;
                NormalOffset = normalOffset;
                DistanceToShift = distanceToShift;
                BlockPlacement = blockPlacement;
                ItemDefinition = itemDefinition;
                BuildAnimation = buildAnimation;
                AnimationSpeed = animationSpeed;
                Palette = palette;
                _texture = texture;
            }

            internal PolygonDecorationSettings PolygonSettings { get; }

            internal int DefaultColorIndex { get; }

            internal bool AutoTetherPoint { get; }

            internal bool NormalOffset { get; }

            internal float DistanceToShift { get; }

            internal bool BlockPlacement { get; }

            internal ItemDefinition ItemDefinition { get; }

            internal bool BuildAnimation { get; }

            internal float AnimationSpeed { get; }

            internal Color[] Palette { get; }

            internal int ColorFor(PolygonData polygonData)
            {
                int colorIndex = DefaultColorIndex;
                if (_texture == null || polygonData.PolyType == PolygonType.Line)
                    return colorIndex;

                Color pixel = _texture.GetPixelBilinear(polygonData.UV.x, polygonData.UV.y);
                float difference = float.MaxValue;
                for (int index = 0; index < Palette.Length; index++)
                {
                    Color palette = Palette[index];
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

            public void Dispose()
            {
                if (_texture == null)
                    return;
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }
        }
    }
}
