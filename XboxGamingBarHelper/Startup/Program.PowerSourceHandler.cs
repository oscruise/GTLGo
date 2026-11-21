using NLog;
using Shared.Enums;
using System;
using System.Collections.Generic;

namespace XboxGamingBarHelper
{
    /// <summary>
    /// Helper-side AC/DC transition handling. The widget is UWP and stops receiving
    /// PowerManager.PowerSourceChanged callbacks while Game Bar is dismissed, so any
    /// AC↔DC transition that happens between Game Bar sessions used to be dropped
    /// (issue #72). The helper caches the active profile's per-state values, subscribes
    /// to SystemManager.PowerSourceChanged, and does the same work the widget would
    /// have done — independent of widget lifecycle.
    /// </summary>
    internal partial class Program
    {
        private static class PowerSourceProfileState
        {
            // Per-state TDP / TDPBoost values. Populated via PowerSourceProfileValues pipe
            // message from the widget. null = no override for that field (helper falls back
            // to whatever the active profile / current property already holds).
            public static int? AcTdp = null;
            public static int? DcTdp = null;
            public static bool? AcTdpBoost = null;
            public static bool? DcTdpBoost = null;

            // Extended per-state values (build 2080+). Same null-means-no-override
            // convention. Covers the full set the widget previously sent piecemeal via
            // per-property pipes — now the helper can reapply all of them on AC/DC
            // transitions independent of widget lifecycle (FSE-only-helper scenarios).
            public static bool? AcCpuBoost = null;
            public static bool? DcCpuBoost = null;
            public static int? AcCpuEpp = null;
            public static int? DcCpuEpp = null;
            public static int? AcOsPowerMode = null;
            public static int? DcOsPowerMode = null;
            // FPSLimit: 0 means "disabled", non-zero is the cap. Widget's profile splits
            // these into FPSLimitEnabled+FPSLimitValue; we collapse on the wire.
            public static int? AcFpsLimit = null;
            public static int? DcFpsLimit = null;

            // AutoTDP per-state (#94 item 3).
            public static bool? AcAutoTdp = null;
            public static bool? DcAutoTdp = null;
            public static int? AcAutoTdpFps = null;
            public static int? DcAutoTdpFps = null;
        }

        // Tracks last observed isOnAC so we can skip the (relatively expensive) plan-switch
        // and TDP-reapply work when SystemManager fires PowerSourceChanged for a status
        // transition that doesn't actually cross the AC/DC boundary. Real-world example
        // (Diego's box, build 2068): a flaky or underspec USB-C charger produces a torrent
        // of Inadequate ↔ Adequate transitions — both map to isOnAC=true, so re-pushing
        // TDP on each is wasted work and could fight the hardware. null on first call so
        // the first real transition always fires (initial seeding is done in SystemManager).
        private static bool? _lastIsOnAC;

