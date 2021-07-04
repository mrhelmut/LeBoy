//#define TEXT_OUTPUT
using System;
using System.IO;

namespace LeBoyLib
{
    /// <summary>
    /// Emulates a Z80 Gameboy CPU, more specifically a Sharp LR35902 which is a Z80 minus a few instructions, with more logical operations and a sound generator.
    /// </summary>
    public partial class GBZ80
    {
        #region CPU states

        /// <summary>
        /// Main CPU memory, TODO mapping
        /// </summary>
        private GBMemory Memory = new GBMemory();
        /// <summary>
        /// 16-bit Program Counter, can address up to 64KB of memory
        /// </summary>
        private ushort PC = 0;
        /// <summary>
        /// 16-bit Stack Pointer
        /// </summary>
        private ushort SP = 0;

        /// <summary>
        /// 8-bit accumulator
        /// </summary>
        private byte A = 0;
        /// <summary>
        /// 8-bit register
        /// Can be combined to form 16-bit register (BC, DE and HL)
        /// </summary>
        private byte B = 0, C = 0, D = 0, E = 0, H = 0, L = 0;

        /// <summary>
        /// 8-bit flags (ZNHC0000, the four lattest flags are not used)
        /// Z = 0x80, N = 0x40, H = 0x20, C = 0x10
        /// Z is the Zero flag (set if the result of the operation is 0)
        /// N is the Substraction flag (set if the last operation was a substration)
        /// H is the half-carry flag (set if there was a carry from bit 3 to 4 on a 8-bit operation, or if there was a carry from bit 7 to 8 on 16-bit a operation)
        /// C is the carry flag (set if there was a carry)
        /// </summary>
        private byte F = 0;

        /// <summary>
        /// Length of the last executed instruction (in bytes)
        /// </summary>
        private uint m = 0;
        /// <summary>
        /// Duration of the last executed instruction (in CPU cycles)
        /// </summary>
        private uint t = 0;
        /// <summary>
        /// Total length of executed instructions (in bytes)
        /// </summary>
        private uint totalM = 0;
        /// <summary>
        /// Total duration of executed instructions (in CPU cycles)
        /// </summary>
        private uint totalT = 0;

        /// <summary>
        /// Interrupts state (0 = off, 1 = on, 2 = has been triggered but must execute next inst first)
        /// </summary>
        private int IME = 0;

        /// <summary>
        /// Tells if the cpu is in low power mode (wait for interrupt)
        /// </summary>
        private bool IsHalted = false;

        public const double ClockSpeed = 4194304.0; // normal GB hz

        #endregion

        #region LCD states

        /// <summary>
        /// Duration of LCD work (in CPU cycles)
        /// </summary>
        private uint LCDmodeT = 0;

        /// <summary>
        /// Screen buffer array
        /// </summary>
        private byte[] ScreenBuffer = new byte[92160];

        /// <summary>
        /// Tells if the LCD just turned on
        /// </summary>
        private bool WasLCDDisabled = false;

        #endregion

        #region Timers
        /// <summary>
        /// Current DIV t-clock since last increment
        /// </summary>
        private uint DIVt = 0;

        /// <summary>
        /// Current TIMA t-clock since last increment
        /// </summary>
        private uint TIMAt = 0;

        /// <summary>
        /// Keeping track of the timer state to avoid drifting
        /// </summary>
        private bool LastTimerState = false;
        #endregion

        /// <summary>
        /// Buttons' states (true = pressed)
        ///   0 = Right
        ///   1 = Left
        ///   2 = Up
        ///   3 = Down
        ///   4 = A
        ///   5 = B
        ///   6 = Select
        ///   7 = Start
        /// </summary>
        public bool[] JoypadState = new bool[8];

        /// <summary>
        /// Reset the CPU
        /// </summary>
        private void Reset()
        {
            Memory.ResetRAM();
            PC = 0; SP = 0;
            A = 0; B = 0; C = 0; D = 0; E = 0; H = 0; L = 0; F = 0;
            m = 0; t = 0;
            totalM = 0; totalT = 0;
            IME = 1;
            LCDmodeT = 2;
            for (int i = 0; i < JoypadState.Length; i++)
                JoypadState[i] = false;
            SkipBIOS();
        }

        /// <summary>
        /// Return the screen buffer
        /// </summary>
        /// <returns>160x144x4 byte array with RGBA color coded on 4 bytes</returns>
        public byte[] GetScreenBuffer()
        {
            return ScreenBuffer;
        }

