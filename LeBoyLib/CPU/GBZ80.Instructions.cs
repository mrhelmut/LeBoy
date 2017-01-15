using System;
using System.Runtime.CompilerServices;

namespace LeBoyLib
{
    /// <summary>
    /// Emulates a Z80 Gameboy CPU, more specifically a Sharp LR35902 which is a Z80 minus a few instructions, with more logical operations and a sound generator.
    /// </summary>
    public partial class GBZ80
    {
        /* Instruction set and timing available at:
         * http://www.pastraiser.com/cpu/gameboy/gameboy_opcodes.html
         * 
         * Naming convetions:
         * (HL) => aHL
         * (HL+) => aHLi
         * (HL-) => aHLd
         * 
         * "set F(ZNHC)" means that flags are affected by the instruction:
         * Letters mean that flags are affected accordingly to their behavior
         * 0 or 1 mean that flags are set to 0 or 1
         * - mean that flags preserve their value
         * Example: "set F(-01C)" means that the Zero flag is preserved, N is set to 0, H to 1 and the Carry is calculated accordingly to the instruction behavior (e.g. if there's an overflow during an addition, then the flag is set, otherwise 0)
         */

        #region Base instruction set

        /// <summary>
        /// 00 "NOP": No operation, but still consume CPU clock cycles
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NOP()
        {
            m = 1; t = 4;
        }

        /// <summary>
        /// 01 "LD BC,d16": Load 16-bit immediate into BC
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_BC_d16()
        {
            C = Memory[PC];
            B = Memory[PC + 1];
            PC += 2;
            m = 3; t = 12;
        }

        /// <summary>
        /// 02 "LD (BC),A": Save A to address pointed by BC
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aBC_A()
        {
            Memory[(B << 8) + C] = A;
            m = 1; t = 8;
        }

        /// <summary>
        /// 03 "INC BC": Increment 16-bit BC, no flags are set on 16-bit operation
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_BC()
        {
            C++;
            if (C == 0)
                B++;
            m = 1; t = 8;
        }

