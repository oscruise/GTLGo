using Shared.Data;
using Shared.Enums;
using System;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.OnScreenDisplay
{
    internal class OnScreenDisplayProperty : HelperProperty<int, OnScreenDisplayManager>
    {
        public OnScreenDisplayProperty(int inValue, IProperty inParentProperty, OnScreenDisplayManager inManager) : base(LoadSavedLevel(inValue), inParentProperty, Function.OSD, inManager)
        {
            // Apply the loaded level to the manager
            manager?.SetLevel(Value);
            Logger.Info($"OSD level initialized to {Value} (loaded from settings)");
        }

        private static int LoadSavedLevel(int defaultValue)
        {
            // GTLGo: al arrancar Windows se usa el nivel del perfil general (el ultimo usado sin juego),
            // no el ultimo nivel usado: antes, si se apagaba la Legion durante un juego con overlay
            // detallado, el sistema arrancaba con el overlay detallado.
            try
            {
                string f = System.IO.Path.Combine(XboxGamingBarHelper.Profile.ProfileManager.LocalFolderPath, GlobalLevelFile);
                if (System.IO.File.Exists(f) && int.TryParse(System.IO.File.ReadAllText(f).Trim(), out int g) && g >= 0 && g <= 3)
                {
                    Logger.Info($"GTLGo: OSD level {g} from the global profile");
                    return g;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"GTLGo: could not read global OSD level: {ex.Message}");
            }
            try
            {
                int savedLevel = Properties.Settings.Default.OSDLevel;
                if (savedLevel >= 0 && savedLevel <= 3)
                {
                    Logger.Info($"Loaded OSD level {savedLevel} from settings");
                    return savedLevel;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load OSD level from settings: {ex.Message}");
            }
            return defaultValue;
        }

        const string GlobalLevelFile = "gtlgo_osd_global.txt";
        static readonly DateTime HelperStartUtc = DateTime.UtcNow;

        /// <summary>GTLGo: nivel de overlay guardado para el perfil general, o null si no hay.</summary>
        internal static int? ReadGlobalLevel()
        {
            try
            {
                string f = System.IO.Path.Combine(XboxGamingBarHelper.Profile.ProfileManager.LocalFolderPath, GlobalLevelFile);
                if (System.IO.File.Exists(f) && int.TryParse(System.IO.File.ReadAllText(f).Trim(), out int g) && g >= 0 && g <= 3)
                    return g;
            }
            catch (Exception ex)
            {
                Logger.Warn($"GTLGo: could not read global OSD level: {ex.Message}");
            }
            return null;
        }

        private void SaveLevel(int level)
        {
            try
            {
                var pm = XboxGamingBarHelper.Profile.ProfileManager.Instance;
                // Los primeros segundos tras arrancar el helper el widget sincroniza valores por defecto
                // (nivel 0) antes de cargar su perfil; no tomarlos como el nivel del perfil general.
                if (pm != null && pm.CurrentProfile.IsGlobalProfile && (DateTime.UtcNow - HelperStartUtc).TotalSeconds > 45)
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(XboxGamingBarHelper.Profile.ProfileManager.LocalFolderPath, GlobalLevelFile), level.ToString());
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"GTLGo: could not save global OSD level: {ex.Message}");
            }
            try
            {
                Properties.Settings.Default.OSDLevel = level;
                Properties.Settings.Default.Save();
                Logger.Debug($"Saved OSD level {level} to settings");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to save OSD level to settings: {ex.Message}");
            }
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            manager?.SetLevel(Value);
            SaveLevel(Value);
        }

        public virtual void ChangeManager(OnScreenDisplayManager inManager)
        {
            if (inManager == null)
            {
                Logger.Warn($"Can't change On-Screen Display's manager to null.");
                return;
            }

            if (inManager == manager)
            {
                Logger.Warn($"On-Screen Display's manager is already the same instance.");
                return;
            }

            // Before changing manager, set the previous manager's level to 0 to clean the old OSD.
            manager.SetLevel(0);
            manager.IsInUsed = false;
            manager = inManager;
            manager.SetLevel(Value);
            manager.IsInUsed = true;
        }
    }
}