        /// <summary>
        /// Skip the BIOS and initialise registers and memory
        /// </summary>
        private void SkipBIOS()
        {
            Memory.IsInBIOS = false;
            PC = 0x0100;
            // These values are from pandoc documentation but looks to be wrong
            A = 0x01; F = 0xB0;
            B = 0x00; C = 0x13;
            D = 0x00; E = 0xD8;
            H = 0x01; L = 0x4D;
            // The following values are from other emulators
            // A = 0x11; F = 0x80;
            // B = 0x00; C = 0x00;
            // D = 0xFF; E = 0x56;
            // H = 0x00; L = 0x0D;
            SP = 0xFFFE;
            IME = 0;
            Memory.SetGBDefault();
        }

        /// <summary>
        /// Load a ROM
        /// </summary>
        /// <param name="rom">Data array</param>
        public void Load(byte[] rom)
        {
            Memory.Reset();
            Reset();
            Memory.Load(rom);
        }

#if TEXT_OUTPUT
        int currentStep = 0;
        bool fileOpened = false;
        StreamWriter sw;
#endif

        /// <summary>
        /// Decode the next instruction and dispatch to the corresponding function
        /// </summary>               
        public uint DecodeAndDispatch()
        {
#if TEXT_OUTPUT    
            if (!fileOpened)
            {
                FileStream fs = new FileStream("bad.txt", FileMode.Create);
                sw = new StreamWriter(fs);
                fileOpened = true;
            }
#endif            

            // interrupts registers
            
            byte IF = Memory[0xFF0F];
            byte IE = Memory[0xFFFF];

            //if (IsHalted && (IF & IE) != 0)
            //    IsHalted = false;
           
            totalM += m;
            totalT += t;

            if (!IsHalted)
            {
                byte opcode = Memory[PC++]; // read the current opcode and increase PC

#if TEXT_OUTPUT
                currentStep++;                
                
                sw.WriteLine(PC + "(" + opcode + "):" + "a=" + A + ",b=" + B + ",c=" + C + ",d=" + D + ",e=" + E + ",f=" + F + ",h=" + H + ",l=" + L + ",TAC=" + Memory[0xFF07] + ",TMA=" + Memory[0xFF06] + ",TIMA=" + Memory[0xFF05] + ",sp=" + SP);
                //sw.WriteLine("IF=" + Memory[0xFF0F] + ",LY=" + Memory[0xFF44] + ",LCD=" + Memory[0xFF41]);
                //sw.WriteLine(Memory[0x7FF4]);
            
                if (currentStep == 600000)
                {
                    sw.Flush();
                    sw.Close();                    
                }
#endif

                // dispatch to the instruction & execute
                if (opcode == 0xCB)
                {
                    opcode = Memory[PC++]; // CB m = 1
                    
                    // While the documentation states that CB is 1 byte and 4 cycles long,
                    // it actually doesn't use any cycle (in fact, its lenght is already taken in account in the CB instructions' timings).                   

                    switch (opcode)
                    {
                        #region CB prefixed instruction set dispatch
                        case 0x00: RLC_B(); break;
                        case 0x01: RLC_C(); break;
                        case 0x02: RLC_D(); break;
                        case 0x03: RLC_E(); break;
                        case 0x04: RLC_H(); break;
                        case 0x05: RLC_L(); break;
                        case 0x06: RLC_aHL(); break;
                        case 0x07: RLC_A(); break;
                        case 0x08: RRC_B(); break;
                        case 0x09: RRC_C(); break;
                        case 0x0A: RRC_D(); break;
                        case 0x0B: RRC_E(); break;
                        case 0x0C: RRC_H(); break;
                        case 0x0D: RRC_L(); break;
                        case 0x0E: RRC_aHL(); break;
                        case 0x0F: RRC_A(); break;
                        case 0x10: RL_B(); break;
                        case 0x11: RL_C(); break;
                        case 0x12: RL_D(); break;
                        case 0x13: RL_E(); break;
                        case 0x14: RL_H(); break;
                        case 0x15: RL_L(); break;
                        case 0x16: RL_aHL(); break;
                        case 0x17: RL_A(); break;
                        case 0x18: RR_B(); break;
                        case 0x19: RR_C(); break;
                        case 0x1A: RR_D(); break;
                        case 0x1B: RR_E(); break;
                        case 0x1C: RR_H(); break;
                        case 0x1D: RR_L(); break;
                        case 0x1E: RR_aHL(); break;
                        case 0x1F: RR_A(); break;
                        case 0x20: SLA_B(); break;
                        case 0x21: SLA_C(); break;
                        case 0x22: SLA_D(); break;
                        case 0x23: SLA_E(); break;
                        case 0x24: SLA_H(); break;
                        case 0x25: SLA_L(); break;
                        case 0x26: SLA_aHL(); break;
                        case 0x27: SLA_A(); break;
                        case 0x28: SRA_B(); break;
                        case 0x29: SRA_C(); break;
                        case 0x2A: SRA_D(); break;
                        case 0x2B: SRA_E(); break;
                        case 0x2C: SRA_H(); break;
                        case 0x2D: SRA_L(); break;
                        case 0x2E: SRA_aHL(); break;
                        case 0x2F: SRA_A(); break;
                        case 0x30: SWAP_B(); break;
                        case 0x31: SWAP_C(); break;
                        case 0x32: SWAP_D(); break;
                        case 0x33: SWAP_E(); break;
                        case 0x34: SWAP_H(); break;
                        case 0x35: SWAP_L(); break;
                        case 0x36: SWAP_aHL(); break;
                        case 0x37: SWAP_A(); break;
                        case 0x38: SRL_B(); break;
                        case 0x39: SRL_C(); break;
                        case 0x3A: SRL_D(); break;
                        case 0x3B: SRL_E(); break;
                        case 0x3C: SRL_H(); break;
                        case 0x3D: SRL_L(); break;
                        case 0x3E: SRL_aHL(); break;
                        case 0x3F: SRL_A(); break;
                        case 0x40: BIT_0_B(); break;
                        case 0x41: BIT_0_C(); break;
                        case 0x42: BIT_0_D(); break;
                        case 0x43: BIT_0_E(); break;
                        case 0x44: BIT_0_H(); break;
                        case 0x45: BIT_0_L(); break;
                        case 0x46: BIT_0_aHL(); break;
                        case 0x47: BIT_0_A(); break;
                        case 0x48: BIT_1_B(); break;
                        case 0x49: BIT_1_C(); break;
                        case 0x4A: BIT_1_D(); break;
                        case 0x4B: BIT_1_E(); break;
                        case 0x4C: BIT_1_H(); break;
                        case 0x4D: BIT_1_L(); break;
                        case 0x4E: BIT_1_aHL(); break;
                        case 0x4F: BIT_1_A(); break;
                        case 0x50: BIT_2_B(); break;
                        case 0x51: BIT_2_C(); break;
                        case 0x52: BIT_2_D(); break;
                        case 0x53: BIT_2_E(); break;
                        case 0x54: BIT_2_H(); break;
                        case 0x55: BIT_2_L(); break;
                        case 0x56: BIT_2_aHL(); break;
                        case 0x57: BIT_2_A(); break;
                        case 0x58: BIT_3_B(); break;
                        case 0x59: BIT_3_C(); break;
                        case 0x5A: BIT_3_D(); break;
                        case 0x5B: BIT_3_E(); break;
                        case 0x5C: BIT_3_H(); break;
                        case 0x5D: BIT_3_L(); break;
                        case 0x5E: BIT_3_aHL(); break;
                        case 0x5F: BIT_3_A(); break;
                        case 0x60: BIT_4_B(); break;
                        case 0x61: BIT_4_C(); break;
                        case 0x62: BIT_4_D(); break;
                        case 0x63: BIT_4_E(); break;
                        case 0x64: BIT_4_H(); break;
                        case 0x65: BIT_4_L(); break;
                        case 0x66: BIT_4_aHL(); break;
                        case 0x67: BIT_4_A(); break;
                        case 0x68: BIT_5_B(); break;
                        case 0x69: BIT_5_C(); break;
                        case 0x6A: BIT_5_D(); break;
                        case 0x6B: BIT_5_E(); break;
                        case 0x6C: BIT_5_H(); break;
                        case 0x6D: BIT_5_L(); break;
                        case 0x6E: BIT_5_aHL(); break;
                        case 0x6F: BIT_5_A(); break;
                        case 0x70: BIT_6_B(); break;
                        case 0x71: BIT_6_C(); break;
                        case 0x72: BIT_6_D(); break;
                        case 0x73: BIT_6_E(); break;
                        case 0x74: BIT_6_H(); break;
                        case 0x75: BIT_6_L(); break;
                        case 0x76: BIT_6_aHL(); break;
                        case 0x77: BIT_6_A(); break;
                        case 0x78: BIT_7_B(); break;
                        case 0x79: BIT_7_C(); break;
                        case 0x7A: BIT_7_D(); break;
                        case 0x7B: BIT_7_E(); break;
                        case 0x7C: BIT_7_H(); break;
                        case 0x7D: BIT_7_L(); break;
                        case 0x7E: BIT_7_aHL(); break;
                        case 0x7F: BIT_7_A(); break;
                        case 0x80: RES_0_B(); break;
                        case 0x81: RES_0_C(); break;
                        case 0x82: RES_0_D(); break;
                        case 0x83: RES_0_E(); break;
                        case 0x84: RES_0_H(); break;
                        case 0x85: RES_0_L(); break;
                        case 0x86: RES_0_aHL(); break;
                        case 0x87: RES_0_A(); break;
                        case 0x88: RES_1_B(); break;
                        case 0x89: RES_1_C(); break;
                        case 0x8A: RES_1_D(); break;
                        case 0x8B: RES_1_E(); break;
                        case 0x8C: RES_1_H(); break;
                        case 0x8D: RES_1_L(); break;
                        case 0x8E: RES_1_aHL(); break;
                        case 0x8F: RES_1_A(); break;
                        case 0x90: RES_2_B(); break;
                        case 0x91: RES_2_C(); break;
                        case 0x92: RES_2_D(); break;
                        case 0x93: RES_2_E(); break;
                        case 0x94: RES_2_H(); break;
                        case 0x95: RES_2_L(); break;
                        case 0x96: RES_2_aHL(); break;
                        case 0x97: RES_2_A(); break;
                        case 0x98: RES_3_B(); break;
                        case 0x99: RES_3_C(); break;
                        case 0x9A: RES_3_D(); break;
                        case 0x9B: RES_3_E(); break;
                        case 0x9C: RES_3_H(); break;
                        case 0x9D: RES_3_L(); break;
                        case 0x9E: RES_3_aHL(); break;
                        case 0x9F: RES_3_A(); break;
                        case 0xA0: RES_4_B(); break;
                        case 0xA1: RES_4_C(); break;
                        case 0xA2: RES_4_D(); break;
                        case 0xA3: RES_4_E(); break;
                        case 0xA4: RES_4_H(); break;
                        case 0xA5: RES_4_L(); break;
                        case 0xA6: RES_4_aHL(); break;
                        case 0xA7: RES_4_A(); break;
                        case 0xA8: RES_5_B(); break;
                        case 0xA9: RES_5_C(); break;
                        case 0xAA: RES_5_D(); break;
                        case 0xAB: RES_5_E(); break;
                        case 0xAC: RES_5_H(); break;
                        case 0xAD: RES_5_L(); break;
                        case 0xAE: RES_5_aHL(); break;
                        case 0xAF: RES_5_A(); break;
                        case 0xB0: RES_6_B(); break;
                        case 0xB1: RES_6_C(); break;
                        case 0xB2: RES_6_D(); break;
                        case 0xB3: RES_6_E(); break;
                        case 0xB4: RES_6_H(); break;
                        case 0xB5: RES_6_L(); break;
                        case 0xB6: RES_6_aHL(); break;
                        case 0xB7: RES_6_A(); break;
                        case 0xB8: RES_7_B(); break;
                        case 0xB9: RES_7_C(); break;
                        case 0xBA: RES_7_D(); break;
                        case 0xBB: RES_7_E(); break;
                        case 0xBC: RES_7_H(); break;
                        case 0xBD: RES_7_L(); break;
                        case 0xBE: RES_7_aHL(); break;
                        case 0xBF: RES_7_A(); break;
                        case 0xC0: SET_0_B(); break;
                        case 0xC1: SET_0_C(); break;
                        case 0xC2: SET_0_D(); break;
                        case 0xC3: SET_0_E(); break;
                        case 0xC4: SET_0_H(); break;
                        case 0xC5: SET_0_L(); break;
                        case 0xC6: SET_0_aHL(); break;
                        case 0xC7: SET_0_A(); break;
                        case 0xC8: SET_1_B(); break;
                        case 0xC9: SET_1_C(); break;
                        case 0xCA: SET_1_D(); break;
                        case 0xCB: SET_1_E(); break;
                        case 0xCC: SET_1_H(); break;
                        case 0xCD: SET_1_L(); break;
                        case 0xCE: SET_1_aHL(); break;
                        case 0xCF: SET_1_A(); break;
                        case 0xD0: SET_2_B(); break;
                        case 0xD1: SET_2_C(); break;
                        case 0xD2: SET_2_D(); break;
                        case 0xD3: SET_2_E(); break;
                        case 0xD4: SET_2_H(); break;
                        case 0xD5: SET_2_L(); break;
                        case 0xD6: SET_2_aHL(); break;
                        case 0xD7: SET_2_A(); break;
                        case 0xD8: SET_3_B(); break;
                        case 0xD9: SET_3_C(); break;
                        case 0xDA: SET_3_D(); break;
                        case 0xDB: SET_3_E(); break;
                        case 0xDC: SET_3_H(); break;
                        case 0xDD: SET_3_L(); break;
                        case 0xDE: SET_3_aHL(); break;
                        case 0xDF: SET_3_A(); break;
                        case 0xE0: SET_4_B(); break;
                        case 0xE1: SET_4_C(); break;
                        case 0xE2: SET_4_D(); break;
                        case 0xE3: SET_4_E(); break;
                        case 0xE4: SET_4_H(); break;
                        case 0xE5: SET_4_L(); break;
                        case 0xE6: SET_4_aHL(); break;
                        case 0xE7: SET_4_A(); break;
                        case 0xE8: SET_5_B(); break;
                        case 0xE9: SET_5_C(); break;
                        case 0xEA: SET_5_D(); break;
                        case 0xEB: SET_5_E(); break;
                        case 0xEC: SET_5_H(); break;
                        case 0xED: SET_5_L(); break;
                        case 0xEE: SET_5_aHL(); break;
                        case 0xEF: SET_5_A(); break;
                        case 0xF0: SET_6_B(); break;
                        case 0xF1: SET_6_C(); break;
                        case 0xF2: SET_6_D(); break;
                        case 0xF3: SET_6_E(); break;
                        case 0xF4: SET_6_H(); break;
                        case 0xF5: SET_6_L(); break;
                        case 0xF6: SET_6_aHL(); break;
                        case 0xF7: SET_6_A(); break;
                        case 0xF8: SET_7_B(); break;
                        case 0xF9: SET_7_C(); break;
                        case 0xFA: SET_7_D(); break;
                        case 0xFB: SET_7_E(); break;
                        case 0xFC: SET_7_H(); break;
                        case 0xFD: SET_7_L(); break;
                        case 0xFE: SET_7_aHL(); break;
                        case 0xFF: SET_7_A(); break;
                        #endregion
                    }
                }
                else
                    switch (opcode)
                    {
                        #region Base instruction set dispatch
                        case 0x00: NOP(); break;
                        case 0x01: LD_BC_d16(); break;
                        case 0x02: LD_aBC_A(); break;
                        case 0x03: INC_BC(); break;
                        case 0x04: INC_B(); break;
                        case 0x05: DEC_B(); break;
                        case 0x06: LD_B_d8(); break;
                        case 0x07: RLCA(); break;
                        case 0x08: LD_a16_SP(); break;
                        case 0x09: ADD_HL_BC(); break;
                        case 0x0A: LD_A_aBC(); break;
                        case 0x0B: DEC_BC(); break;
                        case 0x0C: INC_C(); break;
                        case 0x0D: DEC_C(); break;
                        case 0x0E: LD_C_d8(); break;
                        case 0x0F: RRCA(); break;
                        case 0x10: STOP_0(); break;
                        case 0x11: LD_DE_d16(); break;
                        case 0x12: LD_aDE_A(); break;
                        case 0x13: INC_DE(); break;
                        case 0x14: INC_D(); break;
                        case 0x15: DEC_D(); break;
                        case 0x16: LD_D_d8(); break;
                        case 0x17: RLA(); break;
                        case 0x18: JR_r8(); break;
                        case 0x19: ADD_HL_DE(); break;
                        case 0x1A: LD_A_aDE(); break;
                        case 0x1B: DEC_DE(); break;
                        case 0x1C: INC_E(); break;
                        case 0x1D: DEC_E(); break;
                        case 0x1E: LD_E_d8(); break;
                        case 0x1F: RRA(); break;
                        case 0x20: JR_NZ_r8(); break;
                        case 0x21: LD_HL_d16(); break;
                        case 0x22: LD_aHLi_A(); break;
                        case 0x23: INC_HL(); break;
                        case 0x24: INC_H(); break;
                        case 0x25: DEC_H(); break;
                        case 0x26: LD_H_d8(); break;
                        case 0x27: DAA(); break;
                        case 0x28: JR_Z_r8(); break;
                        case 0x29: ADD_HL_HL(); break;
                        case 0x2A: LD_A_aHLi(); break;
                        case 0x2B: DEC_HL(); break;
                        case 0x2C: INC_L(); break;
                        case 0x2D: DEC_L(); break;
                        case 0x2E: LD_L_d8(); break;
                        case 0x2F: CPL(); break;
                        case 0x30: JR_NC_r8(); break;
                        case 0x31: LD_SP_d16(); break;
                        case 0x32: LD_aHLd_A(); break;
                        case 0x33: INC_SP(); break;
                        case 0x34: INC_aHL(); break;
                        case 0x35: DEC_aHL(); break;
                        case 0x36: LD_aHL_d8(); break;
                        case 0x37: SCF(); break;
                        case 0x38: JR_C_r8(); break;
                        case 0x39: ADD_HL_SP(); break;
                        case 0x3A: LD_A_aHLd(); break;
                        case 0x3B: DEC_SP(); break;
                        case 0x3C: INC_A(); break;
                        case 0x3D: DEC_A(); break;
                        case 0x3E: LD_A_d8(); break;
                        case 0x3F: CCF(); break;
                        case 0x40: LD_B_B(); break;
                        case 0x41: LD_B_C(); break;
                        case 0x42: LD_B_D(); break;
                        case 0x43: LD_B_E(); break;
                        case 0x44: LD_B_H(); break;
                        case 0x45: LD_B_L(); break;
                        case 0x46: LD_B_aHL(); break;
                        case 0x47: LD_B_A(); break;
                        case 0x48: LD_C_B(); break;
                        case 0x49: LD_C_C(); break;
                        case 0x4A: LD_C_D(); break;
                        case 0x4B: LD_C_E(); break;
                        case 0x4C: LD_C_H(); break;
                        case 0x4D: LD_C_L(); break;
                        case 0x4E: LD_C_aHL(); break;
                        case 0x4F: LD_C_A(); break;
                        case 0x50: LD_D_B(); break;
                        case 0x51: LD_D_C(); break;
                        case 0x52: LD_D_D(); break;
                        case 0x53: LD_D_E(); break;
                        case 0x54: LD_D_H(); break;
                        case 0x55: LD_D_L(); break;
                        case 0x56: LD_D_aHL(); break;
                        case 0x57: LD_D_A(); break;
                        case 0x58: LD_E_B(); break;
                        case 0x59: LD_E_C(); break;
                        case 0x5A: LD_E_D(); break;
                        case 0x5B: LD_E_E(); break;
                        case 0x5C: LD_E_H(); break;
                        case 0x5D: LD_E_L(); break;
                        case 0x5E: LD_E_aHL(); break;
                        case 0x5F: LD_E_A(); break;
                        case 0x60: LD_H_B(); break;
                        case 0x61: LD_H_C(); break;
                        case 0x62: LD_H_D(); break;
                        case 0x63: LD_H_E(); break;
                        case 0x64: LD_H_H(); break;
                        case 0x65: LD_H_L(); break;
                        case 0x66: LD_H_aHL(); break;
                        case 0x67: LD_H_A(); break;
                        case 0x68: LD_L_B(); break;
                        case 0x69: LD_L_C(); break;
                        case 0x6A: LD_L_D(); break;
                        case 0x6B: LD_L_E(); break;
                        case 0x6C: LD_L_H(); break;
                        case 0x6D: LD_L_L(); break;
                        case 0x6E: LD_L_aHL(); break;
                        case 0x6F: LD_L_A(); break;
                        case 0x70: LD_aHL_B(); break;
                        case 0x71: LD_aHL_C(); break;
                        case 0x72: LD_aHL_D(); break;
                        case 0x73: LD_aHL_E(); break;
                        case 0x74: LD_aHL_H(); break;
                        case 0x75: LD_aHL_L(); break;
                        case 0x76: HALT(); break;
                        case 0x77: LD_aHL_A(); break;
                        case 0x78: LD_A_B(); break;
                        case 0x79: LD_A_C(); break;
                        case 0x7A: LD_A_D(); break;
                        case 0x7B: LD_A_E(); break;
                        case 0x7C: LD_A_H(); break;
                        case 0x7D: LD_A_L(); break;
                        case 0x7E: LD_A_aHL(); break;
                        case 0x7F: LD_A_A(); break;
                        case 0x80: ADD_A_B(); break;
                        case 0x81: ADD_A_C(); break;
                        case 0x82: ADD_A_D(); break;
                        case 0x83: ADD_A_E(); break;
                        case 0x84: ADD_A_H(); break;
                        case 0x85: ADD_A_L(); break;
                        case 0x86: ADD_A_aHL(); break;
                        case 0x87: ADD_A_A(); break;
                        case 0x88: ADC_A_B(); break;
                        case 0x89: ADC_A_C(); break;
                        case 0x8A: ADC_A_D(); break;
                        case 0x8B: ADC_A_E(); break;
                        case 0x8C: ADC_A_H(); break;
                        case 0x8D: ADC_A_L(); break;
                        case 0x8E: ADC_A_aHL(); break;
                        case 0x8F: ADC_A_A(); break;
                        case 0x90: SUB_A_B(); break;
                        case 0x91: SUB_A_C(); break;
                        case 0x92: SUB_A_D(); break;
                        case 0x93: SUB_A_E(); break;
                        case 0x94: SUB_A_H(); break;
                        case 0x95: SUB_A_L(); break;
                        case 0x96: SUB_A_aHL(); break;
                        case 0x97: SUB_A_A(); break;
                        case 0x98: SBC_A_B(); break;
                        case 0x99: SBC_A_C(); break;
                        case 0x9A: SBC_A_D(); break;
                        case 0x9B: SBC_A_E(); break;
                        case 0x9C: SBC_A_H(); break;
                        case 0x9D: SBC_A_L(); break;
                        case 0x9E: SBC_A_aHL(); break;
                        case 0x9F: SBC_A_A(); break;
                        case 0xA0: AND_A_B(); break;
                        case 0xA1: AND_A_C(); break;
                        case 0xA2: AND_A_D(); break;
                        case 0xA3: AND_A_E(); break;
                        case 0xA4: AND_A_H(); break;
                        case 0xA5: AND_A_L(); break;
                        case 0xA6: AND_A_aHL(); break;
                        case 0xA7: AND_A_A(); break;
                        case 0xA8: XOR_A_B(); break;
                        case 0xA9: XOR_A_C(); break;
                        case 0xAA: XOR_A_D(); break;
                        case 0xAB: XOR_A_E(); break;
                        case 0xAC: XOR_A_H(); break;
                        case 0xAD: XOR_A_L(); break;
                        case 0xAE: XOR_A_aHL(); break;
                        case 0xAF: XOR_A_A(); break;
                        case 0xB0: OR_A_B(); break;
                        case 0xB1: OR_A_C(); break;
                        case 0xB2: OR_A_D(); break;
                        case 0xB3: OR_A_E(); break;
                        case 0xB4: OR_A_H(); break;
                        case 0xB5: OR_A_L(); break;
                        case 0xB6: OR_A_aHL(); break;
                        case 0xB7: OR_A_A(); break;
                        case 0xB8: CP_A_B(); break;
                        case 0xB9: CP_A_C(); break;
                        case 0xBA: CP_A_D(); break;
                        case 0xBB: CP_A_E(); break;
                        case 0xBC: CP_A_H(); break;
                        case 0xBD: CP_A_L(); break;
                        case 0xBE: CP_A_aHL(); break;
                        case 0xBF: CP_A_A(); break;
                        case 0xC0: RET_NZ(); break;
                        case 0xC1: POP_BC(); break;
                        case 0xC2: JP_NZ_a16(); break;
                        case 0xC3: JP_a16(); break;
                        case 0xC4: CALL_NZ_a16(); break;
                        case 0xC5: PUSH_BC(); break;
                        case 0xC6: ADD_A_d8(); break;
                        case 0xC7: RST_00H(); break;
                        case 0xC8: RET_Z(); break;
                        case 0xC9: RET(); break;
                        case 0xCA: JP_Z_a16(); break;
                        case 0xCB: break; // CB prefix, should not enter here
                        case 0xCC: CALL_Z_a16(); break;
                        case 0xCD: CALL_a16(); break;
                        case 0xCE: ADC_A_d8(); break;
                        case 0xCF: RST_08H(); break;
                        case 0xD0: RET_NC(); break;
                        case 0xD1: POP_DE(); break;
                        case 0xD2: JP_NC_a16(); break;
                        case 0xD3: break; // no instruction
                        case 0xD4: CALL_NC_a16(); break;
                        case 0xD5: PUSH_DE(); break;
                        case 0xD6: SUB_A_d8(); break;
                        case 0xD7: RST_10H(); break;
                        case 0xD8: RET_C(); break;
                        case 0xD9: RETI(); break;
                        case 0xDA: JP_C_a16(); break;
                        case 0xDB: break; // no instruction
                        case 0xDC: CALL_C_a16(); break;
                        case 0xDD: break; // no instruction
                        case 0xDE: SBC_A_d8(); break;
                        case 0xDF: RST_18H(); break;
                        case 0xE0: LDH_a8_A(); break;
                        case 0xE1: POP_HL(); break;
                        case 0xE2: LD_aC_A(); break;
                        case 0xE3: break; // no instruction
                        case 0xE4: break; // no instruction
                        case 0xE5: PUSH_HL(); break;
                        case 0xE6: AND_A_d8(); break;
                        case 0xE7: RST_20H(); break;
                        case 0xE8: ADD_SP_r8(); break;
                        case 0xE9: JP_HL(); break;
                        case 0xEA: LD_a16_A(); break;
                        case 0xEB: break; // no instruction
                        case 0xEC: break; // no instruction
                        case 0xED: break; // no instruction
                        case 0xEE: XOR_A_d8(); break;
                        case 0xEF: RST_28H(); break;
                        case 0xF0: LDH_A_a8(); break;
                        case 0xF1: POP_AF(); break;
                        case 0xF2: LD_A_aC(); break;
                        case 0xF3: DI(); break;
                        case 0xF4: break; // no instruction
                        case 0xF5: PUSH_AF(); break;
                        case 0xF6: OR_A_d8(); break;
                        case 0xF7: RST_30H(); break;
                        case 0xF8: LD_HL_SPr8(); break;
                        case 0xF9: LD_SP_HL(); break;
                        case 0xFA: LD_A_a16(); break;
                        case 0xFB: EI(); break;
                        case 0xFC: break; // no instruction
                        case 0xFD: break; // no instruction
                        case 0xFE: CP_A_a8(); break;
                        case 0xFF: RST_38H(); break;
                        #endregion
                    }
                
            }
            else
            {
                m = 1; // nop
                t = 4;
            }

            totalM += m;
            totalT += t;

            if (IME == 2)
                IME = 1;
            else if (IME == 1)
            {
                // interrupt handling
                IF = Memory[0xFF0F];
                IE = Memory[0xFFFF];
                byte requestInt = (byte)(IE & IF);
                if (requestInt != 0)
                {
                    // interrupt priority
                    if ((requestInt & 0x01) == 0x01)
                    {
                        INT_40h();
                        IF &= (0xFF - 0x01);
                    }
                    else if ((requestInt & 0x02) == 0x02)
                    {
                        INT_48h();
                        IF &= (0xFF - 0x02);
                    }
                    else if ((requestInt & 0x04) == 0x04)
                    {
                        INT_50h();
                        IF &= (0xFF - 0x04);
                    }
                    else if ((requestInt & 0x08) == 0x08)
                    {
                        INT_58h();
                        IF &= (0xFF - 0x08);
                    }
                    else if ((requestInt & 0x10) == 0x10)
                    {
                        INT_60h();
                        IF &= (0xFF - 0x10);
                    }

                    Memory[0xFF0F] = IF;
                }
            }

            LCD_Step();
            Sound_Step();

            // TIMERS
            byte timerState = Memory[0xFF07];
            DIVt += totalT;
            if (DIVt >= 256)
            {
                DIVt -= 256;
                Memory.IncDIV();
            }
            if ((timerState & 0x04) == 0x04) // timer enabled
            {
                TIMAt += totalT;

                if (LastTimerState == false)
                    TIMAt -= 8; // adjust the lenght of timer activation because the activation instruction has been counted in totalT

                uint tSpeed = 16;
                switch (timerState & 0x03)
                {
                    case 0: tSpeed = 1024; break;
                    case 1: tSpeed = 16; break;
                    case 2: tSpeed = 64; break;
                    case 3: tSpeed = 256; break;
                }
                if (TIMAt >= tSpeed)
                {
                    TIMAt -= tSpeed;
                    byte TIMA = Memory[0xFF05];
                    Memory[0xFF05] = ++TIMA;
                    if (TIMA == 0)
                    {
                        // timer interrupt
                        Memory[0xFF05] = Memory[0xFF06];
                        IF = Memory[0xFF0F];
                        IF |= 0x04;
                        Memory[0xFF0F] = IF;
                    }
                }

                LastTimerState = true;
            }
            else
                LastTimerState = false;

            // INPUTS
            byte joypad = 0;
            byte rank = 1;
            byte joypadLast = Memory[0xFF00];
            byte buttonSelect = (byte)(joypadLast & 0x20);
            byte directionSelect = (byte)(joypadLast & 0x10);
            if (directionSelect == 0 || buttonSelect == 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (buttonSelect == 0 && JoypadState[i + 4] == false)
                        joypad |= rank;
                    else if (directionSelect == 0 && JoypadState[i] == false)
                        joypad |= rank;
                    rank <<= 1;
                }
                // check if something went from High to Low (i.e. has been pressed)
                byte lastKey = (byte)(joypadLast & 0xF);
                byte not = (byte)(~lastKey);
                lastKey = (byte)((joypad & 0xF) & not);
                if (lastKey != 0)
                {
                    // input interrupt
                    IF |= 0x10;
                    Memory[0xFF0F] = IF;
                }

                // update the inputs
                byte newValue = (byte)(joypad | (joypadLast & 0xF0));
                Memory.UpdateJoypad(newValue);
            }

            uint cycles = t;

            m = 0; t = 0;
            totalM = 0;
            totalT = 0;

            return cycles;
        }       
    }
}