        /// <summary>
        /// 04 "INC B": Increment 8-bit B, set F(Z0H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_B()
        {
            B++;
            F &= 0x10; // C is not changed, N is set to 0
            if (B == 0)
                F |= 0x80; // Z = 1
            if ((B & 0xF) == 0)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 05 "DEC B": Decrement 8-bit B, set F(Z1H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_B()
        {
            B--;
            F = (byte)((F & 0x10) | 0x40); // C is not changed, N is set to 1
            if (B == 0)
                F |= 0x80; // Z = 1
            if ((B & 0xF) == 0xF)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 06 "LD B,d8": Load 8-bit immediate into B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_B_d8()
        {
            B = Memory[PC];
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// 07 "RLCA": Rotate A left, set F(000C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RLCA()
        {
            F = 0;
            A = (byte)((byte)(A << 1) | (byte)(A >> 7));
            if ((A & 0x1) == 1)
                F = 0x10; // C = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 08 "LD (a16),SP": Save SP to given address
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_a16_SP()
        {
            ushort a16 = (ushort)((Memory[PC + 1] << 8) + Memory[PC]);
            Memory[a16 + 1] = (byte)((SP & 0xFF00) >> 8);
            Memory[a16] = (byte)(SP & 0x00FF);
            m = 3; t = 20;
            PC += 2;
        }

        /// <summary>
        /// 09 "ADD HL,BC": Add 16-bit BC to HL, set F(-0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_HL_BC()
        {
            F &= 0x80; // Z is not changed, N is set to 0
            int HL = (H << 8) + L;
            int BC = (B << 8) + C;
            int halfAdd = (HL & 0x0FFF) + (BC & 0x0FFF);
            int tmp = HL + BC;
            HL = (ushort)tmp;
            if (tmp > 0xFFFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0FFF)
                F |= 0x20; // H = 1
            H = (byte)((HL >> 8) & 0xFF);
            L = (byte)(HL & 0xFF);
            m = 1; t = 8;
        }

        /// <summary>
        /// 0A "LD A,(BC)": Load A from address pointed to by BC
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_aBC()
        {
            int BC = (B << 8) + C;
            A = Memory[BC];
            m = 1; t = 8;
        }

        /// <summary>
        /// 0B "DEC BC": Decrement 16-bit BC
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_BC()
        {
            C--;
            if (C == 0xFF)
                B--;
            m = 1; t = 8;
        }

        /// <summary>
        /// 0C "INC C": Increment 8-bit C, set F(Z0H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_C()
        {
            C++;
            F &= 0x10; // C is not changed, N is set to 0
            if (C == 0)
                F |= 0x80; // Z = 1
            if ((C & 0xF) == 0)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 0D "DEC C": Decrement 8-bit C, set F(Z1H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_C()
        {
            C--;
            F = (byte)((F & 0x10) | 0x40); // C is not changed, N is set to 1
            if (C == 0)
                F |= 0x80; // Z = 1
            if ((C & 0xF) == 0xF)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 0E "LD C,d8": Load 8-bit immediate into C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_C_d8()
        {
            C = Memory[PC];
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// 0F "RRCA": Rotate A right, set F(000C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RRCA()
        {
            F = 0;
            A = (byte)((byte)(A >> 1) | (byte)(A << 7));
            if ((A & 0x80) == 0x80)
                F = 0x10; // C = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 10 "STOP 0": Stop CPU
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void STOP_0()
        {
            // ???
            PC++;
            m = 2; t = 4;
            // when stopped, we should be waiting here for something in FF00 to go from 0 to 1
        }

        /// <summary>
        /// 11 "LD DE,d16": Load 16-bit immediate into DE
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_DE_d16()
        {
            E = Memory[PC];
            D = Memory[PC + 1];
            PC += 2;
            m = 3; t = 12;
        }

        /// <summary>
        /// 12 "LD (DE),A": Save A to address pointed by DE
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aDE_A()
        {
            Memory[(D << 8) + E] = A;
            m = 1; t = 8;
        }

        /// <summary>
        /// 13 "INC DE": Increment 16-bit DE, no flags are set on 16-bit operation
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_DE()
        {
            E++;
            if (E == 0)
                D++;
            m = 1; t = 8;
        }

        /// <summary>
        /// 14 "INC D": Increment 8-bit D, set F(Z0H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_D()
        {
            D++;
            F &= 0x10; // C is not changed, N is set to 0
            if (D == 0)
                F |= 0x80; // Z = 1
            if ((D & 0xF) == 0)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 15 "DEC D": Decrement 8-bit D, set F(Z1H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_D()
        {
            D--;
            F = (byte)((F & 0x10) | 0x40); // C is not changed, N is set to 1
            if (D == 0)
                F |= 0x80; // Z = 1
            if ((D & 0xF) == 0xF)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 16 "LD D,d8": Load 8-bit immediate into D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_D_d8()
        {
            D = Memory[PC];
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// 17 "RLA": Rotate A left, set F(000C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RLA()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            if ((A & 0x80) == 0x80)
                F |= 0x10;
            A = (byte)((A << 1) + carry);
            m = 1; t = 4;
        }

        /// <summary>
        /// 18 "JR r8": Relative jump by signed immediate
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JR_r8()
        {
            int jump = Memory[PC];
            if (jump > 127)
                jump = -((~jump + 1) & 255);
            PC++;
            PC = (ushort)(PC + jump);
            m = 2; t = 12;
        }

        /// <summary>
        /// 19 "ADD HL,DE": Add 16-bit DE to HL, set F(-0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_HL_DE()
        {
            F &= 0x80; // Z is not changed, N is set to 0
            int HL = (H << 8) + L;
            int DE = (D << 8) + E;
            int halfAdd = (HL & 0x0FFF) + (DE & 0x0FFF);
            int tmp = HL + DE;
            HL = (ushort)tmp;
            if (tmp > 0xFFFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0FFF)
                F |= 0x20; // H = 1
            H = (byte)((HL >> 8) & 0xFF);
            L = (byte)(HL & 0xFF);
            m = 1; t = 8;
        }

        /// <summary>
        /// 1A "LD A,(DE)": Load A from address pointed to by DE
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_aDE()
        {
            int DE = (D << 8) + E;
            A = Memory[DE];
            m = 1; t = 8;
        }

        /// <summary>
        /// 1B "DEC DE": Decrement 16-bit DE
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_DE()
        {
            E--;
            if (E == 0xFF)
                D--;
            m = 1; t = 8;
        }

        /// <summary>
        /// 1C "INC E": Increment 8-bit E, set F(Z0H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_E()
        {
            E++;
            F &= 0x10; // C is not changed, N is set to 0
            if (E == 0)
                F |= 0x80; // Z = 1
            if ((E & 0xF) == 0)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 1D "DEC E": Decrement 8-bit E, set F(Z1H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_E()
        {
            E--;
            F = (byte)((F & 0x10) | 0x40); // C is not changed, N is set to 1
            if (E == 0)
                F |= 0x80; // Z = 1
            if ((E & 0xF) == 0xF)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 1E "LD E,d8": Load 8-bit immediate into E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_E_d8()
        {
            E = Memory[PC];
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// 1F "RRA": Rotate A right, set F(000C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RRA()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 0x80;
            F = 0;
            if ((A & 0x1) == 0x1)
                F |= 0x10;
            A = (byte)((A >> 1) + carry);
            m = 1; t = 4;
        }

        /// <summary>
        /// 20 "JR NZ,r8": Relative jump by signed immediate if last result was not zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JR_NZ_r8()
        {
            m = 2; t = 8;
            if ((F & 0x80) != 0x80)
            {
                int jump = Memory[PC];
                if (jump > 127)
                    jump = -((~jump + 1) & 255);
                PC = (ushort)(PC + jump);
                t += 4;
            }
            PC++;
        }

        /// <summary>
        /// 21 "LD HL,d16": Load 16-bit immediate into HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_HL_d16()
        {
            L = Memory[PC];
            H = Memory[PC + 1];
            PC += 2;
            m = 3; t = 12;
        }

        /// <summary>
        /// 22 "LD (HL+),A": Save A to address pointed by HL and increment HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aHLi_A()
        {
            Memory[(H << 8) + L] = A;
            L++;
            if (L == 0)
                H++;
            m = 1; t = 8;
        }

        /// <summary>
        /// 23 "INC HL": Increment 16-bit HL, no flags are set on 16-bit operation
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_HL()
        {
            L++;
            if (L == 0)
                H++;
            m = 1; t = 8;
        }

        /// <summary>
        /// 24 "INC H": Increment 8-bit H, set F(Z0H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_H()
        {
            H++;
            F &= 0x10; // C is not changed, N is set to 0
            if (H == 0)
                F |= 0x80; // Z = 1
            if ((H & 0xF) == 0)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 25 "DEC H": Decrement 8-bit H, set F(Z1H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_H()
        {
            H--;
            F = (byte)((F & 0x10) | 0x40); // C is not changed, N is set to 1
            if (H == 0)
                F |= 0x80; // Z = 1
            if ((H & 0xF) == 0xF)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 26 "LD H,d8": Load 8-bit immediate into H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_H_d8()
        {
            H = Memory[PC];
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// 27 "DAA": Adjust A for BCD addition, set F(Z-0C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DAA()
        {
            byte tmpF = (byte)(F & 0x50); // save N and C

            if ((F & 0x40) == 0x40)
            {
                if ((F & 0x20) == 0x20)
                    A -= 0x06;
                if ((F & 0x10) == 0x10)
                    A -= 0x60;
            }
            else
            {
                if ((F & 0x10) == 0x10 || A > 0x99)
                {
                    if ((F & 0x20) == 0x20 || (A & 0x0F) > 0x09)
                        A += 0x66;
                    else
                        A += 0x60;
                    tmpF |= 0x10; // C = 1
                }
                else if ((F & 0x20) == 0x20 || (A & 0x0F) > 0x09)
                    A += 0x06;
            }

            if (A == 0)
                tmpF |= 0x80; // Z = 1
            F = tmpF;

            m = 1; t = 4;
        }

        /// <summary>
        /// 28 "JR Z,r8": Relative jump by signed immediate if last result was zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JR_Z_r8()
        {
            m = 2; t = 8;
            if ((F & 0x80) == 0x80)
            {
                int jump = Memory[PC];
                if (jump > 127)
                    jump = -((~jump + 1) & 255);
                PC = (ushort)(PC + jump);
                t += 4;
            }
            PC++;
        }

        /// <summary>
        /// 29 "ADD HL,HL": Add 16-bit HL to HL, set F(-0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_HL_HL()
        {
            F &= 0x80; // Z is not changed, N is set to 0
            int HL = (H << 8) + L;
            int halfAdd = (HL & 0x0FFF) + (HL & 0x0FFF);
            int tmp = HL + HL;
            HL = (ushort)tmp;
            if (tmp > 0xFFFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0FFF)
                F |= 0x20; // H = 1
            H = (byte)((HL >> 8) & 0xFF);
            L = (byte)(HL & 0xFF);
            m = 1; t = 8;
        }

        /// <summary>
        /// 2A "LD A,(HL+)": Load A from address pointed to by HL, and increment HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_aHLi()
        {
            int HL = (H << 8) + L;
            A = Memory[HL];
            L++;
            if (L == 0)
                H++;
            m = 1; t = 8;
        }

        /// <summary>
        /// 2B "DEC HL": Decrement 16-bit HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_HL()
        {
            L--;
            if (L == 0xFF)
                H--;
            m = 1; t = 8;
        }

        /// <summary>
        /// 2C "INC L": Increment 8-bit L, set F(Z0H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_L()
        {
            L++;
            F &= 0x10; // C is not changed, N is set to 0
            if (L == 0)
                F |= 0x80; // Z = 1
            if ((L & 0xF) == 0)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 2D "DEC L": Decrement 8-bit L, set F(Z1H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_L()
        {
            L--;
            F = (byte)((F & 0x10) | 0x40); // C is not changed, N is set to 1
            if (L == 0)
                F |= 0x80; // Z = 1
            if ((L & 0xF) == 0xF)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 2E "LD L,d8": Load 8-bit immediate into L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_L_d8()
        {
            L = Memory[PC];
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// 2F "CPL": Complement (logical NOT) on A, set F(-11-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CPL()
        {
            A = (byte)(~A);
            F |= 0x60;
            m = 1; t = 4;
        }

        /// <summary>
        /// 30 "JR NC,r8": Relative jump by signed immediate if last result caused no carry
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JR_NC_r8()
        {
            m = 2; t = 8;
            if ((F & 0x10) != 0x10)
            {
                int jump = Memory[PC];
                if (jump > 127)
                    jump = -((~jump + 1) & 255);
                PC = (ushort)(PC + jump);
                t += 4;
            }
            PC++;
        }

        /// <summary>
        /// 31 "LD SP,d16": Load 16-bit immediate into SP
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_SP_d16()
        {
            SP = (ushort)((Memory[PC + 1] << 8) + Memory[PC]);
            PC += 2;
            m = 3; t = 12;
        }

        /// <summary>
        /// 32 "LD (HL-),A": Save A to address pointed by HL and decrement HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aHLd_A()
        {
            Memory[(H << 8) + L] = A;
            L--;
            if (L == 255)
                H--;
            m = 1; t = 8;
        }

        /// <summary>
        /// 33 "INC SP": Increment 16-bit SP, no flags are set on 16-bit operation
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_SP()
        {
            SP++;
            m = 1; t = 8;
        }

        /// <summary>
        /// 34 "INC (HL)": Increment value pointed by HL, set F(Z0H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_aHL()
        {
            int HL = (H << 8) + L;
            byte tmp = Memory[HL];
            tmp++;
            Memory[HL] = tmp;
            F &= 0x10; // C is not changed, N is set to 0
            if (tmp == 0)
                F |= 0x80; // Z = 1
            if ((tmp & 0xF) == 0)
                F |= 0x20; // H = 1
            m = 1; t = 12;
        }

        /// <summary>
        /// 35 "DEC (HL)": Decrement value pointed by HL, set F(Z1H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_aHL()
        {
            int HL = (H << 8) + L;
            byte tmp = Memory[HL];
            tmp--;
            Memory[HL] = tmp;
            F = (byte)((F & 0x10) | 0x40); // C is not changed, N is set to 1
            if (tmp == 0)
                F |= 0x80; // Z = 1
            if ((tmp & 0xF) == 0xF)
                F |= 0x20; // H = 1
            m = 1; t = 12;
        }

        /// <summary>
        /// 36 "LD (HL),d8": Load 8-bit immediate into address pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aHL_d8()
        {
            int HL = (H << 8) + L;
            Memory[HL] = Memory[PC];
            PC++;
            m = 2; t = 12;
        }

        /// <summary>
        /// 37 "SCF": Set carry flag, set F(-001)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SCF()
        {
            F &= 0x80; // Z is not changed, N, H and C are set to 0
            F |= 0x10; // C is set to 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 38 "JR C,r8": Relative jump by signed immediate if last result caused carry
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JR_C_r8()
        {
            m = 2; t = 8;
            if ((F & 0x10) == 0x10)
            {
                int jump = Memory[PC];
                if (jump > 127)
                    jump = -((~jump + 1) & 255);
                PC = (ushort)(PC + jump);
                t += 4;
            }
            PC++;
        }

        /// <summary>
        /// 39 "ADD HL,SP": Add 16-bit SP to HL, set F(-0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_HL_SP()
        {
            F &= 0x80; // Z is not changed, N is set to 0
            int HL = (H << 8) + L;
            int halfAdd = (HL & 0x0FFF) + (SP & 0x0FFF);
            int tmp = HL + SP;
            HL = (ushort)tmp;
            if (tmp > 0xFFFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0FFF)
                F |= 0x20; // H = 1
            H = (byte)((HL >> 8) & 0xFF);
            L = (byte)(HL & 0xFF);
            m = 1; t = 8;
        }

        /// <summary>
        /// 3A "LD A,(HL-)": Load A from address pointed to by HL, and decrement HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_aHLd()
        {
            int HL = (H << 8) + L;
            A = Memory[HL];
            L--;
            if (L == 255)
                H--;
            m = 1; t = 8;
        }

        /// <summary>
        /// 3B "DEC SP": Decrement 16-bit SP
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_SP()
        {
            SP--;
            m = 1; t = 8;
        }

        /// <summary>
        /// 3C "INC A": Increment 8-bit A, set F(Z0H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INC_A()
        {
            A++;
            F &= 0x10; // C is not changed, N is set to 0
            if (A == 0)
                F |= 0x80; // Z = 1
            if ((A & 0xF) == 0)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 3D "DEC A": Decrement 8-bit A, set F(Z1H-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DEC_A()
        {
            A--;
            F = (byte)((F & 0x10) | 0x40); // C is not changed, N is set to 1
            if (A == 0)
                F |= 0x80; // Z = 1
            if ((A & 0xF) == 0xF)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 3E "LD A,d8": Load 8-bit immediate into A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_d8()
        {
            A = Memory[PC];
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// 3F "CCF": Clear carry flag, set F(-00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CCF()
        {
            F ^= 0x10; // flip carry
            F &= 0x90; // Z and C are not changed, N and H are set to 0
            m = 1; t = 4;
        }

        /// <summary>
        /// 40 "LD B,B": Copy B to B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_B_B()
        {
            // B = B;
            m = 1; t = 4;
        }

        /// <summary>
        /// 41 "LD B,C": Copy C to B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_B_C()
        {
            B = C;
            m = 1; t = 4;
        }

        /// <summary>
        /// 42 "LD B,D": Copy D to B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_B_D()
        {
            B = D;
            m = 1; t = 4;
        }

        /// <summary>
        /// 43 "LD B,E": Copy E to B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_B_E()
        {
            B = E;
            m = 1; t = 4;
        }

        /// <summary>
        /// 44 "LD B,H": Copy H to B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_B_H()
        {
            B = H;
            m = 1; t = 4;
        }

        /// <summary>
        /// 45 "LD B,L": Copy L to B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_B_L()
        {
            B = L;
            m = 1; t = 4;
        }

        /// <summary>
        /// 46 "LD B,(HL)": Copy value pointed by HL to B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_B_aHL()
        {
            int HL = (H << 8) + L;
            B = Memory[HL];
            m = 1; t = 8;
        }

        /// <summary>
        /// 47 "LD B,A": Copy A to B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_B_A()
        {
            B = A;
            m = 1; t = 4;
        }

        /// <summary>
        /// 48 "LD C,B": Copy B to C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_C_B()
        {
            C = B;
            m = 1; t = 4;
        }

        /// <summary>
        /// 49 "LD C,C": Copy C to C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_C_C()
        {
            // C = C;
            m = 1; t = 4;
        }

        /// <summary>
        /// 4A "LD C,D": Copy D to C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_C_D()
        {
            C = D;
            m = 1; t = 4;
        }

        /// <summary>
        /// 4B "LD C,E": Copy E to C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_C_E()
        {
            C = E;
            m = 1; t = 4;
        }

        /// <summary>
        /// 4C "LD C,H": Copy H to C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_C_H()
        {
            C = H;
            m = 1; t = 4;
        }

        /// <summary>
        /// 4D "LD C,L": Copy L to C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_C_L()
        {
            C = L;
            m = 1; t = 4;
        }

        /// <summary>
        /// 4E "LD C,(HL)": Copy value pointed by HL to C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_C_aHL()
        {
            int HL = (H << 8) + L;
            C = Memory[HL];
            m = 1; t = 8;
        }

        /// <summary>
        /// 4F "LD C,A": Copy A to C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_C_A()
        {
            C = A;
            m = 1; t = 4;
        }

        /// <summary>
        /// 50 "LD D,B": Copy B to D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_D_B()
        {
            D = B;
            m = 1; t = 4;
        }

        /// <summary>
        /// 51 "LD D,C": Copy C to D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_D_C()
        {
            D = C;
            m = 1; t = 4;
        }

        /// <summary>
        /// 52 "LD D,D": Copy D to D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_D_D()
        {
            // D = D;
            m = 1; t = 4;
        }

        /// <summary>
        /// 53 "LD D,E": Copy E to D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_D_E()
        {
            D = E;
            m = 1; t = 4;
        }

        /// <summary>
        /// 54 "LD D,H": Copy H to D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_D_H()
        {
            D = H;
            m = 1; t = 4;
        }

        /// <summary>
        /// 55 "LD D,L": Copy L to D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_D_L()
        {
            D = L;
            m = 1; t = 4;
        }

        /// <summary>
        /// 56 "LD D,(HL)": Copy value pointed by HL to D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_D_aHL()
        {
            int HL = (H << 8) + L;
            D = Memory[HL];
            m = 1; t = 8;
        }

        /// <summary>
        /// 57 "LD D,A": Copy A to D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_D_A()
        {
            D = A;
            m = 1; t = 4;
        }

        /// <summary>
        /// 58 "LD E,B": Copy B to E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_E_B()
        {
            E = B;
            m = 1; t = 4;
        }

        /// <summary>
        /// 59 "LD E,C": Copy C to E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_E_C()
        {
            E = C;
            m = 1; t = 4;
        }

        /// <summary>
        /// 5A "LD E,D": Copy D to E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_E_D()
        {
            E = D;
            m = 1; t = 4;
        }

        /// <summary>
        /// 5B "LD E,E": Copy E to E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_E_E()
        {
            // E = E;
            m = 1; t = 4;
        }

        /// <summary>
        /// 5C "LD E,H": Copy H to E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_E_H()
        {
            E = H;
            m = 1; t = 4;
        }

        /// <summary>
        /// 5D "LD E,L": Copy L to E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_E_L()
        {
            E = L;
            m = 1; t = 4;
        }

        /// <summary>
        /// 5E "LD E,(HL)": Copy value pointed by HL to E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_E_aHL()
        {
            int HL = (H << 8) + L;
            E = Memory[HL];
            m = 1; t = 8;
        }

        /// <summary>
        /// 5F "LD E,A": Copy A to E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_E_A()
        {
            E = A;
            m = 1; t = 4;
        }

        /// <summary>
        /// 60 "LD H,B": Copy B to H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_H_B()
        {
            H = B;
            m = 1; t = 4;
        }

        /// <summary>
        /// 61 "LD H,C": Copy C to H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_H_C()
        {
            H = C;
            m = 1; t = 4;
        }

        /// <summary>
        /// 62 "LD H,D": Copy D to H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_H_D()
        {
            H = D;
            m = 1; t = 4;
        }

        /// <summary>
        /// 63 "LD H,E": Copy E to H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_H_E()
        {
            H = E;
            m = 1; t = 4;
        }

        /// <summary>
        /// 64 "LD H,H": Copy H to H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_H_H()
        {
            // H = H;
            m = 1; t = 4;
        }

        /// <summary>
        /// 65 "LD H,L": Copy L to H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_H_L()
        {
            H = L;
            m = 1; t = 4;
        }

        /// <summary>
        /// 66 "LD H,(HL)": Copy value pointed by HL to H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_H_aHL()
        {
            int HL = (H << 8) + L;
            H = Memory[HL];
            m = 1; t = 8;
        }

        /// <summary>
        /// 67 "LD H,A": Copy A to H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_H_A()
        {
            H = A;
            m = 1; t = 4;
        }

        /// <summary>
        /// 68 "LD L,B": Copy B to L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_L_B()
        {
            L = B;
            m = 1; t = 4;
        }

        /// <summary>
        /// 69 "LD L,C": Copy C to L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_L_C()
        {
            L = C;
            m = 1; t = 4;
        }

        /// <summary>
        /// 6A "LD L,D": Copy D to L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_L_D()
        {
            L = D;
            m = 1; t = 4;
        }

        /// <summary>
        /// 6B "LD L,E": Copy E to L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_L_E()
        {
            L = E;
            m = 1; t = 4;
        }

        /// <summary>
        /// 6C "LD L,H": Copy H to L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_L_H()
        {
            L = H;
            m = 1; t = 4;
        }

        /// <summary>
        /// 6D "LD L,L": Copy L to L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_L_L()
        {
            // L = L;
            m = 1; t = 4;
        }

        /// <summary>
        /// 6E "LD L,(HL)": Copy value pointed by HL to L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_L_aHL()
        {
            int HL = (H << 8) + L;
            L = Memory[HL];
            m = 1; t = 8;
        }

        /// <summary>
        /// 6F "LD L,A": Copy A to L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_L_A()
        {
            L = A;
            m = 1; t = 4;
        }

        /// <summary>
        /// 70 "LD (HL),B": Copy B to address pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aHL_B()
        {
            int HL = (H << 8) + L;
            Memory[HL] = B;
            m = 1; t = 8;
        }

        /// <summary>
        /// 71 "LD (HL),C": Copy C to address pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aHL_C()
        {
            int HL = (H << 8) + L;
            Memory[HL] = C;
            m = 1; t = 8;
        }

        /// <summary>
        /// 72 "LD (HL),D": Copy D to address pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aHL_D()
        {
            int HL = (H << 8) + L;
            Memory[HL] = D;
            m = 1; t = 8;
        }

        /// <summary>
        /// 73 "LD (HL),E": Copy E to address pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aHL_E()
        {
            int HL = (H << 8) + L;
            Memory[HL] = E;
            m = 1; t = 8;
        }

        /// <summary>
        /// 74 "LD (HL),H": Copy H to address pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aHL_H()
        {
            int HL = (H << 8) + L;
            Memory[HL] = H;
            m = 1; t = 8;
        }

        /// <summary>
        /// 75 "LD (HL),L": Copy L to address pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aHL_L()
        {
            int HL = (H << 8) + L;
            Memory[HL] = L;
            m = 1; t = 8;
        }

        /// <summary>
        /// 76 "HALT": Halt CPU
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HALT()
        {
            m = 1; t = 8;
            IsHalted = true;
        }

        /// <summary>
        /// 77 "LD (HL),A": Copy A to address pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aHL_A()
        {
            int HL = (H << 8) + L;
            Memory[HL] = A;
            m = 1; t = 8;
        }

        /// <summary>
        /// 78 "LD A,B": Copy B to A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_B()
        {
            A = B;
            m = 1; t = 4;
        }

        /// <summary>
        /// 79 "LD A,C": Copy C to A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_C()
        {
            A = C;
            m = 1; t = 4;
        }

        /// <summary>
        /// 7A "LD A,D": Copy D to A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_D()
        {
            A = D;
            m = 1; t = 4;
        }

        /// <summary>
        /// 7B "LD A,E": Copy E to A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_E()
        {
            A = E;
            m = 1; t = 4;
        }

        /// <summary>
        /// 7C "LD A,H": Copy H to A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_H()
        {
            A = H;
            m = 1; t = 4;
        }

        /// <summary>
        /// 7D "LD A,L": Copy L to A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_L()
        {
            A = L;
            m = 1; t = 4;
        }

        /// <summary>
        /// 7E "LD A,(HL)": Copy value pointed by HL to A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_aHL()
        {
            int HL = (H << 8) + L;
            A = Memory[HL];
            m = 1; t = 8;
        }

        /// <summary>
        /// 7F "LD A,A": Copy A to A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_A()
        {
            // A = A;
            m = 1; t = 4;
        }

        /// <summary>
        /// 80 "ADD A,B": Add B to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_A_B()
        {
            F = 0;
            int halfAdd = (A & 0x0F) + (B & 0x0F);
            int tmp = A + B;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 81 "ADD A,C": Add C to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_A_C()
        {
            F = 0;
            int halfAdd = (A & 0x0F) + (C & 0x0F);
            int tmp = A + C;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 82 "ADD A,D": Add D to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_A_D()
        {
            F = 0;
            int halfAdd = (A & 0x0F) + (D & 0x0F);
            int tmp = A + D;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 83 "ADD A,E": Add E to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_A_E()
        {
            F = 0;
            int halfAdd = (A & 0x0F) + (E & 0x0F);
            int tmp = A + E;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 84 "ADD A,H": Add H to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_A_H()
        {
            F = 0;
            int halfAdd = (A & 0x0F) + (H & 0x0F);
            int tmp = A + H;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 85 "ADD A,L": Add L to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_A_L()
        {
            F = 0;
            int halfAdd = (A & 0x0F) + (L & 0x0F);
            int tmp = A + L;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 86 "ADD A,(HL)": Add value pointed by HL to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_A_aHL()
        {
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            int halfAdd = (A & 0x0F) + (dHL & 0x0F);
            int tmp = A + dHL;
            A += dHL;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 8;
        }

        /// <summary>
        /// 87 "ADD A,A": Add A to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_A_A()
        {
            F = 0;
            int halfAdd = (A & 0x0F) + (A & 0x0F);
            int tmp = A + A;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 88 "ADC A,B": Add B and carry flag to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADC_A_B()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            int halfAdd = (A & 0x0F) + (B & 0x0F) + carry;
            int tmp = A + B + carry;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 89 "ADC A,C": Add C and carry flag to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADC_A_C()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            int halfAdd = (A & 0x0F) + (C & 0x0F) + carry;
            int tmp = A + C + carry;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 8A "ADC A,D": Add D and carry flag to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADC_A_D()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            int halfAdd = (A & 0x0F) + (D & 0x0F) + carry;
            int tmp = A + D + carry;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 8B "ADC A,E": Add E and carry flag to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADC_A_E()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            int halfAdd = (A & 0x0F) + (E & 0x0F) + carry;
            int tmp = A + E + carry;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 8C "ADC A,H": Add H and carry flag to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADC_A_H()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            int halfAdd = (A & 0x0F) + (H & 0x0F) + carry;
            int tmp = A + H + carry;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 8D "ADC A,L": Add L and carry flag to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADC_A_L()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            int halfAdd = (A & 0x0F) + (L & 0x0F) + carry;
            int tmp = A + L + carry;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 8E "ADC A,(HL)": Add value pointed by HL and carry flag to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADC_A_aHL()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            int halfAdd = (A & 0x0F) + (dHL & 0x0F) + carry;
            int tmp = A + dHL + carry;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 8;
        }

        /// <summary>
        /// 8F "ADC A,A": Add A and carry flag to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADC_A_A()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            int halfAdd = (A & 0x0F) + (A & 0x0F) + carry;
            int tmp = A + A + carry;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 90 "SUB A, B": Subtract B from A, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SUB_A_B()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (B & 0x0F));
            ushort tmp = (ushort)(A - B);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 91 "SUB A, C": Subtract C from A, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SUB_A_C()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (C & 0x0F));
            ushort tmp = (ushort)(A - C);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 92 "SUB A, D": Subtract D from A, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SUB_A_D()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (D & 0x0F));
            ushort tmp = (ushort)(A - D);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 93 "SUB A, E": Subtract E from A, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SUB_A_E()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (E & 0x0F));
            ushort tmp = (ushort)(A - E);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 94 "SUB A, H": Subtract H from A, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SUB_A_H()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (H & 0x0F));
            ushort tmp = (ushort)(A - H);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 95 "SUB A, L": Subtract L from A, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SUB_A_L()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (L & 0x0F));
            ushort tmp = (ushort)(A - L);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 96 "SUB A, (HL)": Subtract value pointed by HL from A, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SUB_A_aHL()
        {
            F = 0x40;
            byte dHL = Memory[(H << 8) + L];
            ushort halfAdd = (ushort)((A & 0x0F) - (dHL & 0x0F));
            ushort tmp = (ushort)(A - dHL);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 8;
        }

        /// <summary>
        /// 97 "SUB A, A": Subtract A from A, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SUB_A_A()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (A & 0x0F));
            ushort tmp = (ushort)(A - A);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 98 "SBC A,B": Subtract B and carry flag from A, set, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SBC_A_B()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (B & 0x0F) - carry);
            ushort tmp = (ushort)(A - B - carry);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 99 "SBC A,C": Subtract C and carry flag from A, set, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SBC_A_C()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (C & 0x0F) - carry);
            ushort tmp = (ushort)(A - C - carry);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 9A "SBC A,D": Subtract D and carry flag from A, set, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SBC_A_D()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (D & 0x0F) - carry);
            ushort tmp = (ushort)(A - D - carry);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 9B "SBC A,E": Subtract E and carry flag from A, set, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SBC_A_E()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (E & 0x0F) - carry);
            ushort tmp = (ushort)(A - E - carry);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 9C "SBC A,H": Subtract H and carry flag from A, set, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SBC_A_H()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (H & 0x0F) - carry);
            ushort tmp = (ushort)(A - H - carry);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 9D "SBC A,L": Subtract L and carry flag from A, set, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SBC_A_L()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (L & 0x0F) - carry);
            ushort tmp = (ushort)(A - L - carry);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// 9E "SBC A,(HL)": Subtract the value pointed by HL and carry flag from A, set, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SBC_A_aHL()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0x40;
            byte dHL = Memory[(H << 8) + L];
            ushort halfAdd = (ushort)((A & 0x0F) - (dHL & 0x0F) - carry);
            ushort tmp = (ushort)(A - dHL - carry);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 8;
        }

        /// <summary>
        /// 9F "SBC A,A": Subtract A and carry flag from A, set, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SBC_A_A()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (A & 0x0F) - carry);
            ushort tmp = (ushort)(A - A - carry);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// A0 "AND A,B": Logical AND B against A, set(Z010)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AND_A_B()
        {
            F = 0x20;
            A &= B;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// A1 "AND A,C": Logical AND C against A, set(Z010)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AND_A_C()
        {
            F = 0x20;
            A &= C;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// A2 "AND A,D": Logical AND D against A, set(Z010)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AND_A_D()
        {
            F = 0x20;
            A &= D;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// A3 "AND A,E": Logical AND E against A, set(Z010)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AND_A_E()
        {
            F = 0x20;
            A &= E;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// A4 "AND A,H": Logical AND H against A, set(Z010)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AND_A_H()
        {
            F = 0x20;
            A &= H;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// A5 "AND A,L": Logical AND L against A, set(Z010)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AND_A_L()
        {
            F = 0x20;
            A &= L;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// A6 "AND A,(HL)": Logical AND the value pointed by HL against A, set(Z010)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AND_A_aHL()
        {
            F = 0x20;
            byte dHL = Memory[(H << 8) + L];
            A &= dHL;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 8;
        }

        /// <summary>
        /// A7 "AND A,A": Logical AND A against A, set(Z010)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AND_A_A()
        {
            F = 0x20;
            // A &= A;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// A8 "XOR A,B": Logical XOR B against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void XOR_A_B()
        {
            F = 0;
            A ^= B;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// A9 "XOR A,C": Logical XOR C against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void XOR_A_C()
        {
            F = 0;
            A ^= C;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// AA "XOR A,D": Logical XOR D against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void XOR_A_D()
        {
            F = 0;
            A ^= D;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// AB "XOR A,E": Logical XOR E against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void XOR_A_E()
        {
            F = 0;
            A ^= E;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// AC "XOR A,H": Logical XOR H against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void XOR_A_H()
        {
            F = 0;
            A ^= H;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// AD "XOR A,L": Logical XOR L against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void XOR_A_L()
        {
            F = 0;
            A ^= L;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// AE "XOR A,(HL)": Logical XOR value pointed by HL against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void XOR_A_aHL()
        {
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            A ^= dHL;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 8;
        }

        /// <summary>
        /// AF "XOR A,A": Logical XOR A against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void XOR_A_A()
        {
            F = 0;
            A ^= A;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// B0 "OR A,B": Logical OR B against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OR_A_B()
        {
            F = 0;
            A |= B;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// B1 "OR A,C": Logical OR C against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OR_A_C()
        {
            F = 0;
            A |= C;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// B2 "OR A,D": Logical OR D against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OR_A_D()
        {
            F = 0;
            A |= D;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// B3 "OR A,E": Logical OR E against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OR_A_E()
        {
            F = 0;
            A |= E;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// B4 "OR A,H": Logical OR H against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OR_A_H()
        {
            F = 0;
            A |= H;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// B5 "OR A,L": Logical OR L against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OR_A_L()
        {
            F = 0;
            A |= L;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// B6 "OR A,(HL)": Logical OR value pointed by HL against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OR_A_aHL()
        {
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            A |= dHL;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 8;
        }

        /// <summary>
        /// B7 "OR A,A": Logical OR A against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OR_A_A()
        {
            F = 0;
            // A |= A;
            if (A == 0)
                F |= 0x80;
            m = 1; t = 4;
        }

        /// <summary>
        /// B8 "CP A,B": Compare B against A, set(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CP_A_B()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (B & 0x0F));
            ushort tmp = (ushort)(A - B);
            if ((byte)tmp == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// B9 "CP A,C": Compare C against A, set(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CP_A_C()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (C & 0x0F));
            ushort tmp = (ushort)(A - C);
            if ((byte)tmp == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// BA "CP A,D": Compare D against A, set(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CP_A_D()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (D & 0x0F));
            ushort tmp = (ushort)(A - D);
            if ((byte)tmp == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// BB "CP A,E": Compare E against A, set(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CP_A_E()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (E & 0x0F));
            ushort tmp = (ushort)(A - E);
            if ((byte)tmp == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// BC "CP A,H": Compare H against A, set(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CP_A_H()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (H & 0x0F));
            ushort tmp = (ushort)(A - H);
            if ((byte)tmp == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// BD "CP A,L": Compare L against A, set(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CP_A_L()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (L & 0x0F));
            ushort tmp = (ushort)(A - L);
            if ((byte)tmp == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// BE "CP A,(HL)": Compare the value pointed by HL against A, set(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CP_A_aHL()
        {
            F = 0x40;
            byte dHL = Memory[(H << 8) + L];
            ushort halfAdd = (ushort)((A & 0x0F) - (dHL & 0x0F));
            ushort tmp = (ushort)(A - dHL);
            if ((byte)tmp == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 8;
        }

        /// <summary>
        /// BF "CP A,A": Compare A against A, set(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CP_A_A()
        {
            F = 0x40;
            ushort halfAdd = (ushort)((A & 0x0F) - (A & 0x0F));
            ushort tmp = (ushort)(A - A);
            if ((byte)tmp == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            m = 1; t = 4;
        }

        /// <summary>
        /// C0 "RET NZ": Return if last result was not zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RET_NZ()
        {
            m = 1; t = 8;
            if ((F & 0x80) == 0)
            {
                PC = (ushort)(Memory[SP] + (Memory[SP + 1] << 8));
                SP += 2;
                t += 12;
            }
        }

        /// <summary>
        /// C1 "POP BC": Pop 16-bit value from stack into BC
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void POP_BC()
        {
            B = Memory[SP + 1];
            C = Memory[SP];
            SP += 2;
            m = 1; t = 12;
        }

        /// <summary>
        /// C2 "JP NZ,a16": Absolute jump to 16-bit location if last result was not zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JP_NZ_a16()
        {
            m = 3; t = 12;
            if ((F & 0x80) == 0)
            {
                PC = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
                t += 4;
            }
            else
                PC += 2;
        }

        /// <summary>
        /// C3 "JP a16": Absolute jump to 16-bit location
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JP_a16()
        {
            PC = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
            m = 3; t = 16;
        }

        /// <summary>
        /// C4 "CALL NZ,a16": Call routine at 16-bit location if last result was not zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CALL_NZ_a16()
        {
            m = 3; t = 12;
            if ((F & 0x80) == 0)
            {
                t += 12;
                SP -= 2;
                ushort PC2 = (ushort)(PC + 2);
                Memory[SP] = (byte)(PC2 & 0xFF);
                Memory[SP + 1] = (byte)(PC2 >> 8);
                PC = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
            }
            else
                PC += 2;
        }

        /// <summary>
        /// C5 "PUSH BC": Push 16-bit BC onto stack
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PUSH_BC()
        {
            SP -= 2;
            Memory[SP] = C;
            Memory[SP + 1] = B;
            m = 1; t = 16;
        }

        /// <summary>
        /// C6 "ADD A,d8": Add 8-bit immediate to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_A_d8()
        {
            F = 0;
            byte d8 = Memory[PC];
            int halfAdd = (A & 0x0F) + (d8 & 0x0F);
            int tmp = A + d8;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// C7 "RST 00H": Call routine at address 0000h
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RST_00H()
        {
            SP -= 2;
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);
            PC = 0x00;
            m = 1; t = 16;
        }

        /// <summary>
        /// C8 "RET Z": Return if last result was zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RET_Z()
        {
            m = 1; t = 8;
            if ((F & 0x80) == 0x80)
            {
                PC = (ushort)(Memory[SP] + (Memory[SP + 1] << 8));
                SP += 2;
                t += 12;
            }
        }

        /// <summary>
        /// C9 "RET": Return
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RET()
        {
            m = 1; t = 16;
            PC = (ushort)(Memory[SP] + (Memory[SP + 1] << 8));
            SP += 2;
        }

        /// <summary>
        /// CA "JP Z,a16": Absolute jump to 16-bit location if last result was zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JP_Z_a16()
        {
            m = 3; t = 12;
            if ((F & 0x80) == 0x80)
            {
                PC = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
                t += 4;
            }
            else
                PC += 2;
        }

        // CB : CB prefixed instruction set
        // While the documentation states that CB is 1 byte and 4 cycles long,
        // it actually doesn't use any cycle (in fact, its lenght is already taken in account in the CB instructions' timings).   

        /// <summary>
        /// CC "CALL Z,a16": Call routine at 16-bit location if last result was zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CALL_Z_a16()
        {
            m = 3; t = 12;
            if ((F & 0x80) == 0x80)
            {
                t += 12;
                SP -= 2;
                ushort PC2 = (ushort)(PC + 2);
                Memory[SP] = (byte)(PC2 & 0xFF);
                Memory[SP + 1] = (byte)(PC2 >> 8);
                PC = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
            }
            else
                PC += 2;
        }

        /// <summary>
        /// CD "CALL a16": Call routine at 16-bit location
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CALL_a16()
        {
            m = 3; t = 24;
            SP -= 2;
            ushort PC2 = (ushort)(PC + 2);
            Memory[SP] = (byte)(PC2 & 0xFF);
            Memory[SP + 1] = (byte)(PC2 >> 8);
            PC = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
        }

        /// <summary>
        /// CE "ADC A,d8": Add 8-bit immediate and carry flag to A, set F(Z0HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADC_A_d8()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            byte d8 = Memory[PC];
            int halfAdd = (A & 0x0F) + (d8 & 0x0F) + carry;
            int tmp = A + d8 + carry;
            A = (byte)tmp;
            if (A == 0)
                F = 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// CF "RST 08H": Call routine at address 0008h
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RST_08H()
        {
            SP -= 2;
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);
            PC = 0x08;
            m = 1; t = 16;
        }

        /// <summary>
        /// D0 "RET NC": Return if last result had no carry
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RET_NC()
        {
            m = 1; t = 8;
            if ((F & 0x10) == 0)
            {
                PC = (ushort)(Memory[SP] + (Memory[SP + 1] << 8));
                SP += 2;
                t += 12;
            }
        }

        /// <summary>
        /// D1 "POP DE": Pop 16-bit value from stack into DE
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void POP_DE()
        {
            D = Memory[SP + 1];
            E = Memory[SP];
            SP += 2;
            m = 1; t = 12;
        }

        /// <summary>
        /// D2 "JP NC,a16": Absolute jump to 16-bit location if last result had no carry
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JP_NC_a16()
        {
            m = 3; t = 12;
            if ((F & 0x10) == 0)
            {
                PC = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
                t += 4;
            }
            else
                PC += 2;
        }

        // D3 no instruction

        /// <summary>
        /// D4 "CALL NC,a16": Call routine at 16-bit location if last result had no carry
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CALL_NC_a16()
        {
            m = 3; t = 12;
            if ((F & 0x10) == 0)
            {
                t += 12;
                SP -= 2;
                ushort PC2 = (ushort)(PC + 2);
                Memory[SP] = (byte)(PC2 & 0xFF);
                Memory[SP + 1] = (byte)(PC2 >> 8);
                PC = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
            }
            else
                PC += 2;
        }

        /// <summary>
        /// D5 "PUSH DE": Push 16-bit DE onto stack
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PUSH_DE()
        {
            SP -= 2;
            Memory[SP] = E;
            Memory[SP + 1] = D;
            m = 1; t = 16;
        }

        /// <summary>
        /// D6 "SUB A,d8": Subtract 8-bit immediate from A, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SUB_A_d8()
        {
            F = 0x40;
            byte d8 = Memory[PC];
            ushort halfAdd = (ushort)((A & 0x0F) - (d8 & 0x0F));
            ushort tmp = (ushort)(A - d8);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// D7 "RST 10H": Call routine at address 0010h
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RST_10H()
        {
            SP -= 2;
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);
            PC = 0x10;
            m = 1; t = 16;
        }

        /// <summary>
        /// D8 "RET C": Return if last result had carry
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RET_C()
        {
            m = 1; t = 8;
            if ((F & 0x10) == 0x10)
            {
                PC = (ushort)(Memory[SP] + (Memory[SP + 1] << 8));
                SP += 2;
                t += 12;
            }
        }

        /// <summary>
        /// D9 "RETI": Enable interrupts and return to calling routine
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RETI()
        {
            m = 1; t = 16;
            PC = (ushort)(Memory[SP] + (Memory[SP + 1] << 8));
            SP += 2;
            IME = 2;
        }

        /// <summary>
        /// DA "JP C,a16": Absolute jump to 16-bit location if last result had a carry
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JP_C_a16()
        {
            m = 3; t = 12;
            if ((F & 0x10) == 0x10)
            {
                PC = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
                t += 4;
            }
            else
                PC += 2;
        }

        // DB no instruction

        /// <summary>
        /// DC "CALL C,a16": Call routine at 16-bit location if last result had a carry
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CALL_C_a16()
        {
            m = 3; t = 12;
            if ((F & 0x10) == 0x10)
            {
                t += 12;
                SP -= 2;
                ushort PC2 = (ushort)(PC + 2);
                Memory[SP] = (byte)(PC2 & 0xFF);
                Memory[SP + 1] = (byte)(PC2 >> 8);
                PC = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
            }
            else
                PC += 2;
        }

        // DD no instruction

        /// <summary>
        /// DE "SBC A,d8": Subtract 8-bit immediate and carry flag from A, set, set F(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SBC_A_d8()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0x40;
            byte d8 = Memory[PC];
            ushort halfAdd = (ushort)((A & 0x0F) - (d8 & 0x0F) - carry);
            ushort tmp = (ushort)(A - d8 - carry);
            A = (byte)tmp;
            if (A == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// DF "RST 18H": Call routine at address 0018h
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RST_18H()
        {
            SP -= 2;
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);
            PC = 0x18;
            m = 1; t = 16;
        }

        /// <summary>
        /// E0 "LDH (a8),A": Save A at address pointed to by (FF00h + 8-bit immediate)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LDH_a8_A()
        {
            Memory[Memory[PC] + 0xFF00] = A;
            PC++;
            m = 2; t = 12;
        }

        /// <summary>
        /// E1 "POP HL": Pop 16-bit value from stack into HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void POP_HL()
        {
            H = Memory[SP + 1];
            L = Memory[SP];
            SP += 2;
            m = 1; t = 12;
        }

        /// <summary>
        /// E2 "LD (C),A": Save A at address pointed to by (FF00h + C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_aC_A()
        {
            Memory[C + 0xFF00] = A;
            //PC++;
            m = 1; t = 8;
        }

        // E3 no instruction

        // E4 no instruction

        /// <summary>
        /// E5 "PUSH HL": Push 16-bit HL onto stack
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PUSH_HL()
        {
            SP -= 2;
            Memory[SP] = L;
            Memory[SP + 1] = H;
            m = 1; t = 16;
        }

        /// <summary>
        /// E6 "AND A,d8": Logical AND 8-bit immmediate against A, set(Z010)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AND_A_d8()
        {
            F = 0x20;
            A &= Memory[PC];
            if (A == 0)
                F |= 0x80;
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// E7 "RST 20H": Call routine at address 0020h
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RST_20H()
        {
            SP -= 2;
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);
            PC = 0x20;
            m = 1; t = 16;
        }

        /// <summary>
        /// E8 "ADD SP,r8": Add 8-bit signed immediate to SP, set F(00HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ADD_SP_r8()
        {
            F = 0x0;
            ushort r8 = (ushort)(sbyte)Memory[PC];
            /*
            int halfAdd = (SP & 0x0F) + (r8 & 0x0F);
            int tmp = (SP & 0xFF) + r8;
            SP = (ushort)(SP + r8);
            if (tmp > 0xFF || SP == 0)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F || SP == 0)
                F |= 0x20; // H = 1
            */
            int tmp = SP + r8;
            int xor = SP ^ r8 ^ tmp;
            if ((xor & 0x10) == 0x10)
                F |= 0x20;
            if ((xor & 0x100) == 0x100)
                F |= 0x10;
            SP = (ushort)tmp;
            PC++;
            m = 2; t = 16;
        }

        /// <summary>
        /// E9 "JP HL": Jump to 16-bit value of HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void JP_HL()
        {
            int HL = (H << 8) + L;
            //PC = (ushort)((Memory[HL + 1] << 8) + Memory[HL]);
            PC = (ushort)HL;
            m = 1; t = 4;
        }

        /// <summary>
        /// EA "LD (a16),A": Save A at given 16-bit address
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_a16_A()
        {
            ushort a16 = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
            if (a16 == 0x7FF4)
                Console.WriteLine("k");
            Memory[a16] = A;
            PC += 2;
            m = 3; t = 16;
        }

        // EB no instruction

        // EC no instruction

        // ED no instruction

        /// <summary>
        /// EE "XOR A,d8": Logical XOR 8-bit immediate against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void XOR_A_d8()
        {
            F = 0;
            A ^= Memory[PC];
            if (A == 0)
                F |= 0x80;
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// EF "RST 28H": Call routine at address 0028h
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RST_28H()
        {
            SP -= 2;
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);
            PC = 0x28;
            m = 1; t = 16;
        }

        /// <summary>
        /// F0 "LDH A,(a8)": Load A from address pointed to by (FF00h + 8-bit immediate)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LDH_A_a8()
        {
            byte n = Memory[PC];
            A = Memory[n + 0xFF00];
            PC++;
            m = 2; t = 12;
        }

        /// <summary>
        /// F1 "POP AF": Pop 16-bit value from stack into AF
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void POP_AF()
        {
            A = Memory[SP + 1];
            F = (byte)(Memory[SP] & 0xF0); // the four lower bits of F are alwas 0 ! (unused)
            SP += 2;
            m = 1; t = 12;
        }

        /// <summary>
        /// F2 "LD A,(C)": Load A from address pointed to by (FF00h + C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_aC()
        {
            A = Memory[C + 0xFF00];
            //PC++;
            m = 1; t = 8;
        }

        /// <summary>
        /// F3 "DI": DIsable interrupts
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DI()
        {
            IME = 0;
            m = 1; t = 4;
        }

        // F4 no instruction

        /// <summary>
        /// F5 "PUSH AF": Push 16-bit AF onto stack
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PUSH_AF()
        {
            SP -= 2;
            Memory[SP] = F;
            Memory[SP + 1] = A;
            m = 1; t = 16;
        }

        /// <summary>
        /// F6 "OR A,d8": Logical OR 8-bit immediate against A, set(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OR_A_d8()
        {
            F = 0;
            A |= Memory[PC];
            if (A == 0)
                F |= 0x80;
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// F7 "RST 30H": Call routine at address 0030h
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RST_30H()
        {
            SP -= 2;
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);
            PC = 0x30;
            m = 1; t = 16;
        }

        /// <summary>
        /// F8 "LD HL,SP+r8": Add signed 8-bit immediate to SP and save result in HL, set F(00HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_HL_SPr8()
        {
            F = 0;

            ushort r8 = (ushort)(sbyte)Memory[PC];
            int temp = SP + r8;
            if (((SP ^ r8 ^ temp) & 0x10) == 0x10)
                F |= 0x20;
            if (((SP ^ r8 ^ temp) & 0x100) != 0)
                F |= 0x10;
            r8 = (ushort)temp;

            H = (byte)(r8 >> 8);
            L = (byte)(r8 & 0xFF);
            PC++;
            m = 2; t = 12;
        }

        /// <summary>
        /// F9 "LD SP,HL": Copy HL to SP
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_SP_HL()
        {
            SP = (ushort)((H << 8) + L);
            m = 1; t = 8;
        }

        /// <summary>
        /// FA "LD A,(a16)": Load A from given 16-bit address
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LD_A_a16()
        {
            ushort a16 = (ushort)(Memory[PC] + (Memory[PC + 1] << 8));
            A = Memory[a16];
            PC += 2;
            m = 3; t = 16;
        }

        /// <summary>
        /// FB "EI": Enable interrupts
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EI()
        {
            IME = 2;
            m = 1; t = 4;
        }

        // FC no instruction

        // FD no instruction

        /// <summary>
        /// FE "CP A,a8": Compare 8-bit immediate against A, set(Z1HC)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CP_A_a8()
        {
            F = 0x40;
            byte a8 = Memory[PC];
            ushort halfAdd = (ushort)((A & 0x0F) - (a8 & 0x0F));
            ushort tmp = (ushort)(A - a8);
            if ((byte)tmp == 0)
                F |= 0x80; // Z = 1
            if (tmp > 0xFF)
                F |= 0x10; // C = 1
            if (halfAdd > 0x0F)
                F |= 0x20; // H = 1
            PC++;
            m = 2; t = 8;
        }

        /// <summary>
        /// FF "RST 38H": Call routine at address 0038h
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RST_38H()
        {
            SP -= 2;
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);
            PC = 0x38;
            m = 1; t = 16;
        }

        #endregion

        #region CB prefixed instruction set

        /// <summary>
        /// CB00 "RLC B": Rotate B left with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RLC_B()
        {
            F = 0;
            B = (byte)((B << 1) | (B >> 7));
            if ((B & 1) == 1)
                F |= 0x10; // C = 1
            if (B == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB01 "RLC C": Rotate C left with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RLC_C()
        {
            F = 0;
            C = (byte)((C << 1) | (C >> 7));
            if ((C & 1) == 1)
                F |= 0x10; // C = 1
            if (C == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB02 "RLC D": Rotate D left with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RLC_D()
        {
            F = 0;
            D = (byte)((D << 1) | (D >> 7));
            if ((D & 1) == 1)
                F |= 0x10; // C = 1
            if (D == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB03 "RLC E": Rotate E left with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RLC_E()
        {
            F = 0;
            E = (byte)((E << 1) | (E >> 7));
            if ((E & 1) == 1)
                F |= 0x10; // C = 1
            if (E == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB04 "RLC H": Rotate H left with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RLC_H()
        {
            F = 0;
            H = (byte)((H << 1) | (H >> 7));
            if ((H & 1) == 1)
                F |= 0x10; // C = 1
            if (H == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB05 "RLC L": Rotate L left with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RLC_L()
        {
            F = 0;
            L = (byte)((L << 1) | (L >> 7));
            if ((L & 1) == 1)
                F |= 0x10; // C = 1
            if (L == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB06 "RLC (HL)": Rotate the value pointed by HL left with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RLC_aHL()
        {
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            dHL = (byte)((dHL << 1) | (dHL >> 7));
            if ((dHL & 1) == 1)
                F |= 0x10; // C = 1
            if (dHL == 0)
                F |= 0x80; // Z = 0
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB07 "RLC A": Rotate A left with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RLC_A()
        {
            F = 0;
            A = (byte)((A << 1) | (A >> 7));
            if ((A & 1) == 1)
                F |= 0x10; // C = 1
            if (A == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB08 "RRC B": Rotate B right with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RRC_B()
        {
            F = 0;
            B = (byte)((B >> 1) | (B << 7));
            if ((B & 0x80) == 0x80)
                F |= 0x10; // C = 1
            if (B == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB09 "RRC C": Rotate C right with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RRC_C()
        {
            F = 0;
            C = (byte)((C >> 1) | (C << 7));
            if ((C & 0x80) == 0x80)
                F |= 0x10; // C = 1
            if (C == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB0A "RRC D": Rotate D right with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RRC_D()
        {
            F = 0;
            D = (byte)((D >> 1) | (D << 7));
            if ((D & 0x80) == 0x80)
                F |= 0x10; // C = 1
            if (D == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB0B "RRC E": Rotate E right with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RRC_E()
        {
            F = 0;
            E = (byte)((E >> 1) | (E << 7));
            if ((E & 0x80) == 0x80)
                F |= 0x10; // C = 1
            if (E == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB0C "RRC H": Rotate H right with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RRC_H()
        {
            F = 0;
            H = (byte)((H >> 1) | (H << 7));
            if ((H & 0x80) == 0x80)
                F |= 0x10; // C = 1
            if (H == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB0D "RRC L": Rotate L right with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RRC_L()
        {
            F = 0;
            L = (byte)((L >> 1) | (L << 7));
            if ((L & 0x80) == 0x80)
                F |= 0x10; // C = 1
            if (L == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB0E "RRC (HL)": Rotate the value pointed by HL right with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RRC_aHL()
        {
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            dHL = (byte)((dHL >> 1) | (dHL << 7));
            if ((dHL & 0x80) == 0x80)
                F |= 0x10; // C = 1
            if (dHL == 0)
                F |= 0x80; // Z = 0
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB0F "RRC A": Rotate A right with carry, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RRC_A()
        {
            F = 0;
            A = (byte)((A >> 1) | (A << 7));
            if ((A & 0x80) == 0x80)
                F |= 0x10; // C = 1
            if (A == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB10 "RL B": Rotate B left, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RL_B()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            if ((B & 0x80) == 0x80)
                F = 0x10; // C = 1
            B = (byte)((B << 1) | carry);
            if (B == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB11 "RL C": Rotate C left, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RL_C()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            if ((C & 0x80) == 0x80)
                F = 0x10; // C = 1
            C = (byte)((C << 1) | carry);
            if (C == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB12 "RL D": Rotate D left, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RL_D()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            if ((D & 0x80) == 0x80)
                F = 0x10; // C = 1
            D = (byte)((D << 1) | carry);
            if (D == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB13 "RL E": Rotate E left, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RL_E()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            if ((E & 0x80) == 0x80)
                F = 0x10; // C = 1
            E = (byte)((E << 1) | carry);
            if (E == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB14 "RL H": Rotate H left, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RL_H()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            if ((H & 0x80) == 0x80)
                F = 0x10; // C = 1
            H = (byte)((H << 1) | carry);
            if (H == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB15 "RL L": Rotate L left, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RL_L()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            if ((L & 0x80) == 0x80)
                F = 0x10; // C = 1
            L = (byte)((L << 1) | carry);
            if (L == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB16 "RL (HL)": Rotate value pointed by HL left, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RL_aHL()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & 0x80) == 0x80)
                F = 0x10; // C = 1
            dHL = (byte)((dHL << 1) | carry);
            if (dHL == 0)
                F |= 0x80; // Z = 0
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB17 "RL A": Rotate A left, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RL_A()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 1;
            F = 0;
            if ((A & 0x80) == 0x80)
                F = 0x10; // C = 1
            A = (byte)((A << 1) | carry);
            if (A == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB18 "RR B": Rotate B right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RR_B()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 0x80;
            F = 0;
            if ((B & 1) == 1)
                F = 0x10; // C = 1
            B = (byte)((B >> 1) | carry);
            if (B == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB19 "RR C": Rotate C right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RR_C()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 0x80;
            F = 0;
            if ((C & 1) == 1)
                F = 0x10; // C = 1
            C = (byte)((C >> 1) | carry);
            if (C == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB1A "RR D": Rotate D right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RR_D()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 0x80;
            F = 0;
            if ((D & 1) == 1)
                F = 0x10; // C = 1
            D = (byte)((D >> 1) | carry);
            if (D == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB1B "RR E": Rotate E right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RR_E()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 0x80;
            F = 0;
            if ((E & 1) == 1)
                F = 0x10; // C = 1
            E = (byte)((E >> 1) | carry);
            if (E == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB1C "RR H": Rotate H right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RR_H()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 0x80;
            F = 0;
            if ((H & 1) == 1)
                F = 0x10; // C = 1
            H = (byte)((H >> 1) | carry);
            if (H == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB1D "RR L": Rotate L right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RR_L()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 0x80;
            F = 0;
            if ((L & 1) == 1)
                F = 0x10; // C = 1
            L = (byte)((L >> 1) | carry);
            if (L == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB1E "RR (HL)": Rotate value pointed by HL right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RR_aHL()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 0x80;
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & 1) == 1)
                F = 0x10; // C = 1
            dHL = (byte)((dHL >> 1) | carry);
            if (dHL == 0)
                F |= 0x80; // Z = 0
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB1F "RR A": Rotate A right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RR_A()
        {
            byte carry = 0;
            if ((F & 0x10) == 0x10)
                carry = 0x80;
            F = 0;
            if ((A & 1) == 1)
                F = 0x10; // C = 1
            A = (byte)((A >> 1) | carry);
            if (A == 0)
                F |= 0x80; // Z = 0
            m = 2; t = 8;
        }

        /// <summary>
        /// CB20 "SLA B": Shift B left preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SLA_B()
        {
            F = 0;
            if ((B & 0x80) == 0x80)
                F = 0x10; // C = 1
            B <<= 1;
            if (B == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB21 "SLA C": Shift C left preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SLA_C()
        {
            F = 0;
            if ((C & 0x80) == 0x80)
                F = 0x10; // C = 1
            C <<= 1;
            if (C == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB22 "SLA D": Shift D left preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SLA_D()
        {
            F = 0;
            if ((D & 0x80) == 0x80)
                F = 0x10; // C = 1
            D <<= 1;
            if (D == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB23 "SLA E": Shift E left preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SLA_E()
        {
            F = 0;
            if ((E & 0x80) == 0x80)
                F = 0x10; // C = 1
            E <<= 1;
            if (E == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB24 "SLA H": Shift H left preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SLA_H()
        {
            F = 0;
            if ((H & 0x80) == 0x80)
                F = 0x10; // C = 1
            H <<= 1;
            if (H == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB25 "SLA L": Shift L left preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SLA_L()
        {
            F = 0;
            if ((L & 0x80) == 0x80)
                F = 0x10; // C = 1
            L <<= 1;
            if (L == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB26 "SLA (HL)": Shift value pointed by HL left preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SLA_aHL()
        {
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & 0x80) == 0x80)
                F = 0x10; // C = 1
            dHL <<= 1;
            if (dHL == 0)
                F |= 0x80;
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB27 "SLA A": Shift A left preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SLA_A()
        {
            F = 0;
            if ((A & 0x80) == 0x80)
                F = 0x10; // C = 1
            A <<= 1;
            if (A == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB28 "SRA B": Shift B right preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRA_B()
        {
            F = 0;
            if ((B & 1) == 1)
                F = 0x10; // C = 1
            B = (byte)((sbyte)B >> 1);
            if (B == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB29 "SRA C": Shift C right preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRA_C()
        {
            F = 0;
            if ((C & 1) == 1)
                F = 0x10; // C = 1
            C = (byte)((sbyte)C >> 1);
            if (C == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB2A "SRA D": Shift D right preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRA_D()
        {
            F = 0;
            if ((D & 1) == 1)
                F = 0x10; // C = 1
            D = (byte)((sbyte)D >> 1);
            if (D == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB2B "SRA E": Shift E right preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRA_E()
        {
            F = 0;
            if ((E & 1) == 1)
                F = 0x10; // C = 1
            E = (byte)((sbyte)E >> 1);
            if (E == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB2C "SRA H": Shift H right preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRA_H()
        {
            F = 0;
            if ((H & 1) == 1)
                F = 0x10; // C = 1
            H = (byte)((sbyte)H >> 1);
            if (H == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB2D "SRA L": Shift L right preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRA_L()
        {
            F = 0;
            if ((L & 1) == 1)
                F = 0x10; // C = 1
            L = (byte)((sbyte)L >> 1);
            if (L == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB2E "SRA (HL)": Shift value pointed by HL right preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRA_aHL()
        {
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & 1) == 1)
                F = 0x10; // C = 1
            dHL = (byte)((sbyte)dHL >> 1);
            if (dHL == 0)
                F |= 0x80;
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB2F "SRA A": Shift A right preserving sign, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRA_A()
        {
            F = 0;
            if ((A & 1) == 1)
                F = 0x10; // C = 1
            A = (byte)((sbyte)A >> 1);
            if (A == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB30 "SWAP B": Swap nybbles in B, set F(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SWAP_B()
        {
            F = 0;
            B = (byte)((B >> 4) | (B << 4));
            if (B == 0)
                F = 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB31 "SWAP C": Swap nybbles in C, set F(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SWAP_C()
        {
            F = 0;
            C = (byte)((C >> 4) | (C << 4));
            if (C == 0)
                F = 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB32 "SWAP D": Swap nybbles in D, set F(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SWAP_D()
        {
            F = 0;
            D = (byte)((D >> 4) | (D << 4));
            if (D == 0)
                F = 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB33 "SWAP E": Swap nybbles in E, set F(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SWAP_E()
        {
            F = 0;
            E = (byte)((E >> 4) | (E << 4));
            if (E == 0)
                F = 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB34 "SWAP H": Swap nybbles in H, set F(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SWAP_H()
        {
            F = 0;
            H = (byte)((H >> 4) | (H << 4));
            if (H == 0)
                F = 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB35 "SWAP L": Swap nybbles in L, set F(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SWAP_L()
        {
            F = 0;
            L = (byte)((L >> 4) | (L << 4));
            if (L == 0)
                F = 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB36 "SWAP (HL)": Swap nybbles in value pointed by HL, set F(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SWAP_aHL()
        {
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            dHL = (byte)((dHL >> 4) | (dHL << 4));
            if (dHL == 0)
                F = 0x80;
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB37 "SWAP A": Swap nybbles in A, set F(Z000)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SWAP_A()
        {
            F = 0;
            A = (byte)((A >> 4) | (A << 4));
            if (A == 0)
                F = 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB38 "SRL B": Shift B right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRL_B()
        {
            F = 0;
            if ((B & 1) == 1)
                F = 0x10; // C = 1
            B >>= 1;
            if (B == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB39 "SRL C": Shift C right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRL_C()
        {
            F = 0;
            if ((C & 1) == 1)
                F = 0x10; // C = 1
            C >>= 1;
            if (C == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB3A "SRL D": Shift D right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRL_D()
        {
            F = 0;
            if ((D & 1) == 1)
                F = 0x10; // C = 1
            D >>= 1;
            if (D == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB3B "SRL E": Shift E right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRL_E()
        {
            F = 0;
            if ((E & 1) == 1)
                F = 0x10; // C = 1
            E >>= 1;
            if (E == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB3C "SRL H": Shift H right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRL_H()
        {
            F = 0;
            if ((H & 1) == 1)
                F = 0x10; // C = 1
            H >>= 1;
            if (H == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB3D "SRL L": Shift L right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRL_L()
        {
            F = 0;
            if ((L & 1) == 1)
                F = 0x10; // C = 1
            L >>= 1;
            if (L == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB3E "SRL (HL)": Shift value pointed by HL right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRL_aHL()
        {
            F = 0;
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & 1) == 1)
                F = 0x10; // C = 1
            dHL >>= 1;
            if (dHL == 0)
                F |= 0x80;
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB3F "SRL A": Shift A right, set F(Z00C)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SRL_A()
        {
            F = 0;
            if ((A & 1) == 1)
                F = 0x10; // C = 1
            A >>= 1;
            if (A == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB40 "BIT 0,B": Test bit 0 of B, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_0_B()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((B & (1 << 0)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB41 "BIT 0,C": Test bit 0 of C, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_0_C()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((C & (1 << 0)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB42 "BIT 0,D": Test bit 0 of D, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_0_D()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((D & (1 << 0)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB43 "BIT 0,E": Test bit 0 of E, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_0_E()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((E & (1 << 0)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB44 "BIT 0,H": Test bit 0 of H, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_0_H()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((H & (1 << 0)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB45 "BIT 0,L": Test bit 0 of L, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_0_L()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((L & (1 << 0)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB46 "BIT 0,(HL)": Test bit 0 of value pointed by HL, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_0_aHL()
        {
            F = (byte)((F & 0x10) | 0x20);
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & (1 << 0)) == 0)
                F |= 0x80;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB47 "BIT 0,A": Test bit 0 of A, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_0_A()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((A & (1 << 0)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB48 "BIT 1,B": Test bit 1 of B, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_1_B()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((B & (1 << 1)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB49 "BIT 1,C": Test bit 1 of C, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_1_C()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((C & (1 << 1)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB4A "BIT 1,D": Test bit 1 of D, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_1_D()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((D & (1 << 1)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB4B "BIT 1,E": Test bit 1 of E, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_1_E()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((E & (1 << 1)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB4C "BIT 1,H": Test bit 1 of H, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_1_H()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((H & (1 << 1)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB4D "BIT 1,L": Test bit 1 of L, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_1_L()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((L & (1 << 1)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB4E "BIT 1,(HL)": Test bit 1 of value pointed by HL, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_1_aHL()
        {
            F = (byte)((F & 0x10) | 0x20);
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & (1 << 1)) == 0)
                F |= 0x80;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB4F "BIT 1,A": Test bit 1 of A, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_1_A()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((A & (1 << 1)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB50 "BIT 2,B": Test bit 2 of B, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_2_B()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((B & (1 << 2)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB51 "BIT 2,C": Test bit 2 of C, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_2_C()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((C & (1 << 2)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB52 "BIT 2,D": Test bit 2 of D, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_2_D()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((D & (1 << 2)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB53 "BIT 2,E": Test bit 2 of E, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_2_E()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((E & (1 << 2)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB54 "BIT 2,H": Test bit 2 of H, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_2_H()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((H & (1 << 2)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB55 "BIT 2,L": Test bit 2 of L, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_2_L()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((L & (1 << 2)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB56 "BIT 2,(HL)": Test bit 2 of value pointed by HL, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_2_aHL()
        {
            F = (byte)((F & 0x10) | 0x20);
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & (1 << 2)) == 0)
                F |= 0x80;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB57 "BIT 2,A": Test bit 2 of A, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_2_A()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((A & (1 << 2)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB58 "BIT 3,B": Test bit 3 of B, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_3_B()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((B & (1 << 3)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB59 "BIT 3,C": Test bit 3 of C, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_3_C()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((C & (1 << 3)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB5A "BIT 3,D": Test bit 3 of D, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_3_D()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((D & (1 << 3)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB5B "BIT 3,E": Test bit 3 of E, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_3_E()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((E & (1 << 3)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB5C "BIT 3,H": Test bit 3 of H, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_3_H()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((H & (1 << 3)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB5D "BIT 3,L": Test bit 3 of L, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_3_L()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((L & (1 << 3)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB5E "BIT 3,(HL)": Test bit 3 of value pointed by HL, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_3_aHL()
        {
            F = (byte)((F & 0x10) | 0x20);
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & (1 << 3)) == 0)
                F |= 0x80;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB5F "BIT 3,A": Test bit 3 of A, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_3_A()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((A & (1 << 3)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB60 "BIT 4,B": Test bit 4 of B, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_4_B()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((B & (1 << 4)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB61 "BIT 4,C": Test bit 4 of C, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_4_C()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((C & (1 << 4)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB62 "BIT 4,D": Test bit 4 of D, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_4_D()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((D & (1 << 4)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB63 "BIT 4,E": Test bit 4 of E, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_4_E()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((E & (1 << 4)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB64 "BIT 4,H": Test bit 4 of H, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_4_H()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((H & (1 << 4)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB65 "BIT 4,L": Test bit 4 of L, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_4_L()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((L & (1 << 4)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB66 "BIT 4,(HL)": Test bit 4 of value pointed by HL, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_4_aHL()
        {
            F = (byte)((F & 0x10) | 0x20);
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & (1 << 4)) == 0)
                F |= 0x80;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB67 "BIT 4,A": Test bit 4 of A, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_4_A()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((A & (1 << 4)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB68 "BIT 5,B": Test bit 5 of B, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_5_B()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((B & (1 << 5)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB69 "BIT 5,C": Test bit 5 of C, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_5_C()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((C & (1 << 5)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB6A "BIT 5,D": Test bit 5 of D, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_5_D()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((D & (1 << 5)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB6B "BIT 5,E": Test bit 5 of E, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_5_E()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((E & (1 << 5)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB6C "BIT 5,H": Test bit 5 of H, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_5_H()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((H & (1 << 5)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB6D "BIT 5,L": Test bit 5 of L, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_5_L()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((L & (1 << 5)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB6E "BIT 5,(HL)": Test bit 5 of value pointed by HL, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_5_aHL()
        {
            F = (byte)((F & 0x10) | 0x20);
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & (1 << 5)) == 0)
                F |= 0x80;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB6F "BIT 5,A": Test bit 5 of A, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_5_A()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((A & (1 << 5)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB70 "BIT 6,B": Test bit 6 of B, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_6_B()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((B & (1 << 6)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB71 "BIT 6,C": Test bit 6 of C, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_6_C()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((C & (1 << 6)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB72 "BIT 6,D": Test bit 6 of D, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_6_D()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((D & (1 << 6)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB73 "BIT 6,E": Test bit 6 of E, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_6_E()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((E & (1 << 6)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB74 "BIT 6,H": Test bit 6 of H, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_6_H()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((H & (1 << 6)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB75 "BIT 6,L": Test bit 6 of L, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_6_L()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((L & (1 << 6)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB76 "BIT 6,(HL)": Test bit 6 of value pointed by HL, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_6_aHL()
        {
            F = (byte)((F & 0x10) | 0x20);
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & (1 << 6)) == 0)
                F |= 0x80;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB77 "BIT 6,A": Test bit 6 of A, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_6_A()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((A & (1 << 6)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB78 "BIT 7,B": Test bit 7 of B, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_7_B()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((B & (1 << 7)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB79 "BIT 7,C": Test bit 7 of C, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_7_C()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((C & (1 << 7)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB7A "BIT 7,D": Test bit 7 of D, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_7_D()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((D & (1 << 7)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB7B "BIT 7,E": Test bit 7 of E, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_7_E()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((E & (1 << 7)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB7C "BIT 7,H": Test bit 7 of H, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_7_H()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((H & (1 << 7)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB7D "BIT 7,L": Test bit 7 of L, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_7_L()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((L & (1 << 7)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB7E "BIT 7,(HL)": Test bit 7 of value pointed by HL, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_7_aHL()
        {
            F = (byte)((F & 0x10) | 0x20);
            byte dHL = Memory[(H << 8) + L];
            if ((dHL & (1 << 7)) == 0)
                F |= 0x80;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB7F "BIT 7,A": Test bit 7 of A, set F(Z01-)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BIT_7_A()
        {
            F = (byte)((F & 0x10) | 0x20);
            if ((A & (1 << 7)) == 0)
                F |= 0x80;
            m = 2; t = 8;
        }

        /// <summary>
        /// CB80 "RES 0,B": Clear (reset) bit 0 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_0_B()
        {
            B &= (byte)(~(1 << 0) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB81 "RES 0,C": Clear (reset) bit 0 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_0_C()
        {
            C &= (byte)(~(1 << 0) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB82 "RES 0,D": Clear (reset) bit 0 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_0_D()
        {
            D &= (byte)(~(1 << 0) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB83 "RES 0,E": Clear (reset) bit 0 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_0_E()
        {
            E &= (byte)(~(1 << 0) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB84 "RES 0,H": Clear (reset) bit 0 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_0_H()
        {
            H &= (byte)(~(1 << 0) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB85 "RES 0,L": Clear (reset) bit 0 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_0_L()
        {
            L &= (byte)(~(1 << 0) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB86 "RES 0,(HL)": Clear (reset) bit 0 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_0_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL &= (byte)(~(1 << 0) & 255);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB87 "RES 0,A": Clear (reset) bit 0 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_0_A()
        {
            A &= (byte)(~(1 << 0) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB88 "RES 1,B": Clear (reset) bit 1 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_1_B()
        {
            B &= (byte)(~(1 << 1) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB89 "RES 1,C": Clear (reset) bit 1 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_1_C()
        {
            C &= (byte)(~(1 << 1) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB8A "RES 1,D": Clear (reset) bit 1 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_1_D()
        {
            D &= (byte)(~(1 << 1) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB8B "RES 1,E": Clear (reset) bit 1 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_1_E()
        {
            E &= (byte)(~(1 << 1) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB8C "RES 1,H": Clear (reset) bit 1 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_1_H()
        {
            H &= (byte)(~(1 << 1) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB8D "RES 1,L": Clear (reset) bit 1 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_1_L()
        {
            L &= (byte)(~(1 << 1) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB8E "RES 1,(HL)": Clear (reset) bit 1 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_1_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL &= (byte)(~(1 << 1) & 255);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB8F "RES 1,A": Clear (reset) bit 1 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_1_A()
        {
            A &= (byte)(~(1 << 1) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB90 "RES 2,B": Clear (reset) bit 2 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_2_B()
        {
            B &= (byte)(~(1 << 2) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB91 "RES 2,C": Clear (reset) bit 2 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_2_C()
        {
            C &= (byte)(~(1 << 2) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB92 "RES 2,D": Clear (reset) bit 2 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_2_D()
        {
            D &= (byte)(~(1 << 2) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB93 "RES 2,E": Clear (reset) bit 2 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_2_E()
        {
            E &= (byte)(~(1 << 2) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB94 "RES 2,H": Clear (reset) bit 2 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_2_H()
        {
            H &= (byte)(~(1 << 2) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB95 "RES 2,L": Clear (reset) bit 2 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_2_L()
        {
            L &= (byte)(~(1 << 2) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB96 "RES 2,(HL)": Clear (reset) bit 2 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_2_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL &= (byte)(~(1 << 2) & 255);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB97 "RES 2,A": Clear (reset) bit 2 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_2_A()
        {
            A &= (byte)(~(1 << 2) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB98 "RES 3,B": Clear (reset) bit 3 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_3_B()
        {
            B &= (byte)(~(1 << 3) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB99 "RES 3,C": Clear (reset) bit 3 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_3_C()
        {
            C &= (byte)(~(1 << 3) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB9A "RES 3,D": Clear (reset) bit 3 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_3_D()
        {
            D &= (byte)(~(1 << 3) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB9B "RES 3,E": Clear (reset) bit 3 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_3_E()
        {
            E &= (byte)(~(1 << 3) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB9C "RES 3,H": Clear (reset) bit 3 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_3_H()
        {
            H &= (byte)(~(1 << 3) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB9D "RES 3,L": Clear (reset) bit 3 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_3_L()
        {
            L &= (byte)(~(1 << 3) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CB9E "RES 3,(HL)": Clear (reset) bit 3 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_3_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL &= (byte)(~(1 << 3) & 255);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CB9F "RES 3,A": Clear (reset) bit 3 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_3_A()
        {
            A &= (byte)(~(1 << 3) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBA0 "RES 4,B": Clear (reset) bit 4 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_4_B()
        {
            B &= (byte)(~(1 << 4) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBA1 "RES 4,C": Clear (reset) bit 4 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_4_C()
        {
            C &= (byte)(~(1 << 4) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBA2 "RES 4,D": Clear (reset) bit 4 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_4_D()
        {
            D &= (byte)(~(1 << 4) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBA3 "RES 4,E": Clear (reset) bit 4 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_4_E()
        {
            E &= (byte)(~(1 << 4) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBA4 "RES 4,H": Clear (reset) bit 4 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_4_H()
        {
            H &= (byte)(~(1 << 4) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBA5 "RES 4,L": Clear (reset) bit 4 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_4_L()
        {
            L &= (byte)(~(1 << 4) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBA6 "RES 4,(HL)": Clear (reset) bit 4 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_4_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL &= (byte)(~(1 << 4) & 255);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBA7 "RES 4,A": Clear (reset) bit 4 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_4_A()
        {
            A &= (byte)(~(1 << 4) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBA8 "RES 5,B": Clear (reset) bit 5 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_5_B()
        {
            B &= (byte)(~(1 << 5) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBA9 "RES 5,C": Clear (reset) bit 5 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_5_C()
        {
            C &= (byte)(~(1 << 5) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBAA "RES 5,D": Clear (reset) bit 5 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_5_D()
        {
            D &= (byte)(~(1 << 5) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBAB "RES 5,E": Clear (reset) bit 5 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_5_E()
        {
            E &= (byte)(~(1 << 5) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBAC "RES 5,H": Clear (reset) bit 5 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_5_H()
        {
            H &= (byte)(~(1 << 5) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBAD "RES 5,L": Clear (reset) bit 5 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_5_L()
        {
            L &= (byte)(~(1 << 5) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBAE "RES 5,(HL)": Clear (reset) bit 5 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_5_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL &= (byte)(~(1 << 5) & 255);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBAF "RES 5,A": Clear (reset) bit 5 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_5_A()
        {
            A &= (byte)(~(1 << 5) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBB0 "RES 6,B": Clear (reset) bit 6 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_6_B()
        {
            B &= (byte)(~(1 << 6) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBB1 "RES 6,C": Clear (reset) bit 6 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_6_C()
        {
            C &= (byte)(~(1 << 6) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBB2 "RES 6,D": Clear (reset) bit 6 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_6_D()
        {
            D &= (byte)(~(1 << 6) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBB3 "RES 6,E": Clear (reset) bit 6 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_6_E()
        {
            E &= (byte)(~(1 << 6) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBB4 "RES 6,H": Clear (reset) bit 6 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_6_H()
        {
            H &= (byte)(~(1 << 6) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBB5 "RES 6,L": Clear (reset) bit 6 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_6_L()
        {
            L &= (byte)(~(1 << 6) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBB6 "RES 6,(HL)": Clear (reset) bit 6 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_6_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL &= (byte)(~(1 << 6) & 255);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBB7 "RES 6,A": Clear (reset) bit 6 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_6_A()
        {
            A &= (byte)(~(1 << 6) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBB8 "RES 7,B": Clear (reset) bit 7 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_7_B()
        {
            B &= (byte)(~(1 << 7) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBB9 "RES 7,C": Clear (reset) bit 7 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_7_C()
        {
            C &= (byte)(~(1 << 7) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBBA "RES 7,D": Clear (reset) bit 7 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_7_D()
        {
            D &= (byte)(~(1 << 7) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBBB "RES 7,E": Clear (reset) bit 7 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_7_E()
        {
            E &= (byte)(~(1 << 7) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBBC "RES 7,H": Clear (reset) bit 7 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_7_H()
        {
            H &= (byte)(~(1 << 7) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBBD "RES 7,L": Clear (reset) bit 7 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_7_L()
        {
            L &= (byte)(~(1 << 7) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBBE "RES 7,(HL)": Clear (reset) bit 7 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_7_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL &= (byte)(~(1 << 7) & 255);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBBF "RES 7,A": Clear (reset) bit 7 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RES_7_A()
        {
            A &= (byte)(~(1 << 7) & 255);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBC0 "SET 0,B": Set bit 0 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_0_B()
        {
            B |= (1 << 0);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBC1 "SET 0,C": Set bit 0 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_0_C()
        {
            C |= (1 << 0);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBC2 "SET 0,D": Set bit 0 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_0_D()
        {
            D |= (1 << 0);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBC3 "SET 0,E": Set bit 0 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_0_E()
        {
            E |= (1 << 0);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBC4 "SET 0,H": Set bit 0 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_0_H()
        {
            H |= (1 << 0);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBC5 "SET 0,L": Set bit 0 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_0_L()
        {
            L |= (1 << 0);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBC6 "SET 0,(HL)": Set bit 0 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_0_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL |= (1 << 0);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBC7 "SET 0,A": Set bit 0 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_0_A()
        {
            A |= (1 << 0);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBC8 "SET 1,B": Set bit 1 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_1_B()
        {
            B |= (1 << 1);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBC9 "SET 1,C": Set bit 1 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_1_C()
        {
            C |= (1 << 1);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBCA "SET 1,D": Set bit 1 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_1_D()
        {
            D |= (1 << 1);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBCB "SET 1,E": Set bit 1 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_1_E()
        {
            E |= (1 << 1);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBCC "SET 1,H": Set bit 1 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_1_H()
        {
            H |= (1 << 1);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBCD "SET 1,L": Set bit 1 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_1_L()
        {
            L |= (1 << 1);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBCE "SET 1,(HL)": Set bit 1 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_1_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL |= (1 << 1);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBCF "SET 1,A": Set bit 1 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_1_A()
        {
            A |= (1 << 1);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBD0 "SET 2,B": Set bit 2 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_2_B()
        {
            B |= (1 << 2);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBD1 "SET 2,C": Set bit 2 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_2_C()
        {
            C |= (1 << 2);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBD2 "SET 2,D": Set bit 2 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_2_D()
        {
            D |= (1 << 2);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBD3 "SET 2,E": Set bit 2 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_2_E()
        {
            E |= (1 << 2);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBD4 "SET 2,H": Set bit 2 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_2_H()
        {
            H |= (1 << 2);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBD5 "SET 2,L": Set bit 2 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_2_L()
        {
            L |= (1 << 2);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBD6 "SET 2,(HL)": Set bit 2 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_2_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL |= (1 << 2);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBD7 "SET 2,A": Set bit 2 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_2_A()
        {
            A |= (1 << 2);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBD8 "SET 3,B": Set bit 3 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_3_B()
        {
            B |= (1 << 3);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBD9 "SET 3,C": Set bit 3 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_3_C()
        {
            C |= (1 << 3);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBDA "SET 3,D": Set bit 3 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_3_D()
        {
            D |= (1 << 3);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBDB "SET 3,E": Set bit 3 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_3_E()
        {
            E |= (1 << 3);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBDC "SET 3,H": Set bit 3 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_3_H()
        {
            H |= (1 << 3);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBDD "SET 3,L": Set bit 3 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_3_L()
        {
            L |= (1 << 3);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBDE "SET 3,(HL)": Set bit 3 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_3_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL |= (1 << 3);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBDF "SET 3,A": Set bit 3 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_3_A()
        {
            A |= (1 << 3);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBE0 "SET 4,B": Set bit 4 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_4_B()
        {
            B |= (1 << 4);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBE1 "SET 4,C": Set bit 4 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_4_C()
        {
            C |= (1 << 4);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBE2 "SET 4,D": Set bit 4 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_4_D()
        {
            D |= (1 << 4);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBE3 "SET 4,E": Set bit 4 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_4_E()
        {
            E |= (1 << 4);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBE4 "SET 4,H": Set bit 4 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_4_H()
        {
            H |= (1 << 4);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBE5 "SET 4,L": Set bit 4 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_4_L()
        {
            L |= (1 << 4);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBE6 "SET 4,(HL)": Set bit 4 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_4_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL |= (1 << 4);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBE7 "SET 4,A": Set bit 4 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_4_A()
        {
            A |= (1 << 4);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBE8 "SET 5,B": Set bit 5 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_5_B()
        {
            B |= (1 << 5);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBE9 "SET 5,C": Set bit 5 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_5_C()
        {
            C |= (1 << 5);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBEA "SET 5,D": Set bit 5 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_5_D()
        {
            D |= (1 << 5);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBEB "SET 5,E": Set bit 5 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_5_E()
        {
            E |= (1 << 5);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBEC "SET 5,H": Set bit 5 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_5_H()
        {
            H |= (1 << 5);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBED "SET 5,L": Set bit 5 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_5_L()
        {
            L |= (1 << 5);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBEE "SET 5,(HL)": Set bit 5 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_5_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL |= (1 << 5);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBEF "SET 5,A": Set bit 5 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_5_A()
        {
            A |= (1 << 5);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBF0 "SET 6,B": Set bit 6 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_6_B()
        {
            B |= (1 << 6);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBF1 "SET 6,C": Set bit 6 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_6_C()
        {
            C |= (1 << 6);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBF2 "SET 6,D": Set bit 6 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_6_D()
        {
            D |= (1 << 6);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBF3 "SET 6,E": Set bit 6 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_6_E()
        {
            E |= (1 << 6);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBF4 "SET 6,H": Set bit 6 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_6_H()
        {
            H |= (1 << 6);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBF5 "SET 6,L": Set bit 6 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_6_L()
        {
            L |= (1 << 6);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBF6 "SET 6,(HL)": Set bit 6 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_6_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL |= (1 << 6);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBF7 "SET 6,A": Set bit 6 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_6_A()
        {
            A |= (1 << 6);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBF8 "SET 7,B": Set bit 7 of B
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_7_B()
        {
            B |= (1 << 7);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBF9 "SET 7,C": Set bit 7 of C
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_7_C()
        {
            C |= (1 << 7);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBFA "SET 7,D": Set bit 7 of D
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_7_D()
        {
            D |= (1 << 7);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBFB "SET 7,E": Set bit 7 of E
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_7_E()
        {
            E |= (1 << 7);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBFC "SET 7,H": Set bit 7 of H
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_7_H()
        {
            H |= (1 << 7);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBFD "SET 7,L": Set bit 7 of L
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_7_L()
        {
            L |= (1 << 7);
            m = 2; t = 8;
        }

        /// <summary>
        /// CBFE "SET 7,(HL)": Set bit 7 of value pointed by HL
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_7_aHL()
        {
            byte dHL = Memory[(H << 8) + L];
            dHL |= (1 << 7);
            Memory[(H << 8) + L] = dHL;
            m = 2; t = 16;
        }

        /// <summary>
        /// CBFF "SET 7,A": Set bit 7 of A
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SET_7_A()
        {
            A |= (1 << 7);
            m = 2; t = 8;
        }

        #endregion

        #region Interrupts

        /// <summary>
        /// Call VBlank interrupt handler
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INT_40h()
        {
            IsHalted = false;
            IME = 0;
            SP -= 2;
            /*
            ushort PC2 = (ushort)(PC + 2);
            Memory[SP] = (byte)(PC2 & 0xFF);
            Memory[SP + 1] = (byte)(PC2 >> 8);
            */
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);

            PC = 0x0040;
            m = 3; t = 12; // ???
        }

        /// <summary>
        /// Call LCD Status interrupt handler
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INT_48h()
        {
            IsHalted = false;
            IME = 0;
            SP -= 2;
            /*
            ushort PC2 = (ushort)(PC + 2);
            Memory[SP] = (byte)(PC2 & 0xFF);
            Memory[SP + 1] = (byte)(PC2 >> 8);
            */
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);

            PC = 0x0048;
            m = 3; t = 12; // ???
        }

        /// <summary>
        /// Call timer interrupt handler
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INT_50h()
        {
            IsHalted = false;
            IME = 0;
            SP -= 2;
            /*
            ushort PC2 = (ushort)(PC + 2);
            Memory[SP] = (byte)(PC2 & 0xFF);
            Memory[SP + 1] = (byte)(PC2 >> 8);
            */
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);

            PC = 0x0050;
            m = 3; t = 12; // ???
        }

        /// <summary>
        /// Call serial interrupt handler
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INT_58h()
        {
            IsHalted = false;
            IME = 0;
            SP -= 2;
            /*
            ushort PC2 = (ushort)(PC + 2);
            Memory[SP] = (byte)(PC2 & 0xFF);
            Memory[SP + 1] = (byte)(PC2 >> 8);
            */
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);

            PC = 0x0058;
            m = 3; t = 12; // ???
        }

        /// <summary>
        /// Call joypad interrupt handler
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void INT_60h()
        {
            IsHalted = false;
            IME = 0;
            SP -= 2;
            /*
            ushort PC2 = (ushort)(PC + 2);
            Memory[SP] = (byte)(PC2 & 0xFF);
            Memory[SP + 1] = (byte)(PC2 >> 8);
            */
            Memory[SP] = (byte)(PC & 0xFF);
            Memory[SP + 1] = (byte)(PC >> 8);

            PC = 0x0060;
            m = 3; t = 12; // ???
        }

        #endregion
    }
}
