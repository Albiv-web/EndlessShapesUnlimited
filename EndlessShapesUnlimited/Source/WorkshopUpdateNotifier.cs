using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BrilliantSkies.Core.Constants;
using BrilliantSkies.Core.Timing;
using BrilliantSkies.Modding;
using Newtonsoft.Json.Linq;
using Steamworks;
using NetVersion = System.Version;

namespace DecoLimitLifter
{
    internal static class WorkshopUpdateNotifier
    {
        internal const string LatestVersionPrefix = "Mod latest version ";
        internal const int MaximumRequests = 5;

        private const string UpdateStatusKeySuffix = "::WorkshopUpdate";
        private static readonly Action<ITimeStep> RequestCallback = OnRequestTick;

        private static string _modName;
        private static string _modPath;
        private static NetVersion _localVersion;
        private static ulong _workshopId;
        private static int _requestCount;
        private static bool _registered;
        private static bool _requestInFlight;
        private static CallResult<SteamUGCRequestUGCDetailsResult_t> _steamCall;

        internal static void Start(string modName, NetVersion fallbackVersion)
        {
            try
            {
                _modName = string.IsNullOrWhiteSpace(modName)
                    ? "EndlessShapes Unlimited"
                    : modName;
                _modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrWhiteSpace(_modPath))
                    return;

                _localVersion = fallbackVersion ?? new NetVersion(0, 0, 0);
                _workshopId = 0UL;
                ReadInstalledManifest(_modPath, ref _localVersion, ref _workshopId);
                RemoveUpdateStatus();
                if (_workshopId == 0UL)
                    return;

                _requestCount = 0;
                _requestInFlight = false;
                TryUnregister();
                GameEvents.Twice_Second.RegWithEvent(RequestCallback);
                _registered = true;
            }
            catch
            {
                TryUnregister();
            }
        }

        internal static bool TryParseLatestVersionForVerification(
            string description,
            out NetVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(description))
                return false;

            using (var reader = new StringReader(description))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = StripWorkshopMarkup(line).Trim();
                    if (!line.StartsWith(LatestVersionPrefix, StringComparison.Ordinal))
                        continue;

                    string value = line.Substring(LatestVersionPrefix.Length).Trim();
                    return NetVersion.TryParse(value, out version);
                }
            }

            return false;
        }

        private static string StripWorkshopMarkup(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            StringBuilder builder = new StringBuilder(value.Length);
            bool insideTag = false;
            foreach (char character in value)
            {
                if (character == '[')
                {
                    insideTag = true;
                    continue;
                }

                if (character == ']' && insideTag)
                {
                    insideTag = false;
                    continue;
                }

                if (!insideTag)
                    builder.Append(character);
            }

            return builder.ToString();
        }

        internal static bool IsWorkshopVersionNewerForVerification(
            NetVersion localVersion,
            string description,
            out NetVersion workshopVersion)
        {
            if (!TryParseLatestVersionForVerification(description, out workshopVersion))
                return false;

            localVersion = localVersion ?? new NetVersion(0, 0, 0);
            return workshopVersion > localVersion;
        }

        private static void OnRequestTick(ITimeStep timeStep)
        {
            if (_requestInFlight)
                return;

            if (_requestCount >= MaximumRequests)
            {
                TryUnregister();
                return;
            }

            try
            {
                if (!SteamAPI.IsSteamRunning())
                {
                    TryUnregister();
                    return;
                }

                _requestCount++;
                SteamAPICall_t call = SteamUGC.RequestUGCDetails(
                    new PublishedFileId_t(_workshopId),
                    0);
                _steamCall = new CallResult<SteamUGCRequestUGCDetailsResult_t>(
                    OnWorkshopDetailsReceived);
                _requestInFlight = true;
                _steamCall.Set(call);
            }
            catch
            {
                TryUnregister();
            }
        }

        private static void OnWorkshopDetailsReceived(
            SteamUGCRequestUGCDetailsResult_t result,
            bool ioFailure)
        {
            _requestInFlight = false;
            if (ioFailure)
            {
                if (_requestCount >= MaximumRequests)
                    TryUnregister();
                return;
            }

            TryUnregister();
            string description = result.m_details.m_rgchDescription;
            if (!IsWorkshopVersionNewerForVerification(
                    _localVersion,
                    description,
                    out NetVersion workshopVersion))
            {
                RemoveUpdateStatus();
                return;
            }

            AddUpdateStatus(workshopVersion);
        }

        private static void AddUpdateStatus(NetVersion workshopVersion)
        {
            try
            {
                string key = UpdateStatusKey;
                ModProblems.AllModProblems.Remove(key);
                ModProblems.AddModProblem(
                    _modName,
                    key,
                    "New version released! v" + FormatVersion(workshopVersion),
                    false);
                RefreshActiveGuis();
            }
            catch
            {
                // The notifier is informational only; startup must never depend on it.
            }
        }

        private static void RemoveUpdateStatus()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_modPath))
                    return;

                if (ModProblems.AllModProblems.Remove(UpdateStatusKey))
                    RefreshActiveGuis();
            }
            catch
            {
                // The notifier is informational only; startup must never depend on it.
            }
        }

        private static void ReadInstalledManifest(
            string modPath,
            ref NetVersion localVersion,
            ref ulong workshopId)
        {
            string manifestPath = Path.Combine(modPath, "plugin.json");
            if (!File.Exists(manifestPath))
                return;

            JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
            string versionText = manifest["version"]?.ToString();
            if (!string.IsNullOrWhiteSpace(versionText) &&
                NetVersion.TryParse(versionText, out NetVersion manifestVersion))
            {
                localVersion = manifestVersion;
            }

            JToken workshopToken = manifest["workshop_id"];
            if (workshopToken == null)
                return;

            if (workshopToken.Type == JTokenType.Integer)
            {
                workshopId = workshopToken.Value<ulong>();
                return;
            }

            ulong.TryParse(
                workshopToken.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out workshopId);
        }

        private static void TryUnregister()
        {
            if (!_registered)
                return;

            try { GameEvents.Twice_Second.UnregWithEvent(RequestCallback); }
            catch { }
            _registered = false;
            _requestInFlight = false;
        }

        private static string UpdateStatusKey =>
            string.IsNullOrWhiteSpace(_modPath)
                ? UpdateStatusKeySuffix
                : _modPath + UpdateStatusKeySuffix;

        private static string FormatVersion(NetVersion version)
        {
            if (version == null)
                return "unknown";
            return version.Build >= 0
                ? version.ToString(3)
                : version.ToString();
        }

        private static void RefreshActiveGuis()
        {
            try
            {
                Type displayerType = Type.GetType(
                    "BrilliantSkies.Ui.Displayer.GuiDisplayer, Ui",
                    false);
                object displayer = displayerType
                    ?.GetMethod("GetSingleton", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, null);
                if (displayer == null)
                    return;

                object activeGuis =
                    displayerType.GetProperty(
                        "ActiveGuis",
                        BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(displayer, null) ??
                    displayerType.GetField(
                        "ActiveGuis",
                        BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(displayer);
                if (!(activeGuis is IEnumerable guis))
                    return;

                foreach (object gui in guis)
                {
                    gui?.GetType()
                        .GetMethod(
                            "OnActivateGui",
                            BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Instance)
                        ?.Invoke(gui, null);
                }
            }
            catch
            {
                // The update row can still appear after the screen is reopened.
            }
        }
    }
}
