using System;

namespace LeBoyLib
{
    /// <summary>
    /// Emulates a Z80 Gameboy CPU, more specifically a Sharp LR35902 which is a Z80 minus a few instructions, with more logical operations and a sound generator.
    /// </summary>
    public partial class GBZ80
    {
        public const int SPUSampleRate = 44100;

        // The GB has 4 stereo channels
        // Ch1: Quadrangular wave patterns with sweep and envelope functions.

        double Channel1Cycles = 0.0;

        double Channel1Length = 0;
        int Channel1Coordinate = 0;

        double Channel1SweepTime = 0.0;

        double Channel1VolumeTime = 0.0;
        int Channel1Volume = 0;

        public short[] Channel1Buffer = new short[10000];
        public int Channel1Samples = 0;

        // Sound Channel 1 - Tone & Sweep
        private void Channel1_Step(uint cycles, double SO1Volume, double SO2Volume)
        {
            // check if Channel 1 can emit
            byte NR52 = Memory[0xFF26];
            if ((NR52 & 0b1000_0000) == 0) // all the SPU is disabled
            {
                SO1Volume = 0.0;
                SO2Volume = 0.0;
            }

            byte NR51 = Memory[0xFF25];
            if ((NR51 & 0b0000_0001) == 0) // Channel 1 SO1 is disabled
                SO1Volume = 0.0;
            if ((NR51 & 0b0001_0000) == 0) // Channel 1 SO2 is disabled
                SO2Volume = 0.0;

            byte NR10 = Memory[0xFF10]; // Sweep register (R/W)
            /*
            Bit 6-4 - Sweep Time
            Bit 3   - Sweep Increase/Decrease
                        0: Addition    (frequency increases)
                        1: Subtraction (frequency decreases)
            Bit 2-0 - Number of sweep shift (n: 0-7)
            */
            double sweepTime = ((NR10 & 0b0111_0000) >> 4) / 128.0;
            int sweepIncrease = (NR10 & 0b0000_1000);
            int sweepShift = (NR10 & 0b0000_0111);

            if (Memory.ResetChannel1Sweep)
            {
                Memory.ResetChannel1Sweep = false;
                Channel1SweepTime = 0.0;
            }

            byte NR11 = Memory[0xFF11]; // Channel 1 Sound Length/Wave Pattern Duty (R/W)
            /*
            Bit 7-6 - Wave Pattern Duty (Read/Write)
            Bit 5-0 - Sound length data (Write Only) (t1: 0-63)
            */

            double duty = 0.125;
            switch ((NR11 & 0b1100_0000) >> 6)
            {
                case 1: duty = 0.250; break;
                case 2: duty = 0.500; break;
                case 3: duty = 0.750; break;
            }

            double length = (64 - (NR11 & 0b0011_1111)) * (1.0 / 256.0);

            byte NR12 = Memory[0xFF12]; // Channel 1 Volume Envelope (R/W)
            /*
            Bit 7-4 - Initial Volume of envelope (0-0Fh) (0=No Sound)
            Bit 3   - Envelope Direction (0=Decrease, 1=Increase)
            Bit 2-0 - Number of envelope sweep (n: 0-7)
                      (If zero, stop envelope operation.)
            */

            int initialVolume = (NR12 & 0b1111_0000) >> 4;
            int direction = NR12 & 0b0000_1000;
            int sweepCount = NR12 & 0b0000_0111;

            if (Memory.ResetChannel1Volume)
            {
                // reset envelope
                Memory.ResetChannel1Volume = false;
                Channel1Volume = initialVolume;
                Channel1VolumeTime = 0.0;
            }

            byte NR13 = Memory[0xFF13]; // Channel 1 Frequency lo data (W)
            byte NR14 = Memory[0xFF14]; // Channel 1 Frequency hi data (R/W)

            // Bit 2-0 - Frequency's higher 3 bits (x) (Write Only)
            int frequency = NR13 | ((NR14 & 0b0000_0111) << 8);

            // Bit 7   - Initial (1=Restart Sound)     (Write Only)
            if (Memory.ResetChannel1Length)
            {
                // reset sound
                Memory.ResetChannel1Length = false;
                Channel1Length = length;
                Channel1Coordinate = 0;

                Memory[0xFF26] |= 0b0000_0001;
            }
            // Bit 6   - Counter/consecutive selection (Read/Write)
            //           (1 = Stop output when length in NR21 expires)
            int consecutive = (NR14 & 0b0100_0000);


            // Update synthetizer

            double time = cycles / ClockSpeed;

            // Update sweep
            if (sweepTime > 0.0)
            {
                Channel1SweepTime += time;

                while (Channel1SweepTime >= sweepTime)
                {
                    Channel1SweepTime -= sweepTime;

                    int delta = (int)(frequency / Math.Pow(2, sweepShift));
                    if (sweepIncrease == 0)
                        delta = -delta;
                    frequency = frequency + delta;
                }
            }

            // Update volume envelope
            if (sweepCount > 0)
            {
                Channel1VolumeTime += time;

                double stepInterval = sweepCount / 64.0;
                while (Channel1VolumeTime >= stepInterval)
                {
                    Channel1VolumeTime -= stepInterval;
                    if (direction > 0)
                        Channel1Volume++;
                    else
                        Channel1Volume--;

                    // clamp
                    if (Channel1Volume < 0)
                        Channel1Volume = 0;
                    if (Channel1Volume > 15)
                        Channel1Volume = 15;
                }
            }

            double amplitude = Channel1Volume / 15.0;

            // Generate wave
            Channel1Cycles += cycles;

            double cyclesPerSample = ClockSpeed / SPUSampleRate;

            if (Channel1Cycles >= cyclesPerSample)
            {
                Channel1Cycles -= cyclesPerSample;

                if (consecutive == 0 || Channel1Length >= 0.0)
                {
                    double period = 1.0 / (double)(131072 / (2048 - frequency));

                    // Get current x coordinate and compute current sample value. 
                    double x = Channel1Coordinate / (double)SPUSampleRate;
                    double sample = DutyWave(amplitude, x, period, duty);
                    // SO1 (right)
                    Channel1Buffer[Channel1Samples] = (short)(0.25 * sample * SO1Volume * short.MaxValue);
                    // SO2 (left)
                    Channel1Buffer[Channel1Samples + 1] = (short)(0.25 * sample * SO2Volume * short.MaxValue);

                    Channel1Coordinate = (Channel1Coordinate + 1) % SPUSampleRate;
                    Channel1Samples += 2;
                }
            }

            if (consecutive != 0 && Channel1Length >= 0.0)
            {
                Channel1Length -= time;
                if (Channel1Length < 0.0)
                    Memory[0xFF26] = (byte)(NR52 & 0b1111_1110);
            }
        }

