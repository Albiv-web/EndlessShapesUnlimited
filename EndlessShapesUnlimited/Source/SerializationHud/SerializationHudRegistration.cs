using System;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.Core.Timing;
using BrilliantSkies.PlayerProfiles;
using BrilliantSkies.Ui.Consoles;
using BrilliantSkies.Ui.Consoles.Examples;
using BrilliantSkies.Ui.Special.InfoStore;

namespace DecoLimitLifter.SerializationHud
{
    internal static class SerializationHudRegistration
    {
        private static readonly Action<ITimeStep> UpdateCallback = OnUpdate;
        private static readonly Func<ConsoleWindow, ConsoleUiScreen> ScreenFactory =
            window => new SerializationHudOptionsScreen(window);
        private static bool _eventRegistered;
        private static bool _registered;
        private static bool _screenRegistered;

        internal static bool Enabled
        {
            get
            {
                try { return SerializationHudProfile.Data.Enabled; }
                catch { return false; }
            }
        }

        internal static void Register()
        {
            if (_registered)
                return;

            // Force both profile modules to initialise before registering callbacks.
            _ = SerializationHudProfile.Data;
            _ = SerializationHudKeyMap.Instance;
            try
            {
                if (!FtdOptionsMenuUi.ExtraScreens.Contains(ScreenFactory))
                {
                    _screenRegistered = true;
                    FtdOptionsMenuUi.ExtraScreens.Add(ScreenFactory);
                }
                else
                {
                    _screenRegistered = true;
                }
                _eventRegistered = true;
                GameEvents.UpdateEvent.RegWithEvent(UpdateCallback);
                _registered = true;
            }
            catch (Exception registrationFailure)
            {
                try { Unregister(); }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "Serialization HUD registration and cleanup both failed.",
                        registrationFailure,
                        cleanupFailure);
                }
                throw;
            }
        }

        internal static void Unregister()
        {
            Exception failure = null;
            if (_screenRegistered)
            {
                try { FtdOptionsMenuUi.ExtraScreens.Remove(ScreenFactory); }
                catch (Exception exception) { failure = exception; }
            }
            if (_eventRegistered)
            {
                try { GameEvents.UpdateEvent.UnregWithEvent(UpdateCallback); }
                catch (Exception exception)
                {
                    failure = failure == null
                        ? exception
                        : new AggregateException(failure, exception);
                }
            }
            _screenRegistered = false;
            _eventRegistered = false;
            _registered = false;
            if (failure != null)
                throw failure;
        }

        private static void OnUpdate(ITimeStep timeStep)
        {
            try
            {
                if (!DecoLimitLifter.EsuInputState.CanUseHotkeys())
                {
                    return;
                }

                SerializationHudKeyMap keyMap = SerializationHudKeyMap.Instance;
                if (keyMap.Bool(
                        SerializationHudKeyInput.MeasureUsage,
                        KeyInputEventType.Down))
                {
                    MeasureCurrentConstruct();
                    return;
                }

                if (!keyMap.Bool(
                        SerializationHudKeyInput.ToggleHud,
                        KeyInputEventType.Down))
                {
                    return;
                }

                SerializationHudProfile.ProfileData data = SerializationHudProfile.Data;
                data.Enabled = !data.Enabled;
                InfoStore.Add($"Serialization HUD {(data.Enabled ? "enabled" : "disabled")}.");
                try
                {
                    ProfileManager.Instance.Save(module => module is SerializationHudProfile);
                }
                catch (Exception exception)
                {
                    AdvLogger.LogException(
                        "[EndlessShapes Unlimited] Could not persist the HUD setting",
                        exception,
                        LogOptions._AlertDevInGame);
                }
            }
            catch (Exception exception)
            {
                AdvLogger.LogException(
                    "[EndlessShapes Unlimited] Serialization HUD key handler failed",
                    exception,
                    LogOptions._AlertDevInGame);
            }
        }

        private static void MeasureCurrentConstruct()
        {
            MainConstruct construct = SerializationHudRenderer.CachedConstruct;
            if (construct == null)
            {
                InfoStore.Add("No focused vehicle available for serialization measurement.");
                return;
            }

            InfoStore.Add("Measuring serialization usage. Large vehicles may pause briefly.");
            CraftSerializationSnapshot snapshot = SerializationUsageMeasurement.Measure(construct);
            SerializationHudRenderer.Invalidate(construct);

            BlueprintSerializationUsage usage =
                snapshot?.BlueprintUsage ?? BlueprintSerializationUsage.Empty;
            InfoStore.Add(
                $"Serialization measured: {SerializationHudRenderer.FormatName(snapshot?.Format ?? SerializationWireFormat.Unknown)}, " +
                $"largest stream {SerializationHudRenderer.FormatLargestStream(usage)}, " +
                $"payload total {SerializationHudRenderer.FormatPayloadTotal(usage)}, " +
                $"buffer {SerializationHudRenderer.FormatSaveBuffer(usage)}.");
        }
    }
}
