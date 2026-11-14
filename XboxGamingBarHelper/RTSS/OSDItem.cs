using System;
using System.Collections.Generic;
using System.Drawing;

namespace XboxGamingBarHelper.RTSS
{
    internal abstract class OSDItem
    {
        protected string name;
        protected string id;
        protected string colorCode;
        protected string defaultColorCode;  // Store the original color
        protected string textColor = "FFFFFF";
        protected bool useDynamicColor = false;
        protected int opacity = 100;  // OLED protection opacity (10-100)

        public string Id => id;

        // ===== GTLGo: estilo "Legion" (solo diseño; mismos datos y mismo RTSS) =====
        internal static bool LegionStyle = true;
        internal static int BaseSize = 100;        // tamaño base del overlay en % (TextSize del widget)
        internal static int LabelColumnCells = 0;  // >0 en listas verticales: columna fija de etiquetas
        internal const string LegionDim = "8A93A3";   // unidades y separadores
        internal const string LegionValue = "EEF1F5"; // valores neutros

        private static readonly Dictionary<string, string> LegionLabels = new Dictionary<string, string>
        {
            { "FPS", "5CD6FF" }, { "AppName", "5CD6FF" },
            { "CPU", "7FB6FF" }, { "GPU", "FF9F5A" },
            { "Memory", "C792EA" }, { "VRAM", "B39DDB" },
            { "Battery", "8BE38B" }, { "ControllerBattery", "8BE38B" },
            { "Fan", "9AA5B1" }, { "Temp", "F5A15A" }, { "NET", "9AA5B1" }, { "Time", "B0B8C4" },
            { "FrameBudget", "F2C14E" }, { "AutoTDP", "F2C14E" }, { "TDPLimits", "F2C14E" },
        };

        // Colores neón originales -> paleta suave del estilo Legion
        private static readonly Dictionary<string, string> LegionSoft = new Dictionary<string, string>
        {
            { "00FF00", "7CE38B" }, { "80FF00", "B5E36E" }, { "FFFF00", "F5D76E" },
            { "FF8000", "F5A15A" }, { "FF0000", "F2645A" }, { "0080FF", "6FA8FF" }, { "00FF80", "6EE3C0" },
        };

        protected static string Sz(int pct) => $"<S={Math.Max(30, BaseSize * pct / 100)}>";
        protected static string SzReset() => BaseSize == 100 ? "<S>" : $"<S={BaseSize}>";

        protected string LegionLabelColor()
        {
            // Si el usuario eligió un color de etiqueta en el widget, se respeta
            if (colorCode != defaultColorCode) return colorCode;
            return LegionLabels.TryGetValue(id ?? "", out var c) ? c : colorCode;
        }

        /// <summary>Unidad pequeña y tenue ("fps", "%", "W", "GB").</summary>
        protected string LegionUnit(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return "";
            string u = unit.Trim();
            if (u == "C") u = "°C";
            else if (u == "W/H") u = "W";
            return $"{Sz(62)}<C={ApplyOpacity(LegionDim)}>{u}{SzReset()}";
        }

        /// <summary>Número en caja alineada a la derecha para que no "brinque" al cambiar de dígitos.</summary>
        protected static string LegionNumber(string text, int minCells)
        {
            int w = Math.Max(minCells, text.Length);
            return $"<A=-{w}>{text}<A>";
        }

        public void SetLabelColor(string color)
        {
            if (!string.IsNullOrEmpty(color) && color != "DEFAULT")
            {
                colorCode = color;
            }
            else
            {
                // Reset to default color
                colorCode = defaultColorCode;
            }
        }

        public void SetTextColor(string color)
        {
            if (color == "DYNAMIC")
            {
                useDynamicColor = true;
                textColor = "FFFFFF"; // Default fallback
            }
            else
            {
                useDynamicColor = false;
                textColor = color;
            }
        }

        public void SetOpacity(int opacityValue)
        {
            opacity = Math.Max(10, Math.Min(100, opacityValue));
        }

        /// <summary>
        /// Gets the text color with opacity applied.
        /// Use this in custom GetOSDString implementations instead of textColor directly.
        /// </summary>
        protected string GetTextColorWithOpacity()
        {
            // When using dynamic color, textColor is just "FFFFFF" without opacity
            // When not using dynamic color, textColor already has opacity from RTSSManager
            return useDynamicColor ? ApplyOpacity(textColor) : textColor;
        }

