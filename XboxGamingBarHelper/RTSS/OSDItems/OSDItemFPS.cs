using System.Drawing;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    internal class OSDItemFPS : OSDItem
    {
        public OSDItemFPS() : base("FPS", "FPS", Color.Red)
        {
        }

        public override string GetOSDString(int osdLevel)
        {
            // FPS and frametime - uses text color, yellow for frametime then back to text color
            // Apply opacity to all colors for OLED protection
            if (LegionStyle)
            {
                return BuildLegionFps();
            }

            var tc = GetTextColorWithOpacity();
            var yellow = ApplyOpacity("FFFF00");

            // #66: when PresentMon is supplying live frame stats AND it sees a
            // frame-generated source (AMD AFMF / Intel XeSS-FG / Lossless
            // Scaling), render a "rendered / displayed" pair using the literal
            // values. RTSS prints whatever string we hand it. Otherwise fall
            // back to the RTSS &lt;FR&gt; substitution, which still works for
            // non-AFMF games and as a safety net when PresentMon is
            // unavailable.
            var pm = Program.PresentMonMetrics;
            // Match PresentMonMetrics' hold window (3 s): PresentMon delivers frames in
            // bursts, so a 2 s gate would flap to the RTSS <FR> fallback between bursts.
            if (pm != null && pm.IsLive(3000))
            {
                int rendered = pm.RenderedFps;
                int displayed = pm.DisplayedFps;
                int afmf = pm.AfmfFps;
                // Both numbers are FPS, not panel Hz — rendered is what the
                // game submits, displayed is the rate at which unique
                // buffer changes hit the swap chain (including FG-generated
                // frames). Using "Hz" here confuses users into thinking it
                // means the panel refresh rate.
                if (afmf > 0 && displayed > rendered)
                {
                    // [FG] badge tells the user the displayed number is
                    // frame-generated (AMD AFMF / Intel XeSS-FG / Lossless
                    // Scaling re-tagged here), not a real render rate.
                    return $"<C={tc}>{rendered} / {displayed} fps <C={yellow}>[FG] <FT> ms<C={tc}>";
                }
                if (rendered > 0)
                {
                    return $"<C={tc}>{rendered} fps <C={yellow}><FT> ms<C={tc}>";
                }
            }
            return $"<C={tc}><FR> FPS <C={yellow}><FT> ms<C={tc}>";
        }

        // GTLGo: "FPS  60 fps  16.6 ms" con número grande alineado y frametime en ámbar suave
        private string BuildLegionFps()
        {
            string value = "<FR>";
            string fg = "";
            var pm = Program.PresentMonMetrics;
            if (pm != null && pm.IsLive(3000))
            {
                int rendered = pm.RenderedFps;
                int displayed = pm.DisplayedFps;
                if (pm.AfmfFps > 0 && displayed > rendered)
                {
                    value = $"{rendered}/{displayed}";
                    fg = $" {Sz(62)}<C={ApplyOpacity("F5A15A")}>FG{SzReset()}";
                }
                else if (rendered > 0)
                {
                    value = rendered.ToString();
                }
            }
            string white = ApplyOpacity("FFFFFF");
            string amber = ApplyOpacity("F5D76E");
            return $"{GetNameString()} <C={white}>{LegionNumber(value, 3)}{LegionUnit("fps")}{fg} <C={amber}><A=-4><FT><A>{LegionUnit("ms")}<C={GetTextColorWithOpacity()}>";
        }
    }
}