        // Ch2: Quadrangular wave patterns with envelope functions.

        double Channel2Cycles = 0.0;

        double Channel2Length = 0;
        int Channel2Coordinate = 0;

        double Channel2VolumeTime = 0.0;
        int Channel2Volume = 0;

        public short[] Channel2Buffer = new short[10000];
        public int Channel2Samples = 0;

        // Sound Channel 2 - Tone
        private void Channel2_Step(uint cycles, double SO1Volume, double SO2Volume)
        {
            // check if Channel 2 can emit
            byte NR52 = Memory[0xFF26];
            if ((NR52 & 0b1000_0000) == 0) // all the SPU is disabled
            {
                SO1Volume = 0.0;
                SO2Volume = 0.0;
            }

            byte NR51 = Memory[0xFF25];
            if ((NR51 & 0b0000_0010) == 0) // Channel 2 SO1 is disabled
                SO1Volume = 0.0;
            if ((NR51 & 0b0010_0000) == 0) // Channel 2 SO2 is disabled
                SO2Volume = 0.0;

            // NR20 0xFF15 is unused

            byte NR21 = Memory[0xFF16]; // Channel 2 Sound Length/Wave Pattern Duty (R/W)
            /*
            Bit 7-6 - Wave Pattern Duty (Read/Write)
            Bit 5-0 - Sound length data (Write Only) (t1: 0-63)
            */

            double duty = 0.125;
            switch ((NR21 & 0b1100_0000) >> 6)
            {
                case 1: duty = 0.250; break;
                case 2: duty = 0.500; break;
                case 3: duty = 0.750; break;
            }

            double length = (64 - (NR21 & 0b0011_1111)) * (1.0 / 256.0);

            byte NR22 = Memory[0xFF17]; // Channel 2 Volume Envelope (R/W)
            /*
            Bit 7-4 - Initial Volume of envelope (0-0Fh) (0=No Sound)
            Bit 3   - Envelope Direction (0=Decrease, 1=Increase)
            Bit 2-0 - Number of envelope sweep (n: 0-7)
                      (If zero, stop envelope operation.)
            */

            int initialVolume = (NR22 & 0b1111_0000) >> 4;
            int direction = NR22 & 0b0000_1000;
            int sweepCount = NR22 & 0b0000_0111;

            if (Memory.ResetChannel2Volume)
            {
                // reset envelope
                Memory.ResetChannel2Volume = false;
                Channel2Volume = initialVolume;
                Channel2VolumeTime = 0.0;
            }
            
            byte NR23 = Memory[0xFF18]; // Channel 2 Frequency lo data (W)
            byte NR24 = Memory[0xFF19]; // Channel 2 Frequency hi data (R/W)

            // Bit 2-0 - Frequency's higher 3 bits (x) (Write Only)
            int frequency = NR23 | ((NR24 & 0b0000_0111) << 8);

            // Bit 7   - Initial (1=Restart Sound)     (Write Only)
            if (Memory.ResetChannel2Length)
            {
                // reset sound
                Memory.ResetChannel2Length = false;
                Channel2Length = length;
                Channel2Coordinate = 0;

                Memory[0xFF26] |= 0b0000_0010;
            }
            // Bit 6   - Counter/consecutive selection (Read/Write)
            //           (1 = Stop output when length in NR21 expires)
            int consecutive = (NR24 & 0b0100_0000);


            // Update synthetizer

            double time = cycles / ClockSpeed;

            // Update volume envelope
            if (sweepCount > 0)
            {
                Channel2VolumeTime += time;

                double stepInterval = sweepCount / 64.0;
                while (Channel2VolumeTime >= stepInterval)
                {
                    Channel2VolumeTime -= stepInterval;
                    if (direction > 0)
                        Channel2Volume++;
                    else
                        Channel2Volume--;

                    // clamp
                    if (Channel2Volume < 0)
                        Channel2Volume = 0;
                    if (Channel2Volume > 15)
                        Channel2Volume = 15;
                }
            }

            double amplitude = Channel2Volume / 15.0;

            // Generate wave
            Channel2Cycles += cycles;

            double cyclesPerSample = ClockSpeed / SPUSampleRate;

            if (Channel2Cycles >= cyclesPerSample)
            {
                Channel2Cycles -= cyclesPerSample;

                if (consecutive == 0 || Channel2Length >= 0.0)
                {
                    double period = 1.0 / (double)(131072 / (2048 - frequency));

                    // Get current x coordinate and compute current sample value. 
                    double x = Channel2Coordinate / (double)SPUSampleRate;
                    double sample = DutyWave(amplitude, x, period, duty);
                    // SO1 (right)
                    Channel2Buffer[Channel2Samples] = (short)(0.25 * sample * SO1Volume * short.MaxValue);
                    // SO2 (left)
                    Channel2Buffer[Channel2Samples + 1] = (short)(0.25 * sample * SO2Volume * short.MaxValue);

                    Channel2Coordinate = (Channel2Coordinate + 1) % SPUSampleRate;
                    Channel2Samples += 2;
                }
            }

            if (consecutive != 0 && Channel2Length >= 0.0)
            {
                Channel2Length -= time;
                if (Channel2Length < 0.0)
                    Memory[0xFF26] = (byte)(NR52 & 0b1111_1101);
            }
        }