        /// <summary>
        /// Cache the per-state TDP / TDPBoost values for the active profile so the helper
        /// can apply them on AC/DC transitions. Widget should call this whenever the
        /// active profile or its AC/DC sub-profile changes. JSON value of `null` for any
        /// field means "no override" — helper leaves that property alone on transition.
        /// </summary>
        internal static void ApplyPowerSourceProfileValues(string configJson)
        {
            try
            {
                var cfg = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(configJson);
                if (cfg == null) return;

                int? ParseInt(string key)
                {
                    if (!cfg.TryGetValue(key, out var el)) return null;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetInt32(out int v)) return v;
                    return null;
                }
                bool? ParseBool(string key)
                {
                    if (!cfg.TryGetValue(key, out var el)) return null;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                    return null;
                }

                PowerSourceProfileState.AcTdp = ParseInt("AcTdp");
                PowerSourceProfileState.DcTdp = ParseInt("DcTdp");
                PowerSourceProfileState.AcTdpBoost = ParseBool("AcTdpBoost");
                PowerSourceProfileState.DcTdpBoost = ParseBool("DcTdpBoost");

                PowerSourceProfileState.AcCpuBoost = ParseBool("AcCpuBoost");
                PowerSourceProfileState.DcCpuBoost = ParseBool("DcCpuBoost");
                PowerSourceProfileState.AcCpuEpp = ParseInt("AcCpuEpp");
                PowerSourceProfileState.DcCpuEpp = ParseInt("DcCpuEpp");
                PowerSourceProfileState.AcOsPowerMode = ParseInt("AcOsPowerMode");
                PowerSourceProfileState.DcOsPowerMode = ParseInt("DcOsPowerMode");
                PowerSourceProfileState.AcFpsLimit = ParseInt("AcFpsLimit");
                PowerSourceProfileState.DcFpsLimit = ParseInt("DcFpsLimit");
                PowerSourceProfileState.AcAutoTdp = ParseBool("AcAutoTdp");
                PowerSourceProfileState.DcAutoTdp = ParseBool("DcAutoTdp");
                PowerSourceProfileState.AcAutoTdpFps = ParseInt("AcAutoTdpFps");
                PowerSourceProfileState.DcAutoTdpFps = ParseInt("DcAutoTdpFps");
                UseProfileFileTdp();

                Logger.Info($"Applied PowerSourceProfileValues "
                    + $"(AC: tdp={PowerSourceProfileState.AcTdp?.ToString() ?? "-"}W, "
                    + $"boost={PowerSourceProfileState.AcTdpBoost?.ToString() ?? "-"}, "
                    + $"cpuBoost={PowerSourceProfileState.AcCpuBoost?.ToString() ?? "-"}, "
                    + $"epp={PowerSourceProfileState.AcCpuEpp?.ToString() ?? "-"}, "
                    + $"osMode={PowerSourceProfileState.AcOsPowerMode?.ToString() ?? "-"}, "
                    + $"fpsLimit={PowerSourceProfileState.AcFpsLimit?.ToString() ?? "-"}; "
                    + $"DC: tdp={PowerSourceProfileState.DcTdp?.ToString() ?? "-"}W, "
                    + $"boost={PowerSourceProfileState.DcTdpBoost?.ToString() ?? "-"}, "
                    + $"cpuBoost={PowerSourceProfileState.DcCpuBoost?.ToString() ?? "-"}, "
                    + $"epp={PowerSourceProfileState.DcCpuEpp?.ToString() ?? "-"}, "
                    + $"osMode={PowerSourceProfileState.DcOsPowerMode?.ToString() ?? "-"}, "
                    + $"fpsLimit={PowerSourceProfileState.DcFpsLimit?.ToString() ?? "-"})");
            }
            catch (Exception ex)
            {
                Logger.Error($"ApplyPowerSourceProfileValues: {ex.Message}");
                return;
            }
            ReconcileTdpWithPowerSource();
        }

        /// <summary>True when a charger is connected (Inadequate counts as AC, as in the widget).</summary>
        internal static bool IsOnACPower()
        {
            return global::Windows.System.Power.PowerManager.PowerSupplyStatus
                   != global::Windows.System.Power.PowerSupplyStatus.NotPresent;
        }

        /// <summary>
        /// GTLGo: a profile's TDP for a power state. The profile file keeps the AC value in TDP and
        /// the battery one in TDP_DC (null or 0: the AC value). Every switch to a profile applied
        /// TDP alone, and only the widget's PowerSourceProfileValues corrected it to the battery
        /// value, so with the widget asleep a game detected again on battery stayed at its AC TDP
        /// (Legion, 2026-11-21 10:42:08-10:45:58: Zelda at 25 W on battery, its battery TDP 20 W,
        /// after four re-detections while FriGo's settings window came and went).
        /// </summary>
        internal static int ProfileTdpFor(Shared.Data.GameProfile profile, bool onAC)
        {
            if (onAC || !profile.TDP_DC.HasValue || profile.TDP_DC.Value <= 0)
            {
                return profile.TDP;
            }
            return profile.TDP_DC.Value;
        }

        /// <summary>GTLGo: a profile's CPU boost for a power state (CPUBoost_DC null: the AC value).</summary>
        internal static bool ProfileCpuBoostFor(Shared.Data.GameProfile profile, bool onAC)
        {
            return onAC ? profile.CPUBoost : (profile.CPUBoost_DC ?? profile.CPUBoost);
        }

