using Microsoft.Gaming.XboxGameBar;
using Microsoft.Gaming.XboxGameBar.Input;
using Microsoft.UI.Xaml.Controls;
using NLog;
using Shared.Data;
using Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.System.Power;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.UI;
using XboxGamingBar.Data;
using XboxGamingBar.Event;
using XboxGamingBar.IPC;
using XboxGamingBar.QuickSettings;
using Shared.Enums;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {

        // GTLGo: emuladores (RetroBat) arrancan con el overlay en nivel 2 "Juego" la primera vez.
        private static readonly string[] GtlgoEmulatorExes =
        {
            "lichii", "fritchii", "eden", "citron", "yuzu", "suyu", "sudachi", "torzu", "ryujinx",
            "dolphin", "cemu", "rpcs3", "pcsx2-qt", "pcsx2", "duckstation-qt-x64-releaseltcg", "duckstation",
            "ppsspp", "ppssppwindows64", "xemu", "xenia", "xenia_canary", "vita3k", "retroarch", "azahar",
            "citra-qt", "lime3ds", "melonds", "flycast", "redream", "shadps4", "mame", "mesen"
        };

        /// <summary>
        /// GTLGo: aplica LocalState\gtlgo_import_widget.json a los contenedores Profile_Game_&lt;nombre&gt;
        /// (incluye _AC y _DC si existen) y renombra el archivo a .aplicado.
        /// </summary>
        private void ImportGtlgoWidgetPresets()
        {
            try
            {
                string dir = ApplicationData.Current.LocalFolder.Path;
                string file = System.IO.Path.Combine(dir, "gtlgo_import_widget.json");
                if (!System.IO.File.Exists(file)) return;
                var arr = Windows.Data.Json.JsonArray.Parse(System.IO.File.ReadAllText(file));
                var settings = ApplicationData.Current.LocalSettings;
                int applied = 0;
                foreach (var item in arr)
                {
                    var o = item.GetObject();
                    string name = o.ContainsKey("name") ? o.GetNamedString("name") : null;
                    if (string.IsNullOrEmpty(name)) continue;
                    // GTLGo: "global" is the general profile (no game): its containers are
                    // Profile_Global and the general AC/DC ones, Profile_AC and Profile_DC.
                    bool isGlobal = string.Equals(name, "global", StringComparison.OrdinalIgnoreCase);
                    bool any = false;
                    foreach (var suffix in new[] { "", "_AC", "_DC" })
                    {
                        string key = isGlobal
                            ? (suffix.Length == 0 ? "Profile_Global" : $"Profile{suffix}")
                            : $"Profile_Game_{name}{suffix}";
                        if (!settings.Containers.ContainsKey(key)) continue;
                        var c = settings.Containers[key];
                        string tdpKey = (suffix == "_DC" && o.ContainsKey("tdpDc")) ? "tdpDc" : "tdp";
                        if (o.ContainsKey(tdpKey)) c.Values["TDP"] = o.GetNamedNumber(tdpKey);
                        if (o.ContainsKey("autoTdp")) c.Values["AutoTDPEnabled"] = o.GetNamedBoolean("autoTdp");
                        if (o.ContainsKey("targetFps")) c.Values["AutoTDPTargetFPS"] = (int)o.GetNamedNumber("targetFps");
                        if (o.ContainsKey("minTdp")) c.Values["AutoTDPMinTDP"] = (int)o.GetNamedNumber("minTdp");
                        if (o.ContainsKey("maxTdp")) c.Values["AutoTDPMaxTDP"] = (int)o.GetNamedNumber("maxTdp");
                        c.Values["LastModifiedUtc"] = DateTime.UtcNow.Ticks;
                        any = true;
                    }
                    if (any) applied++;
                }
                string done = file + ".aplicado";
                if (System.IO.File.Exists(done)) System.IO.File.Delete(done);
                System.IO.File.Move(file, done);
                Logger.Info($"GTLGo: import de perfiles del widget: {applied} juegos actualizados");
            }
            catch (Exception ex)
            {
                Logger.Warn($"GTLGo: import de perfiles del widget falló: {ex.Message}");
            }
        }

        private void ApplyEmulatorOverlayDefault(PerformanceProfile profile)
        {
            try
            {
                string exe = System.IO.Path.GetFileNameWithoutExtension(runningGame?.Value.GameId.Path ?? "")?.ToLowerInvariant() ?? "";
                if (profile != null && System.Array.IndexOf(GtlgoEmulatorExes, exe) >= 0 && profile.OverlayLevel < 2)
                {
                    profile.OverlayLevel = 2;
                    Logger.Info($"GTLGo: nuevo perfil de emulador ({exe}) con overlay nivel 2");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"GTLGo: ApplyEmulatorOverlayDefault: {ex.Message}");
            }
        }

        private void LoadOrCreateGameProfiles()
        {
            if (!HasValidGame(currentGameName))
                return;

            var settings = ApplicationData.Current.LocalSettings;
            bool splitEnabled = GetPerGamePowerSourceProfileEnabled(currentGameName);
            bool hasSingle = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}");
            bool hasAC = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}_AC");
            bool hasDC = settings.Containers.ContainsKey($"Profile_Game_{currentGameName}_DC");

            if (splitEnabled)
            {
                // Ensure AC/DC game profiles exist. If only a single profile exists, seed both from it.
                PerformanceProfile seedProfile = null;
                if (hasSingle)
                {
                    seedProfile = new PerformanceProfile();
                    LoadProfileFromStorage($"Game_{currentGameName}", seedProfile);
                }

                if (!hasAC)
                {
                    gameACProfile = (seedProfile ?? acProfile).Clone();
                    if (seedProfile == null) ApplyEmulatorOverlayDefault(gameACProfile);
                    SaveProfileToStorage($"Game_{currentGameName}_AC", gameACProfile);
                    Logger.Info($"Initialized game AC profile for {currentGameName} (seed={(seedProfile != null ? "single profile" : "global AC")})");
                }
                else
                {
                    LoadProfileFromStorage($"Game_{currentGameName}_AC", gameACProfile);
                }

                if (!hasDC)
                {
                    gameDCProfile = (seedProfile ?? dcProfile).Clone();
                    if (seedProfile == null) ApplyEmulatorOverlayDefault(gameDCProfile);
                    SaveProfileToStorage($"Game_{currentGameName}_DC", gameDCProfile);
                    Logger.Info($"Initialized game DC profile for {currentGameName} (seed={(seedProfile != null ? "single profile" : "global DC")})");
                }
                else
                {
                    LoadProfileFromStorage($"Game_{currentGameName}_DC", gameDCProfile);
                }

                Logger.Info($"Loaded game AC/DC profiles for {currentGameName}");
                // Sync the per-state values down to the helper so it can apply them on
                // AC/DC transitions without the widget being awake. Without this the
                // helper would push its cached (current-state) TDP and the user would see
                // the slider update but hardware lag (the bug surfaced in build 2070
                // testing on 2026-04-25).
                SendPowerSourceProfileValuesToHelper();
            }
            else
            {
                // Ensure single game profile exists. If only AC/DC exists, seed from active power source profile.
                if (!hasSingle)
                {
                    PerformanceProfile seedProfile = null;
                    if (hasAC || hasDC)
                    {
                        var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                        bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;

                        string sourceProfileName;
                        if (isOnAC && hasAC)
                        {
                            sourceProfileName = $"Game_{currentGameName}_AC";
                        }
                        else if (!isOnAC && hasDC)
                        {
                            sourceProfileName = $"Game_{currentGameName}_DC";
                        }
                        else if (hasAC)
                        {
                            sourceProfileName = $"Game_{currentGameName}_AC";
                        }
                        else
                        {
                            sourceProfileName = $"Game_{currentGameName}_DC";
                        }

                        seedProfile = new PerformanceProfile();
                        LoadProfileFromStorage(sourceProfileName, seedProfile);
                        Logger.Info($"Seeding single game profile for {currentGameName} from {sourceProfileName}");
                    }

                    if (seedProfile == null && GetGlobalPowerSourceProfileEnabled())
                    {
                        var powerSupplyStatus = PowerManager.PowerSupplyStatus;
                        bool isOnAC = powerSupplyStatus != PowerSupplyStatus.NotPresent;
                        seedProfile = (isOnAC ? acProfile : dcProfile).Clone();
                        Logger.Info($"Seeding single game profile for {currentGameName} from global {(isOnAC ? "AC" : "DC")} profile");
                    }

                    gameProfile = (seedProfile ?? globalProfile).Clone();
                    if (seedProfile == null || !(hasAC || hasDC)) ApplyEmulatorOverlayDefault(gameProfile);
                    SaveProfileToStorage($"Game_{currentGameName}", gameProfile);
                    Logger.Info($"Initialized game profile for {currentGameName} (seed={(seedProfile != null ? "active profile" : "global profile")})");
                }
                else
                {
                    LoadProfileFromStorage($"Game_{currentGameName}", gameProfile);
                    Logger.Info($"Loaded existing game profile for {currentGameName}");
                }
            }

            // Stamp the running game's exe path into every container we just created or
            // loaded for this title. Used by the Profiles tab to group multiple titles
            // that share an exe (e.g. emulators like Citron / RetroArch where each game
            // produces a different window title) under a single collapsed parent card.
            EnsureGameExePathStored(currentGameName);
        }

        /// <summary>
        /// Writes the current running game's full exe path into every Profile_Game_<name>
        /// container that exists for the given title (single, _AC, _DC). Idempotent —
        /// safe to call repeatedly. Skipped silently when the running game's path is
        /// not available (game closed mid-load, race during startup).
        /// </summary>
        private void EnsureGameExePathStored(string gameName)
        {
            try
            {
                if (runningGame == null) return;
                var rg = runningGame.Value; // RunningGame is a struct, can't ?.
                if (rg == null || !rg.IsValid() || rg.GameId == null) return;
                string exePath = rg.GameId.Path;
                if (string.IsNullOrEmpty(exePath)) return;
                if (string.IsNullOrEmpty(gameName)) return;

                var settings = ApplicationData.Current.LocalSettings;
                foreach (var suffix in new[] { "", "_AC", "_DC" })
                {
                    var key = $"Profile_Game_{gameName}{suffix}";
                    if (settings.Containers.ContainsKey(key))
                    {
                        var existing = settings.Containers[key].Values.ContainsKey("GameExePath")
                            ? settings.Containers[key].Values["GameExePath"] as string
                            : null;
                        if (existing != exePath)
                        {
                            settings.Containers[key].Values["GameExePath"] = exePath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"EnsureGameExePathStored({gameName}) failed: {ex.Message}");
            }
        }

    }
}