        /// <summary>
        /// Applies opacity to a hex color for OLED protection.
        /// </summary>
        protected string ApplyOpacity(string hexColor)
        {
            if (opacity >= 100 || string.IsNullOrEmpty(hexColor) || hexColor.Length < 6)
                return hexColor;

            try
            {
                float factor = opacity / 100f;
                byte r = (byte)(Convert.ToByte(hexColor.Substring(0, 2), 16) * factor);
                byte g = (byte)(Convert.ToByte(hexColor.Substring(2, 2), 16) * factor);
                byte b = (byte)(Convert.ToByte(hexColor.Substring(4, 2), 16) * factor);
                return $"{r:X2}{g:X2}{b:X2}";
            }
            catch
            {
                return hexColor;
            }
        }

        protected OSDItem()
        {
            name = "OSD Item";
            id = "Unknown";
            colorCode = "FFFFFF";
            defaultColorCode = "FFFFFF";
        }

        protected OSDItem(string name, Color color) : this(name, name, color)
        {
        }

        protected OSDItem(string name, string id, Color color)
        {
            this.name = name;
            this.id = id;
            this.colorCode = $"{color.R:X2}{color.G:X2}{color.B:X2}";
            this.defaultColorCode = this.colorCode;  // Store the default
        }

        public virtual string GetOSDString(int osdLevel)
        {
            var osdValues = GetValues(osdLevel);

            if (osdValues == null || osdValues.Count == 0)
            {
                return string.Empty;
            }

            if (LegionStyle)
            {
                return BuildLegionString(osdValues);
            }

            var tc = GetTextColorWithOpacity();
            var osdString = $"{GetNameString()} ";

            if (osdValues == null || osdValues.Count == 0)
            {
                return osdString + " N/A";
            }

            for (int i = 0; i < osdValues.Count; i++)
            {
                var osdValue = osdValues[i];
                if (osdValue.Value < 0)
                {
                    osdString += $"<C={tc}>N/A";
                }
                else
                {
                    var valueColor = GetValueColor(osdValue);
                    osdString += $"<C={valueColor}>{osdValue.Prefix}{osdValue.FormattedValue}{osdValue.Unit}";
                }
                if (i < osdValues.Count - 1)
                {
                    osdString += " ";
                }
            }

            // Reset to text color at end
            osdString += $"<C={tc}>";

            return osdString;
        }

        private string BuildLegionString(List<OSDItemValue> osdValues)
        {
            var sb = new System.Text.StringBuilder();
            string nameStr = GetNameString();
            if (!string.IsNullOrEmpty(nameStr)) sb.Append(nameStr).Append(' ');
            for (int i = 0; i < osdValues.Count; i++)
            {
                var v = osdValues[i];
                if (v.Value < 0)
                {
                    sb.Append($"<C={ApplyOpacity(LegionDim)}>--");
                }
                else
                {
                    int cells = 0;
                    switch (v.ValueType)
                    {
                        case OSDValueType.Percentage:
                        case OSDValueType.PercentageInv: cells = 3; break;
                        case OSDValueType.Temperature:
                        case OSDValueType.Wattage: cells = 2; break;
                        case OSDValueType.Speed: cells = 4; break;
                    }
                    string text = v.Prefix + v.FormattedValue;
                    sb.Append($"<C={GetValueColor(v)}>");
                    sb.Append(cells > 0 ? LegionNumber(text, cells) : text);
                    sb.Append(LegionUnit(v.Unit));
                }
                if (i < osdValues.Count - 1 && v.Unit != "/" && !(v.Unit ?? "").EndsWith("/"))
                {
                    sb.Append(' ');
                }
            }
            sb.Append($"<C={GetTextColorWithOpacity()}>");
            return sb.ToString();
        }

        protected virtual string GetNameString()
        {
            if (LegionStyle)
            {
                string label = $"{Sz(68)}<C={ApplyOpacity(LegionLabelColor())}>{(name ?? "").ToUpperInvariant()}{SzReset()}<C={GetTextColorWithOpacity()}>";
                return LabelColumnCells > 0 ? $"<A={LabelColumnCells}>{label}<A>" : label;
            }
            return $"<C={ApplyOpacity(colorCode)}>{name}<C={GetTextColorWithOpacity()}>";
        }