        /// <summary>GTLGo: a profile's CPU EPP for a power state (CPUEPP_DC null: the AC value).</summary>
        internal static int ProfileCpuEppFor(Shared.Data.GameProfile profile, bool onAC)
        {
            return onAC ? profile.CPUEPP : (profile.CPUEPP_DC ?? profile.CPUEPP);
        }

        /// <summary>GTLGo: a profile's AutoTDP switch for a power state (AutoTDPEnabled_DC null: the AC value).</summary>
        internal static bool ProfileAutoTdpFor(Shared.Data.GameProfile profile, bool onAC)
        {
            return onAC ? profile.AutoTDPEnabled : (profile.AutoTDPEnabled_DC ?? profile.AutoTDPEnabled);
        }

        /// <summary>GTLGo: a profile's AutoTDP target for a power state (AutoTDPTargetFPS_DC null: the AC value).</summary>
        internal static int ProfileAutoTdpFpsFor(Shared.Data.GameProfile profile, bool onAC)
        {
            return onAC ? profile.AutoTDPTargetFPS : (profile.AutoTDPTargetFPS_DC ?? profile.AutoTDPTargetFPS);
        }

        /// <summary>
        /// GTLGo: the helper's AC/DC cache takes the values of the profile it just switched to, from
        /// its file. Before, only the widget filled the cache, so a charger plugged or pulled while
        /// the widget slept applied the previous profile's AC/DC values to the new one.
        /// </summary>
        internal static void SeedPowerSourceStateFromProfile(Shared.Data.GameProfile profile, string name)
        {
            try
            {
                PowerSourceProfileState.AcTdp = ProfileTdpFor(profile, true);
                PowerSourceProfileState.DcTdp = ProfileTdpFor(profile, false);
                PowerSourceProfileState.AcCpuBoost = ProfileCpuBoostFor(profile, true);
                PowerSourceProfileState.DcCpuBoost = ProfileCpuBoostFor(profile, false);
                PowerSourceProfileState.AcCpuEpp = ProfileCpuEppFor(profile, true);
                PowerSourceProfileState.DcCpuEpp = ProfileCpuEppFor(profile, false);
                PowerSourceProfileState.AcAutoTdp = ProfileAutoTdpFor(profile, true);
                PowerSourceProfileState.DcAutoTdp = ProfileAutoTdpFor(profile, false);
                PowerSourceProfileState.AcAutoTdpFps = ProfileAutoTdpFpsFor(profile, true);
                PowerSourceProfileState.DcAutoTdpFps = ProfileAutoTdpFpsFor(profile, false);
                Logger.Info($"GTLGo: AC/DC values from the profile file of {name}: "
                    + $"AC {PowerSourceProfileState.AcTdp}W, DC {PowerSourceProfileState.DcTdp}W");
            }
            catch (Exception ex)
            {
                Logger.Warn($"GTLGo: SeedPowerSourceStateFromProfile: {ex.Message}");
            }
        }

        /// <summary>
        /// GTLGo: logs which TDP a profile switch applies for the current power state.
        /// </summary>
        private static int ProfileTdpForNow(Shared.Data.GameProfile profile, string name)
        {
            bool onAC = IsOnACPower();
            int tdp = ProfileTdpFor(profile, onAC);
            Logger.Info($"GTLGo: {name}: TDP {tdp}W for the current power state ({(onAC ? "AC" : "DC")}; "
                + $"profile AC {ProfileTdpFor(profile, true)}W / DC {ProfileTdpFor(profile, false)}W)");
            return tdp;
        }

