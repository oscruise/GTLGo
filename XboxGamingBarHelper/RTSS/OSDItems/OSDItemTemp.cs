using System;
using System.Collections.Generic;
using System.Drawing;
using XboxGamingBarHelper.Performance;

namespace XboxGamingBarHelper.RTSS.OSDItems
{
    /// <summary>
    /// GTLGo: temperatura compacta del APU (la mayor entre CPU y GPU), con color por umbral.
    /// </summary>
    internal class OSDItemTemp : OSDItem
    {
        private readonly HardwareSensor cpuTemperatureSensor;
        private readonly HardwareSensor gpuTemperatureSensor;

        public OSDItemTemp(HardwareSensor cpuTemperatureSensor, HardwareSensor gpuTemperatureSensor) : base("TEMP", "Temp", Color.Orange)
        {
            this.cpuTemperatureSensor = cpuTemperatureSensor;
            this.gpuTemperatureSensor = gpuTemperatureSensor;
        }

        protected override List<OSDItemValue> GetValues(int osdLevel)
        {
            var osdItems = base.GetValues(osdLevel);
            float cpu = cpuTemperatureSensor?.Value ?? -1;
            float gpu = gpuTemperatureSensor?.Value ?? -1;
            float t = Math.Max(cpu, gpu);
            osdItems.Add(new OSDItemValue(t > 0 ? t : -1, "C", OSDValueType.Temperature));
            return osdItems;
        }
    }
}
