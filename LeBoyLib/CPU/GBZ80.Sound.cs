using System;

namespace LeBoyLib
{
    /// <summary>
    /// Emulates a Z80 Gameboy CPU, more specifically a Sharp LR35902 which is a Z80 minus a few instructions, with more logical operations and a sound generator.
    /// </summary>
    public partial class GBZ80
    {
        // The GB has 4 stereo channels
        // Ch1: Quadrangular wave patterns with sweep and envelope functions.
        // Ch2: Quadrangular wave patterns with envelope functions.
        // Ch3: Voluntary wave patterns from wave RAM (4 bits per sample).
        // Ch4: White noise with an envelope function.


        private void Sound_Step()
        {


            // Ch2
            /*
            byte NR21 = Memory[0xFF16];
            float duty = (((NR21 & 0xC0) >> 6) + 1) / 8.0f;
            int t1 = NR21 & 0x3F;

            byte NR22 = Memory[0xFF17];
            float initialVolume = ((NR22 & 0xF0) >> 4) / 15.0f;
            bool increasing = (NR22 & 0x8) != 0;
            int sweeps = NR22 & 0x7;
            */

            // Ch4
            byte NR41 = Memory[0xFF20];
            byte NR42 = Memory[0xFF21];
            byte NR43 = Memory[0xFF22];
            byte NR44 = Memory[0xFF23];

            if ((NR44 & 0x80) != 0)
            {
                Console.WriteLine("boop");
                Memory[0xFF23] = (byte)(NR44 & ~0x80);
            }
        }

        private double PulseWave(double time, double frequency, double duty, double amplitude)
        {
            double period = 1.0 / frequency;
            double timeModulusPeriod = time - Math.Floor(time / period) * period;
            double phase = timeModulusPeriod / period;
            if (phase <= duty)
                return amplitude;
            else
                return -amplitude;
        }
    }
}