        // Ch3: Voluntary wave patterns from wave RAM (4 bits per sample).

        double Channel3Cycles = 0.0;

        int Channel3Coordinate = 0;

        public short[] Channel3Buffer = new short[10000];
        public int Channel3Samples = 0;

        bool Channel3TickTock;

        private void Channel3_Step(uint cycles, double SO1Volume, double SO2Volume)
        {
            // check if Channel 3 can emit
            byte NR52 = Memory[0xFF26];
            if ((NR52 & 0b1000_0000) == 0) // all the SPU is disabled
            {
                SO1Volume = 0.0;
                SO2Volume = 0.0;
            }

            byte NR51 = Memory[0xFF25];
            if ((NR51 & 0b0000_0100) == 0) // Channel 3 SO1 is disabled
                SO1Volume = 0.0;
            if ((NR51 & 0b0100_0000) == 0) // Channel 3 SO2 is disabled
                SO2Volume = 0.0;

            byte NR30 = Memory[0xFF1A]; // Channel 3 Sound on/off (R/W)
            /*
            Bit 7 - Sound Channel 3 Off  (0=Stop, 1=Playback)  (Read/Write)
            */

            int playing = (NR30 & 0b1000_0000);

            byte NR31 = Memory[0xFF1B]; // Channel 3 Sound Length
            /*
            Bit 7-0 - Sound length (t1: 0 - 255)
            */

            double length = (256 - NR31) * (1.0 / 256.0);

            byte NR32 = Memory[0xFF1C]; // Channel 3 Select output level (R/W)
            /*
            Bits 6-5 - Select output level (Read/Write)
            */

            double level = 0.0;
            switch ((NR32 & 0b0110_0000) >> 5)
            {
                case 1: level = 1.0; break;
                case 2: level = 0.5; break;
                case 3: level = 0.25; break;
            }

            byte NR33 = Memory[0xFF1D]; // Channel 3 Frequency’s lower data (W)
            byte NR34 = Memory[0xFF1E]; // Channel 3 Frequency’s higher data (R/W)

            // Bit 2-0 - Frequency's higher 3 bits (x) (Write Only)
            int frequency = NR33 | ((NR34 & 0b0000_0111) << 8);

            // Update synthetizer

            double time = cycles / ClockSpeed;

            // Generate wave
            Channel3Cycles += cycles;

            double cyclesPerSample = ClockSpeed / SPUSampleRate;

            if (Channel3Cycles >= cyclesPerSample)
            {
                Channel3Cycles -= cyclesPerSample;

                double period = 1.0 / (double)(65536 / (2048 - frequency));
                int intervalSampleCount = (int)(period * SPUSampleRate);

                if (intervalSampleCount > 0 && playing != 0)
                {
                    

                    // Get current x coordinate and compute current sample value. 
                    int waveRamCoordinate = (int)(Channel3Coordinate / (double)intervalSampleCount * 16);
                    int waveDataSample = Channel3TickTock
                            ? (Memory[0xFF30 + waveRamCoordinate] & 0xF)
                            : ((Memory[0xFF30 + waveRamCoordinate] >> 4) & 0xF);
                    double sample = level * (waveDataSample - 7) / 15.0;
                    // SO1 (right)
                    Channel3Buffer[Channel3Samples] = (short)(0.25 * sample * SO1Volume * short.MaxValue);
                    // SO2 (left)
                    Channel3Buffer[Channel3Samples + 1] = (short)(0.25 * sample * SO2Volume * short.MaxValue);

                    Channel3Samples += 2;

                    Channel3Coordinate++;
                    if (Channel3Coordinate >= intervalSampleCount)
                    {
                        Channel3TickTock = !Channel3TickTock;
                        Channel3Coordinate = 0;
                    }
                }
            }
        }

