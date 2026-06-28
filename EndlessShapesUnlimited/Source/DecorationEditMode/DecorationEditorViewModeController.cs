using System;
using System.Reflection;
using BrilliantSkies.Common.ChunkCreators;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.PlayerProfiles;

namespace DecoLimitLifter.DecorationEditMode
{
    internal sealed class DecorationEditorViewModeController
    {
        private readonly cBuild _build;
        private MBuild_Ftd _options;
        private bool _captured;
        private bool _previousWireframe;
        private bool _previousForceVisualisations;
        private SpecialBuildView _previousSpecialView;
        private MethodInfo _runMimicView;
        private bool _mimicViewUnavailable;

        internal DecorationEditorViewModeController(cBuild build)
        {
            _build = build;
        }

        internal void Capture(MBuild_Ftd options)
        {
            _options = options;
            if (_options != null)
            {
                _previousWireframe = _options.DecorationWireframe;
                _previousForceVisualisations = _options.ShowForceVisualisations;
            }
            _previousSpecialView = TryReadSpecialView() ?? SpecialBuildView.None;

            _runMimicView = typeof(cBuild).GetMethod(
                "RunMimicView",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _mimicViewUnavailable = _runMimicView == null;
            _captured = true;
        }

        internal void Apply(DecorationEditorViewMode mode)
        {
            if (!_captured)
                return;

            try
            {
                if (_options != null)
                {
                    _options.DecorationWireframe =
                        mode == DecorationEditorViewMode.Wireframe ||
                        mode == DecorationEditorViewMode.DecorationOnly;

                    _options.ShowForceVisualisations =
                        mode == DecorationEditorViewMode.Mass ||
                        mode == DecorationEditorViewMode.Drag ||
                        mode == DecorationEditorViewMode.Cost ||
                        mode == DecorationEditorViewMode.Surface ||
                        mode == DecorationEditorViewMode.Important
                            ? true
                            : _previousForceVisualisations;
                }

                SetSpecialView(ToSpecialBuildView(mode));
                if (mode == DecorationEditorViewMode.DecorationOnly)
                    TryRunMimicView();
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration Edit view mode apply failed",
                    exception,
                    LogOptions._AlertDevInGame);
            }
        }

        internal void Tick(DecorationEditorViewMode mode)
        {
            if (mode == DecorationEditorViewMode.DecorationOnly)
                TryRunMimicView();
        }

        internal void Restore()
        {
            if (!_captured)
                return;

            try
            {
                if (_options != null)
                {
                    _options.DecorationWireframe = _previousWireframe;
                    _options.ShowForceVisualisations = _previousForceVisualisations;
                }
                SetSpecialView(_previousSpecialView);
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Decoration Edit view mode restore failed",
                    exception,
                    LogOptions._AlertDevInGame);
            }
            finally
            {
                _captured = false;
            }
        }

        private void TryRunMimicView()
        {
            if (_build == null || _runMimicView == null || _mimicViewUnavailable)
                return;

            try
            {
                _runMimicView.Invoke(_build, Array.Empty<object>());
            }
            catch (TargetInvocationException exception)
            {
                _mimicViewUnavailable = true;
                if (exception.InnerException != null)
                {
                    AdvLogger.LogException(
                        "[EndlessShapes Unlimited] Native mimic view bridge failed",
                        exception.InnerException,
                        LogOptions._AlertDevInGame);
                }
            }
            catch
            {
                _mimicViewUnavailable = true;
                // The fallback ESU overlay remains valid if the native mimic view is unavailable.
            }
        }

        private void SetSpecialView(SpecialBuildView view)
        {
            try
            {
                MainConstruct main = _build?.GetCC();
                main?.ChunkControls?.SetSpecialView(view);
            }
            catch
            {
                // FTD versions that reject direct special-view control keep ESU's overlay fallback.
            }
        }

        private SpecialBuildView? TryReadSpecialView()
        {
            try
            {
                object controls = _build?.GetCC()?.ChunkControls;
                if (controls == null)
                    return null;

                object current = TryReadProperty(controls, "SpecialView");
                if (current is SpecialBuildView direct)
                    return direct;

                object displayOptions =
                    TryReadProperty(controls, "DisplayOptions") ??
                    TryReadField(controls, "DisplayOptions") ??
                    TryReadField(controls, "_displayOptions");
                current = TryReadProperty(displayOptions, "SpecialView");
                if (current is SpecialBuildView fromDisplayOptions)
                    return fromDisplayOptions;
            }
            catch
            {
                // Restore defaults to None if FTD does not expose the current view.
            }

            return null;
        }

        private static object TryReadProperty(object instance, string name)
        {
            if (instance == null)
                return null;
            PropertyInfo property = instance.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(instance, null);
        }

        private static object TryReadField(object instance, string name)
        {
            if (instance == null)
                return null;
            FieldInfo field = instance.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(instance);
        }

        private static SpecialBuildView ToSpecialBuildView(DecorationEditorViewMode mode)
        {
            switch (mode)
            {
                case DecorationEditorViewMode.DecorationOnly:
                    return SpecialBuildView.Mimic;
                case DecorationEditorViewMode.Mass:
                    return SpecialBuildView.Weight;
                case DecorationEditorViewMode.Drag:
                    return SpecialBuildView.Drag;
                case DecorationEditorViewMode.Cost:
                    return SpecialBuildView.Cost;
                case DecorationEditorViewMode.Surface:
                    return SpecialBuildView.Surface;
                case DecorationEditorViewMode.Important:
                    return SpecialBuildView.Important;
                default:
                    return SpecialBuildView.None;
            }
        }
    }
}