        /// <summary>
        /// GTLGo: a TDP the widget sends in the first 15 s after the helper switched profile that
        /// differs from the profile file's value for the current power state is the widget's own,
        /// older copy of the profile (it keeps one per profile and sends it when the profile
        /// changes). At 10:39:37 on 2026-11-21 its copy of the general profile (25 W) arrived 1.5 s
        /// after the switch, was applied on battery and saved as the general profile's battery
        /// TDP. Within that window the file wins; the helper's value is sent back to the widget.
        /// After it, a change made in the widget is the player's, as before.
        /// </summary>
        internal static bool IsStaleWidgetTdp(int value)
        {
            try
            {
                double since = (DateTime.UtcNow - profileSwitchTime).TotalSeconds;
                if (since < 0 || since >= 15 || isApplyingProfile)
                {
                    return false;
                }
                if (performanceManager == null || performanceManager.IsAutoTDPActive)
                {
                    return false;
                }
                if (defaultGameProfileManager != null && defaultGameProfileManager.ProfileEnabled.Value)
                {
                    return false;
                }
                var cur = profileManager?.CurrentProfile;
                if (cur == null || (!cur.IsGlobalProfile && !cur.Use))
                {
                    return false;
                }
                bool onAC = IsOnACPower();
                int file = ProfileTdpFor(cur.Value, onAC);
                if (file <= 0 || value == file)
                {
                    return false;
                }
                Logger.Info($"GTLGo: TDP {value}W from the widget {since:F1} s after switching to {cur.GameId.Name} "
                    + $"ignored: its profile file says {file}W ({(onAC ? "AC" : "DC")})");
                SendTdpBackToWidget();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"GTLGo: IsStaleWidgetTdp: {ex.Message}");
                return false;
            }
        }

        private static int tdpResyncPending = 0;

        /// <summary>GTLGo: the widget's slider gets the helper's TDP back (after the pipe reply).</summary>
        private static void SendTdpBackToWidget()
        {
            if (System.Threading.Interlocked.Exchange(ref tdpResyncPending, 1) == 1)
            {
                return;
            }
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(300);
                    performanceManager?.TDP?.SyncToRemote();
                }
                catch (Exception ex)
                {
                    Logger.Debug($"GTLGo: TDP resync failed: {ex.Message}");
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref tdpResyncPending, 0);
                }
            });
        }

        /// <summary>
        /// GTLGo: the game's profile file decides its TDP. The widget keeps its own copy of each
        /// profile and sends it when a game starts; Mario Kart World's copy still had 30 W / 25 W
        /// while its file had 25 W / 20 W, and the copy won (Legion, 2026-11-15 19:21). For 15 s
        /// after the helper switches to a game's profile the file's values replace the widget's;
        /// after that a change made in the widget is the player's and is saved to the file.
        /// </summary>
        private static void UseProfileFileTdp()
        {
            try
            {
                var cur = profileManager?.CurrentProfile;
                if (cur == null || (!cur.IsGlobalProfile && !cur.Use))
                {
                    return;
                }
                int fileAc = ProfileTdpFor(cur.Value, true);
                int fileDc = ProfileTdpFor(cur.Value, false);
                int? sentAc = PowerSourceProfileState.AcTdp, sentDc = PowerSourceProfileState.DcTdp;
                // GTLGo: the general profile too (2026-11-21 10:37:24: the widget's copy, 25/25 W,
                // replaced the file's 15 W a second after RetroBat came back). Only inside the
                // window: after it the widget's values for the general profile apply as before.
                if (cur.IsGlobalProfile && (DateTime.UtcNow - profileSwitchTime).TotalSeconds >= 15)
                {
                    return;
                }
                if ((DateTime.UtcNow - profileSwitchTime).TotalSeconds < 15)
                {
                    if ((sentAc.HasValue && sentAc.Value != fileAc) || (sentDc.HasValue && sentDc.Value != fileDc))
                    {
                        Logger.Info($"GTLGo: the widget's copy of {cur.GameId.Name} says AC {sentAc}W / DC {sentDc}W; "
                            + $"its profile file says AC {fileAc}W / DC {fileDc}W: the file wins");
                    }
                    PowerSourceProfileState.AcTdp = fileAc;
                    PowerSourceProfileState.DcTdp = fileDc;
                    return;
                }
                if (sentAc.HasValue && sentAc.Value > 0 && sentAc.Value != fileAc)
                {
                    Logger.Info($"GTLGo: AC TDP of {cur.GameId.Name} changed in the widget: {fileAc}W -> {sentAc.Value}W (saved)");
                    cur.TDP = sentAc.Value;
                }
                if (sentDc.HasValue && sentDc.Value > 0 && sentDc.Value != fileDc)
                {
                    Logger.Info($"GTLGo: DC TDP of {cur.GameId.Name} changed in the widget: {fileDc}W -> {sentDc.Value}W (saved)");
                    cur.TDP_DC = sentDc.Value;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"GTLGo: UseProfileFileTdp: {ex.Message}");
            }
        }

        /// <summary>
        /// GTLGo: the per-state TDP was applied only on AC/DC transitions, so a game started on
        /// AC got its profile's single saved TDP (the DC value, 20 W) instead of its AC value
        /// (25 W; Legion, 2026-11-15 09:25). Each time the widget sends the active profile's
        /// AC/DC values, apply the one for the current power state if it differs.
        /// </summary>
        private static void ReconcileTdpWithPowerSource()
        {
            try
            {
                if (performanceManager == null || performanceManager.IsAutoTDPActive)
                {
                    return;
                }
                if (defaultGameProfileManager != null && defaultGameProfileManager.ProfileEnabled.Value)
                {
                    return;
                }
                if (legionManager == null || legionManager.CurrentPerformanceMode != 255)
                {
                    return;
                }
                bool isOnAC = global::Windows.System.Power.PowerManager.PowerSupplyStatus
                              != global::Windows.System.Power.PowerSupplyStatus.NotPresent;
                bool? perStateAutoTdp = isOnAC ? PowerSourceProfileState.AcAutoTdp : PowerSourceProfileState.DcAutoTdp;
                if (perStateAutoTdp == true)
                {
                    return;
                }
                int? wanted = isOnAC ? PowerSourceProfileState.AcTdp : PowerSourceProfileState.DcTdp;
                if (!wanted.HasValue || wanted.Value <= 0 || wanted.Value == performanceManager.TDP.Value)
                {
                    return;
                }
                Logger.Info($"GTLGo: TDP {performanceManager.TDP.Value}W -> {wanted.Value}W for the current power state ({(isOnAC ? "AC" : "DC")})");
                performanceManager.TDP.SetValue(wanted.Value);
                performanceManager.SetTDP(wanted.Value);
            }
            catch (Exception ex)
            {
                Logger.Error($"GTLGo: ReconcileTdpWithPowerSource: {ex.Message}");
            }
        }

        private static void SystemManager_PowerSourceChanged(object sender, global::Windows.System.Power.PowerSupplyStatus newStatus)
        {
            // Match the widget's interpretation (GamingWidget.PowerSourceEvents.cs:70 and
            // GamingWidget.xaml.cs:2742, :2777): "Inadequate" means a charger is connected
            // but can't keep up with current load — common with the Legion Go's stock
            // USB-C charger under heavy draw. Treat that as AC, not battery, so we match
            // what the user expects when they have something physically plugged in.
            // Only NotPresent (truly unplugged) means DC.
            bool isOnAC = newStatus != global::Windows.System.Power.PowerSupplyStatus.NotPresent;

            // Short-circuit when isOnAC didn't actually change. SystemManager fires
            // PowerSourceChanged on every PowerSupplyStatus transition (Adequate ↔
            // Inadequate ↔ NotPresent), but our work below — power plan switch and
            // TDP reapply — only depends on the AC/DC boolean. On a flaky charger
            // (Inadequate ↔ Adequate flapping) we'd otherwise re-push TDP dozens of
            // times in a few minutes for no behavioral change.
            if (_lastIsOnAC == isOnAC)
            {
                Logger.Debug($"Helper-side AC/DC handler: status {newStatus} but isOnAC unchanged ({isOnAC}); skipping plan/TDP work");
                return;
            }
            _lastIsOnAC = isOnAC;

            // Apply per-state values cached by the widget on AC/DC transitions.
            //
            // Layered gating, in order of how much they should restrict the work:
            //   - performanceManager null / AutoTDP active → skip everything (no manager
            //     to call, or AutoTDP overwrites TDP every tick anyway).
            //   - DGP active → skip everything; DGP curates all per-game settings.
            //   - TDP-specific gate (Legion Custom OR Power-Source-Profile auto-switch):
            //     only blocks TDP/TDPBoost reapply. In Legion preset modes the system
            //     manages TDP itself, so pushing our value would fight the preset.
            //   - Extended fields (CPUBoost / CPUEPP / CPUState / OSPowerMode / FPSLimit)
            //     are NOT gated by Legion mode — those settings work regardless of preset
            //     mode, and the per-property equality check below ensures we no-op when
            //     the user's AC and DC profiles are identical for a given field.
            //
            // Net effect: helper-side AC/DC apply does useful work whenever the user has
            // configured ANY AC/DC differences for the active profile, whether or not
            // they've also enabled the Power-Source-Profile power-plan auto-switch.
            try
            {
                if (performanceManager == null)
                {
                    return;
                }

                bool dgpActive = defaultGameProfileManager != null && defaultGameProfileManager.ProfileEnabled.Value;
                if (dgpActive)
                {
                    Logger.Debug("Helper-side AC/DC handler: skipping AC/DC apply — DGP is active");
                    return;
                }

                // 2-pre) Per-state AutoTDP (#94 item 3). MUST run before the
                // IsAutoTDPActive gate below: when AutoTDP is currently running and
                // the new power state wants it off, the old gate returned without
                // ever disabling it — an AC-on/DC-off split could never switch off
                // helper-side, and the widget-side switch only runs while the widget
                // is awake (Game Bar suspends it whenever the overlay closes).
                // Ordering rules (see RestoreGlobalProfileSettings):
                //  - disable AutoTDP and clear IsAutoTDPActive BEFORE the TDP apply
                //    below, or TDPProperty.NotifyPropertyChanged skips the hardware
                //    write;
                //  - ForceSetValue so the widget's toggle gets the pipe push even if
                //    a stale widget-side value matches.
                bool? perStateAutoTdp = isOnAC ? PowerSourceProfileState.AcAutoTdp : PowerSourceProfileState.DcAutoTdp;
                if (perStateAutoTdp.HasValue && autoTDPManager != null)
                {
                    int? perStateAutoTdpFps = isOnAC ? PowerSourceProfileState.AcAutoTdpFps : PowerSourceProfileState.DcAutoTdpFps;
                    if (perStateAutoTdp.Value && perStateAutoTdpFps.HasValue && perStateAutoTdpFps.Value > 0
                        && autoTDPManager.TargetFPS != null
                        && perStateAutoTdpFps.Value != autoTDPManager.TargetFPS.Value)
                    {
                        Logger.Info($"Helper-side AC/DC handler: applying AutoTDP target FPS {perStateAutoTdpFps.Value} from per-state {(isOnAC ? "AC" : "DC")} profile");
                        autoTDPManager.TargetFPS.SetValue(perStateAutoTdpFps.Value);
                    }

                    if (autoTDPManager.Enabled != null && perStateAutoTdp.Value != autoTDPManager.Enabled.Value)
                    {
                        Logger.Info($"Helper-side AC/DC handler: applying AutoTDP={perStateAutoTdp.Value} from per-state {(isOnAC ? "AC" : "DC")} profile");
                        autoTDPManager.Enabled.ForceSetValue(perStateAutoTdp.Value);
                    }

                    if (!perStateAutoTdp.Value && performanceManager.IsAutoTDPActive)
                    {
                        // Only the AutoTDP tick clears this; do it now so the TDP
                        // apply below isn't skipped.
                        performanceManager.IsAutoTDPActive = false;
                    }
                }

                if (performanceManager.IsAutoTDPActive)
                {
                    // AutoTDP stays active for this power state — it overwrites TDP
                    // every tick, so skip the static per-state applies below.
                    return;
                }

                bool isLegionCustomMode = legionManager != null && legionManager.CurrentPerformanceMode == 255;

                // TDP / TDPBoost — gated by Legion Custom mode; in Legion preset modes the
                // system manages TDP itself, so pushing our value would fight the preset.
                if (!isLegionCustomMode)
                {
                    Logger.Debug("Helper-side AC/DC handler: skipping TDP/TDPBoost reapply — not in Legion Custom mode (extended fields below still apply)");
                }
                else
                {
                    // Pick the per-state TDP value for the new power state, if the widget
                    // has configured one via PowerSourceProfileValues. Falls back to the
                    // helper's current cached TDP so behavior matches the pre-AC/DC-values
                    // build for users who haven't configured per-state values.
                    int? perStateTdp = isOnAC ? PowerSourceProfileState.AcTdp : PowerSourceProfileState.DcTdp;
                    int targetTdp = perStateTdp ?? performanceManager.TDP.Value;
                    if (targetTdp > 0)
                    {
                        string source = perStateTdp.HasValue ? $"per-state {(isOnAC ? "AC" : "DC")} profile" : "current cached value";
                        Logger.Info($"Helper-side AC/DC handler: applying TDP {targetTdp}W from {source} (legionCustom={isLegionCustomMode})");
                        // Update the helper's TDP property so the widget's slider stays
                        // in sync (the property change pipes back to the widget). Use
                        // SetValue rather than just calling SetTDP so the widget UI
                        // reflects the new value.
                        if (perStateTdp.HasValue && perStateTdp.Value != performanceManager.TDP.Value)
                        {
                            performanceManager.TDP.SetValue(targetTdp);
                        }
                        performanceManager.SetTDP(targetTdp);
                    }

                    // Apply per-state TDP Boost too. SetValue triggers the property's
                    // PropertyChanged handler which calls SetTDP again — that's fine, the
                    // debounce in PerformanceManager.SetTDP coalesces the calls.
                    bool? perStateBoost = isOnAC ? PowerSourceProfileState.AcTdpBoost : PowerSourceProfileState.DcTdpBoost;
                    if (perStateBoost.HasValue && performanceManager.TDPBoostEnabled != null)
                    {
                        if (perStateBoost.Value != performanceManager.TDPBoostEnabled.Value)
                        {
                            Logger.Info($"Helper-side AC/DC handler: applying TDPBoost={perStateBoost.Value} from per-state {(isOnAC ? "AC" : "DC")} profile");
                            performanceManager.TDPBoostEnabled.SetValue(perStateBoost.Value);
                        }
                    }
                }

                // Extended fields (build 2080+): CPUBoost / CPUEPP / OSPowerMode /
                // FPSLimit. Apply unconditionally — these settings aren't
                // managed by Legion preset modes, so they're safe to reapply anytime the
                // cached value differs from current. Skip silently when the cache has no
                // override (legacy widget that didn't pipe extended fields, or first
                // transition before widget had a chance to reconnect). null-coalescing on
                // each SetValue mirrors the TDP block above.
                string state = isOnAC ? "AC" : "DC";

                bool? perStateCpuBoost = isOnAC ? PowerSourceProfileState.AcCpuBoost : PowerSourceProfileState.DcCpuBoost;
                if (perStateCpuBoost.HasValue && powerManager?.CPUBoost != null
                    && perStateCpuBoost.Value != powerManager.CPUBoost.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying CPUBoost={perStateCpuBoost.Value} from per-state {state} profile");
                    powerManager.CPUBoost.SetValue(perStateCpuBoost.Value);
                }

                int? perStateCpuEpp = isOnAC ? PowerSourceProfileState.AcCpuEpp : PowerSourceProfileState.DcCpuEpp;
                if (perStateCpuEpp.HasValue && powerManager?.CPUEPP != null
                    && perStateCpuEpp.Value != powerManager.CPUEPP.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying CPUEPP={perStateCpuEpp.Value} from per-state {state} profile");
                    powerManager.CPUEPP.SetValue(perStateCpuEpp.Value);
                }

                int? perStateOsMode = isOnAC ? PowerSourceProfileState.AcOsPowerMode : PowerSourceProfileState.DcOsPowerMode;
                if (perStateOsMode.HasValue && powerManager?.OSPowerMode != null
                    && perStateOsMode.Value != powerManager.OSPowerMode.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying OSPowerMode={perStateOsMode.Value} from per-state {state} profile");
                    powerManager.OSPowerMode.SetValue(perStateOsMode.Value);
                }

                int? perStateFpsLimit = isOnAC ? PowerSourceProfileState.AcFpsLimit : PowerSourceProfileState.DcFpsLimit;
                if (perStateFpsLimit.HasValue && rtssManager?.FPSLimit != null
                    && perStateFpsLimit.Value != rtssManager.FPSLimit.Value)
                {
                    Logger.Info($"Helper-side AC/DC handler: applying FPSLimit={perStateFpsLimit.Value} from per-state {state} profile");
                    rtssManager.FPSLimit.SetValue(perStateFpsLimit.Value);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Helper-side AC/DC handler: reapply TDP threw: {ex.Message}");
            }
        }
    }
}