        // Ch4: White noise with an envelope function.

        double Channel4Cycles = 0.0;

        double Channel4Length = 0;

        double Channel4VolumeTime = 0.0;
        int Channel4Volume = 0;

        short PolynomialState = 0x7F;

        double Channel4PolynomialCycles = 0.0;

        public short[] Channel4Buffer = new short[10000];
        public int Channel4Samples = 0;

        private void Channel4_Step(uint cycles, double SO1Volume, double SO2Volume)
        {
            // check if Channel 4 can emit
            byte NR52 = Memory[0xFF26];
            if ((NR52 & 0b1000_0000) == 0) // all the SPU is disabled
            {
                SO1Volume = 0.0;
                SO2Volume = 0.0;
            }

            byte NR51 = Memory[0xFF25];
            if ((NR51 & 0b0000_1000) == 0) // Channel 4 SO1 is disabled
                SO1Volume = 0.0;
            if ((NR51 & 0b1000_0000) == 0) // Channel 4 SO2 is disabled
                SO2Volume = 0.0;

            // NR40 0xFF15 is unused

            byte NR40 = Memory[0xFF20]; // Channel 4 Sound Length (R/W)
            /*
            Bit 5-0 - Sound length data (t1: 0-63)
            */

            double length = (64 - (NR40 & 0b0011_1111)) * (1.0 / 256.0);

            byte NR41 = Memory[0xFF21]; // Channel 4 Volume Envelope (R/W)
            /*
            Bit 7-4 - Initial Volume of envelope (0-0Fh) (0=No Sound)
            Bit 3   - Envelope Direction (0=Decrease, 1=Increase)
            Bit 2-0 - Number of envelope sweep (n: 0-7)
                      (If zero, stop envelope operation.)
            */

            int initialVolume = (NR41 & 0b1111_0000) >> 4;
            int direction = NR41 & 0b0000_1000;
            int sweepCount = NR41 & 0b0000_0111;

            if (Memory.ResetChannel4Volume)
            {
                // reset envelope
                Memory.ResetChannel4Volume = false;
                Channel4Volume = initialVolume;
                Channel4VolumeTime = 0.0;
            }

            byte NR43 = Memory[0xFF22]; // Channel 4 Polynomial Counter (R/W)
            /*
            Bit 7-4 - Shift Clock Frequency (s)
            Bit 3   - Counter Step/Width (0=15 bits, 1=7 bits)
            Bit 2-0 - Dividing Ratio of Frequencies (r)
            */

            double shiftClockFrequency = (NR43 & 0b1111_0000) >> 4;
            int counterStep = NR43 & 0b0000_1000;
            double dividingRatio = NR43 & 0b0000_0111;
            if (dividingRatio == 0.0)
                dividingRatio = 0.5;
            // Frequency = 524288 Hz / r / 2^(s+1) ;For r=0 assume r=0.5 instead
            double frequency = 524288.0 / dividingRatio / Math.Pow(2.0, shiftClockFrequency + 1.0);

            if (Memory.ResetChannel4Clock)
            {
                Memory.ResetChannel4Clock = false;
                PolynomialState = (short)(counterStep != 0 ? 0x7F : 0x7FFF);
                Channel4PolynomialCycles = 0.0;
            }

            byte NR44 = Memory[0xFF23]; // Channel 4 Counter/consecutive; Inital (R/W)

            // Bit 7   - Initial (1=Restart Sound)     (Write Only)
            if (Memory.ResetChannel4Length)
            {
                // reset sound
                Memory.ResetChannel4Length = false;
                Channel4Length = length;

                Memory[0xFF26] |= 0b0000_1000;
            }
            // Bit 6   - Counter/consecutive selection (Read/Write)
            //           (1 = Stop output when length in NR21 expires)
            int consecutive = (NR44 & 0b0100_0000);


            // Update synthetizer

            double time = cycles / ClockSpeed;

            // Update volume envelope
            if (sweepCount > 0)
            {
                Channel4VolumeTime += time;

                double stepInterval = sweepCount / 64.0;
                while (Channel4VolumeTime >= stepInterval)
                {
                    Channel4VolumeTime -= stepInterval;
                    if (direction > 0)
                        Channel4Volume++;
                    else
                        Channel4Volume--;

                    // clamp
                    if (Channel4Volume < 0)
                        Channel4Volume = 0;
                    if (Channel4Volume > 15)
                        Channel4Volume = 15;
                }
            }

            double amplitude = Channel4Volume / 15.0;

            // Generate wave
            Channel4PolynomialCycles += cycles;

            double polynomialCyclesPerSample = ClockSpeed / frequency;

            if (Channel4PolynomialCycles >= polynomialCyclesPerSample)
            {
                Channel4PolynomialCycles -= polynomialCyclesPerSample;

                byte nextBit = (byte)(((PolynomialState >> 1) & 1) ^ (PolynomialState & 1));
                PolynomialState >>= 1;
                PolynomialState |= (short)(nextBit << (counterStep != 0 ? 6 : 14));
            }


            Channel4Cycles += cycles;

            double cyclesPerSample = ClockSpeed / SPUSampleRate;

            if (Channel4Cycles >= cyclesPerSample)
            {
                Channel4Cycles -= cyclesPerSample;

                if (consecutive == 0 || Channel4Length >= 0.0)
                {
                    double sample = 0.0;
                    if ((PolynomialState & 1) == 1)
                        sample = amplitude;

                    // SO1 (right)
                    Channel4Buffer[Channel4Samples] = (short)(0.25 * sample * SO1Volume * short.MaxValue);
                    // SO2 (left)
                    Channel4Buffer[Channel4Samples + 1] = (short)(0.25 * sample * SO2Volume * short.MaxValue);

                    Channel4Samples += 2;
                }
            }

            if (consecutive != 0 && Channel4Length >= 0.0)
            {
                Channel4Length -= time;
                if (Channel4Length < 0.0)
                    Memory[0xFF26] = (byte)(NR52 & 0b1111_0111);
            }
        }

        private void Sound_Step(uint cycles)
        {
            // SPU global state
            byte NR50 = Memory[0xFF24]; // Channel control / ON-OFF / Volume (R/W)
            /*
            Bit 7   - Output Vin to SO2 terminal (1=Enable)
            Bit 6-4 - SO2 output level (volume)  (0-7)
            Bit 3   - Output Vin to SO1 terminal (1=Enable)
            Bit 2-0 - SO1 output level (volume)  (0-7)
            */
            double SO1Volume = (NR50 & 0b0000_0111) / 7.0;
            double SO2Volume = ((NR50 & 0b0111_0000) >> 4) / 7.0;

            Channel1_Step(cycles, SO1Volume, SO2Volume);
            Channel2_Step(cycles, SO1Volume, SO2Volume);
            Channel3_Step(cycles, SO1Volume, SO2Volume);
            Channel4_Step(cycles, SO1Volume, SO2Volume);
        }

        private double DutyWave(double amplitude, double x, double period, double duty)
        {
            x %= period;
            x /= period;

            return (x <= duty ? amplitude : -amplitude);
        }
    }
}
