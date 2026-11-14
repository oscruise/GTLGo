using System.Linq;

namespace Shared.Utilities
{
    public static class StringHelper
    {
        public static string CleanStringForSerialization(string input)
        {
            if (input == null)
                return null;

            // Remove XML-invalid control characters:
            // anything < 0x20 except TAB (0x09), LF (0x0A), CR (0x0D)
            return new string(input.Where(c =>
                c == '\t' || c == '\n' || c == '\r' || c >= ' '
            ).ToArray());
        }

        /// <summary>
        /// Normalizes a game display name for use as a profile key. Some titles
        /// inject zero-width/format code points (U+200B ZERO WIDTH SPACE, U+FEFF,
        /// directional marks) into their window titles — and can vary them per
        /// launch — so a name-keyed profile silently stops matching an identical-
        /// looking name. Strips all Unicode format characters (category Cf),
        /// converts non-standard space separators to a plain space, collapses
        /// whitespace runs, and trims.
        /// </summary>
        public static string CleanGameName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == System.Globalization.UnicodeCategory.Format)
                    continue;
                sb.Append(cat == System.Globalization.UnicodeCategory.SpaceSeparator ? ' ' : c);
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
        }

        // ---- GTLGo (Legion Go): nombre de juego estable a partir del titulo de ventana ----

        static readonly string[] EmulatorBrands =
        {
            "lichii", "fritchii", "eden", "citron", "yuzu", "suyu", "sudachi", "torzu", "ryujinx",
            "dolphin", "cemu", "rpcs3", "pcsx2", "duckstation", "ppsspp", "xemu", "xenia", "vita3k",
            "shadps4", "azahar", "citra", "lime3ds", "melonds", "retroarch",
        };

        static readonly System.Text.RegularExpressions.Regex NoiseSegment = new System.Text.RegularExpressions.Regex(
            @"^(v?\d+(\.\d+)+[a-z0-9\-\.]*|v\d+[\w\.\-]*|(msvc|clang|gnu|gcc|llvm)\b.*|(amd|nvidia|intel|qualcomm)\b.*|vulkan|opengl|d3d1[12]|directx\s*1[12]|\(?\d{2}-bit\)?|x64|x86|arm64|release|debug|(\d+(\.\d+)?\s*(fps|ms|%))|\[?[0-9a-f]{16}\]?)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// Convierte el titulo de la ventana en un nombre de juego estable para usarlo como perfil.
        /// Los emuladores ponen en el titulo su nombre, version, compilador y GPU
        /// ("Fritchii | v4.1 | Clang 22.1.8 | The Legend of Zelda: Ocarina of Time (64-bit) | AMD"),
        /// y eso cambiaba con cada actualizacion, rompiendo el perfil. Aqui se queda solo el juego:
        /// "The Legend of Zelda: Ocarina of Time". Los titulos sin separadores (juegos de PC) solo se limpian.
        /// Devuelve "" si el titulo no tiene juego (emulador sin juego abierto).
        /// </summary>
        public static string GameNameFromTitle(string title)
        {
            title = CleanGameName(title);
            if (string.IsNullOrEmpty(title))
                return title;

            // "Ryujinx 1.2.3 - Juego v1.0 (0100...) (64-bit)", "DuckStation 0.1-9000 - Juego": marca y version antes del " - "
            int dash = title.IndexOf(" - ", System.StringComparison.Ordinal);
            if (dash > 0 && title.IndexOf('|') < 0)
            {
                string head = title.Substring(0, dash).Trim().ToLowerInvariant();
                if (System.Array.IndexOf(EmulatorBrands, head.Split(' ')[0]) >= 0)
                {
                    string rest = title.Substring(dash + 3);
                    rest = System.Text.RegularExpressions.Regex.Replace(rest, @"\s+v?\d+(\.\d+)+\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    rest = System.Text.RegularExpressions.Regex.Replace(rest, @"\s*[\(\[](\d{2}-bit|[0-9a-f]{16})[\)\]]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    return CleanGameName(rest);
                }
            }

            string[] parts = title.Split('|');
            bool isEmulatorTitle = false;
            var kept = new System.Collections.Generic.List<string>();
            foreach (var raw in parts)
            {
                string seg = raw.Trim();
                if (seg.Length == 0)
                    continue;
                string low = seg.ToLowerInvariant();
                // Marca del emulador, con o sin version pegada ("Fritchii v5.8.4", "Ryujinx 1.2.3")
                string firstWord = low.Split(' ')[0];
                if (System.Array.IndexOf(EmulatorBrands, firstWord) >= 0 && (seg.IndexOf(' ') < 0 || NoiseSegment.IsMatch(seg.Substring(seg.IndexOf(' ') + 1).Trim())))
                {
                    isEmulatorTitle = true;
                    continue;
                }
                if (NoiseSegment.IsMatch(seg))
                {
                    isEmulatorTitle = true;
                    continue;
                }
                kept.Add(seg);
            }

            if (!isEmulatorTitle && parts.Length < 3)
            {
                // Titulo normal (juego de PC): se usa tal cual
                return title;
            }

            if (kept.Count == 0)
                return "";

            // El juego es el segmento mas largo de lo que queda
            string game = kept.OrderByDescending(s => s.Length).First();
            // Quitar "(64-bit)", "[0100...]" y versiones al final
            game = System.Text.RegularExpressions.Regex.Replace(game, @"\s*[\(\[](\d{2}-bit|[0-9a-f]{16}|v?\d+(\.\d+)+)[\)\]]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return CleanGameName(game);
        }
    }
}
