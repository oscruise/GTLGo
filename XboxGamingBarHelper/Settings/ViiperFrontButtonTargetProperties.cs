using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.Settings
{
    /// <summary>
    /// Which emulated-controller button the Legion "Desktop" front button acts as while
    /// controller emulation is active. String value: none|guide|touchpad|share|options|l3|r3.
    /// Resolved per emulated type in the VIIPER forwarder. Default "guide" matches the prior
    /// hardcoded behavior (Desktop -> PS/Guide).
    /// </summary>
    internal class ViiperDesktopButtonTargetProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "guide";
        private const string SettingsKey = "ViiperDesktopButtonTarget";

        public ViiperDesktopButtonTargetProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_DesktopButtonTarget, inManager)
        {
            Logger.Info($"ViiperDesktopButtonTarget loaded: {Value}");
        }

        private static string LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<string>(SettingsKey, out var value) && !string.IsNullOrEmpty(value))
                return value;
            return Default;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ViiperDesktopButtonTarget changed: {Value}");
            LocalSettingsHelper.SetValue(SettingsKey, Value ?? Default);
        }
    }

    /// <summary>
    /// Which emulated-controller button the Legion "Page" front button acts as while controller
    /// emulation is active. String value: none|guide|touchpad|share|options|l3|r3. Default
    /// "touchpad" matches the prior hardcoded behavior (Page -> Touchpad click).
    /// </summary>
    internal class ViiperPageButtonTargetProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "touchpad";
        private const string SettingsKey = "ViiperPageButtonTarget";

        public ViiperPageButtonTargetProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_PageButtonTarget, inManager)
        {
            Logger.Info($"ViiperPageButtonTarget loaded: {Value}");
        }

        private static string LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<string>(SettingsKey, out var value) && !string.IsNullOrEmpty(value))
                return value;
            return Default;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ViiperPageButtonTarget changed: {Value}");
            LocalSettingsHelper.SetValue(SettingsKey, Value ?? Default);
        }
    }

    /// <summary>
    /// GTLGo: Legion back button that acts as the emulated controller's Guide/PS button while
    /// controller emulation is active (none|y1|y2|y3|m2|m3). Lets RetroBat use Guide as a
    /// dedicated hotkey so Select stays free. Default "y2" (the user's choice).
    /// </summary>
    internal class ViiperBackGuideButtonProperty : HelperProperty<string, SettingsManager>
    {
        public const string Default = "y2";
        private const string SettingsKey = "ViiperBackGuideButton";

        public ViiperBackGuideButtonProperty(SettingsManager inManager)
            : base(LoadFromSettings(), null, Function.Viiper_BackGuideButton, inManager)
        {
            Logger.Info($"ViiperBackGuideButton loaded: {Value}");
        }

        private static string LoadFromSettings()
        {
            if (LocalSettingsHelper.TryGetValue<string>(SettingsKey, out var value) && !string.IsNullOrEmpty(value))
                return value;
            return Default;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Logger.Info($"ViiperBackGuideButton changed: {Value}");
            LocalSettingsHelper.SetValue(SettingsKey, Value ?? Default);
        }
    }
}