        protected virtual List<OSDItemValue> GetValues(int osdLevel)
        {
            return new List<OSDItemValue>();
        }

        /// <summary>
        /// Gets the color for a value based on its type and the dynamic color setting.
        /// Applies opacity for OLED protection.
        /// </summary>
        protected string GetValueColor(OSDItemValue value)
        {
            if (LegionStyle)
            {
                // Estilo Legion: los valores con umbral siempre llevan color (RAM en rojo arriba de 85 %)
                string baseColor;
                switch (value.ValueType)
                {
                    case OSDValueType.Temperature: baseColor = GetTemperatureColor(value.Value); break;
                    case OSDValueType.Percentage: baseColor = GetPercentageColor(value.Value); break;
                    case OSDValueType.PercentageInv: baseColor = GetPercentageInvColor(value.Value); break;
                    case OSDValueType.Wattage: baseColor = GetWattageColor(value.Value); break;
                    default: baseColor = LegionValue; break;
                }
                if (LegionSoft.TryGetValue(baseColor, out var soft)) baseColor = soft;
                return ApplyOpacity(baseColor);
            }

            if (!useDynamicColor || value.ValueType == OSDValueType.None)
            {
                // When not using dynamic color, textColor already has opacity from RTSSManager
                // When using dynamic color but value type is None, apply opacity to default white
                return useDynamicColor ? ApplyOpacity(textColor) : textColor;
            }

            var dynamicColor = value.ValueType switch
            {
                OSDValueType.Temperature => GetTemperatureColor(value.Value),
                OSDValueType.Percentage => GetPercentageColor(value.Value),
                OSDValueType.PercentageInv => GetPercentageInvColor(value.Value),
                OSDValueType.Wattage => GetWattageColor(value.Value),
                OSDValueType.Speed => textColor, // Speed doesn't change color
                _ => textColor
            };

            return ApplyOpacity(dynamicColor);
        }

        /// <summary>
        /// Temperature color: Blue (cold) -> Green (normal) -> Yellow (warm) -> Red (hot)
        /// </summary>
        private string GetTemperatureColor(float temp)
        {
            if (temp < 45) return "0080FF";      // Blue - cold
            if (temp < 55) return "00FF80";      // Cyan-green - cool
            if (temp < 65) return "00FF00";      // Green - normal
            if (temp < 75) return "80FF00";      // Yellow-green - getting warm
            if (temp < 80) return "FFFF00";      // Yellow - warm
            if (temp < 85) return "FF8000";      // Orange - hot
            return "FF0000";                      // Red - very hot
        }

        /// <summary>
        /// Percentage color (for usage): Green (low) -> Yellow (mid) -> Red (high)
        /// </summary>
        private string GetPercentageColor(float percent)
        {
            if (percent < 30) return "00FF00";   // Green - low usage
            if (percent < 50) return "80FF00";   // Yellow-green
            if (percent < 70) return "FFFF00";   // Yellow - moderate
            if (percent < 85) return "FF8000";   // Orange - high
            return "FF0000";                      // Red - very high
        }

        /// <summary>
        /// Inverted percentage color (for battery): Red (low) -> Yellow (mid) -> Green (high)
        /// </summary>
        private string GetPercentageInvColor(float percent)
        {
            if (percent < 15) return "FF0000";   // Red - critical
            if (percent < 30) return "FF8000";   // Orange - low
            if (percent < 50) return "FFFF00";   // Yellow - mid
            if (percent < 70) return "80FF00";   // Yellow-green
            return "00FF00";                      // Green - good
        }

        /// <summary>
        /// Wattage color: Green (low) -> Yellow (mid) -> Red (high)
        /// Based on typical handheld TDP ranges (5-30W)
        /// </summary>
        private string GetWattageColor(float watts)
        {
            if (watts < 8) return "00FF00";      // Green - low power
            if (watts < 15) return "80FF00";     // Yellow-green
            if (watts < 20) return "FFFF00";     // Yellow - moderate
            if (watts < 25) return "FF8000";     // Orange - high
            return "FF0000";                      // Red - very high
        }
    }
}
