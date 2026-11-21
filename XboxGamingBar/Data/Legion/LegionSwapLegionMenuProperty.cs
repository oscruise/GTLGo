using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// GTLGo: swap Legion L/R with View/Menu (firmware). The helper reapplies it on every reconnect.
    /// </summary>
    internal class LegionSwapLegionMenuProperty : WidgetToggleProperty
    {
        public LegionSwapLegionMenuProperty(ToggleSwitch inUI, Page inOwner) : base(true, Function.LegionSwapLegionMenu, inUI, inOwner)
        {
        }
    }
}
