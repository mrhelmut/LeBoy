using System;

namespace LeBoyLib
{
    /// <summary>
    /// Manage the GB memory mapping
    /// Mapping information from: http://bgb.bircd.org/pandocs.htm#memorymap
    /// 0000-3FFF   16KB ROM Bank 00     (in cartridge, fixed at bank 00)
    /// 4000-7FFF   16KB ROM Bank 01..NN (in cartridge, switchable bank number)
    /// 8000-9FFF   8KB Video RAM (VRAM) (switchable bank 0-1 in CGB Mode)
    /// A000-BFFF   8KB External RAM     (in cartridge, switchable bank, if any)
    /// C000-CFFF   4KB Work RAM Bank 0 (WRAM)
    /// D000-DFFF   4KB Work RAM Bank 1 (WRAM)  (switchable bank 1-7 in CGB Mode)
    /// E000-FDFF   Same as C000-DDFF (ECHO)    (typically not used)
    /// FE00-FE9F   Sprite Attribute Table (OAM)
    /// FEA0-FEFF   Not Usable
    /// FF00-FF7F   I/O Ports
    /// FF80-FFFE   High RAM (HRAM)
    /// FFFF        Interrupt Enable Register
    /// </summary>
    public class GBMemory
    {
        #region ROM header information

        /// <summary>
        /// Type of the Memory Bank Controller (MBC)
        /// </summary>
        private enum MemoryBankControllerType
        {
            NoBanking,
            MBC1,
            MBC2,
            MBC3,
            MMM01,
            MBC4,
            MBC5,
            MBC6,
            MBC7,
            PocketCamera,
            TAMA5,
            HuC3,
            HuC1,
        }

        /// <summary>
        /// Type of the Memory Bank Controller (MBC) used by the loaded cartridge
        /// </summary>
        private MemoryBankControllerType MBC = MemoryBankControllerType.NoBanking;

        /// <summary>
        /// ROM number of banks
        /// </summary>
        private int RomSize;

        /// <summary>
        /// RAM number of banks
        /// </summary>
        private int RamSize;

        #endregion

        #region Cartridge states
        /// <summary>
        /// Current ROM bank enabled
        /// </summary>
        private int CurrentROMBank = 0;

        /// <summary>
        /// RAM / ROM addressing mode
        /// </summary>
        private bool AccessingRam = false;

        /// <summary>
        /// Current RAM bank enabled
        /// </summary>
        private int CurrentRAMBank = 0;

        /// <summary>
        /// Current RTC register selected
        /// </summary>
        private int CurrentRTCRegister = 0;
        #endregion

        /// <summary>
        /// General memory
        /// </summary>
        private byte[] Memory = new byte[65536];

        /// <summary>
        /// ROM banks
        /// </summary>
        private byte[][] RomBanks;

        /// <summary>
        /// RAM banks
        /// </summary>
        private byte[][] RamBanks;

        /// <summary>
        /// BIOS, 256 bytes
        /// </summary>
        private byte[] BIOS = new byte[]
        {
            0x31, 0xFE, 0xFF, 0xAF, 0x21, 0xFF, 0x9F, 0x32, 0xCB, 0x7C, 0x20, 0xFB, 0x21, 0x26, 0xFF, 0x0E,
            0x11, 0x3E, 0x80, 0x32, 0xE2, 0x0C, 0x3E, 0xF3, 0xE2, 0x32, 0x3E, 0x77, 0x77, 0x3E, 0xFC, 0xE0,
            0x47, 0x11, 0x04, 0x01, 0x21, 0x10, 0x80, 0x1A, 0xCD, 0x95, 0x00, 0xCD, 0x96, 0x00, 0x13, 0x7B,
            0xFE, 0x34, 0x20, 0xF3, 0x11, 0xD8, 0x00, 0x06, 0x08, 0x1A, 0x13, 0x22, 0x23, 0x05, 0x20, 0xF9,
            0x3E, 0x19, 0xEA, 0x10, 0x99, 0x21, 0x2F, 0x99, 0x0E, 0x0C, 0x3D, 0x28, 0x08, 0x32, 0x0D, 0x20,
            0xF9, 0x2E, 0x0F, 0x18, 0xF3, 0x67, 0x3E, 0x64, 0x57, 0xE0, 0x42, 0x3E, 0x91, 0xE0, 0x40, 0x04,
            0x1E, 0x02, 0x0E, 0x0C, 0xF0, 0x44, 0xFE, 0x90, 0x20, 0xFA, 0x0D, 0x20, 0xF7, 0x1D, 0x20, 0xF2,
            0x0E, 0x13, 0x24, 0x7C, 0x1E, 0x83, 0xFE, 0x62, 0x28, 0x06, 0x1E, 0xC1, 0xFE, 0x64, 0x20, 0x06,
            0x7B, 0xE2, 0x0C, 0x3E, 0x87, 0xF2, 0xF0, 0x42, 0x90, 0xE0, 0x42, 0x15, 0x20, 0xD2, 0x05, 0x20,
            0x4F, 0x16, 0x20, 0x18, 0xCB, 0x4F, 0x06, 0x04, 0xC5, 0xCB, 0x11, 0x17, 0xC1, 0xCB, 0x11, 0x17,
            0x05, 0x20, 0xF5, 0x22, 0x23, 0x22, 0x23, 0xC9, 0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B,
            0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D, 0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E,
            0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99, 0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC,
            0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E, 0x3c, 0x42, 0xB9, 0xA5, 0xB9, 0xA5, 0x42, 0x4C,
            0x21, 0x04, 0x01, 0x11, 0xA8, 0x00, 0x1A, 0x13, 0xBE, 0x20, 0xFE, 0x23, 0x7D, 0xFE, 0x34, 0x20,
            0xF5, 0x06, 0x19, 0x78, 0x86, 0x23, 0x05, 0x20, 0xFB, 0x86, 0x20, 0xFE, 0x3E, 0x01, 0xE0, 0x50
        };

        /// <summary>
        /// Tell if the program is still in the BIOS/boot sequence
        /// </summary>
        public bool IsInBIOS = true;

        /// <summary>
        /// [] overload to access the general memory
        /// - Preserve memory ghosting between C000-DDFF and E000-FDFF
        /// - Access the BIOS during the boot up sequence
        /// </summary>
        /// <param name="address">Address</param>
        /// <returns>Byte in memory at address</returns>
        public byte this[int address]
        {
            get
            {
                //if (IsInBIOS && address == 0x0100)
                //    IsInBIOS = false;
                //if (IsInBIOS && address < 0x0100)
                //    return BIOS[address];
                switch (address)
                {
                    case int n when (n < 0x4000):
                        return RomBanks[0][address];
                    case int n when (n < 0x8000):
                        return RomBanks[CurrentROMBank][address - 0x4000];
                    case int n when (n >= 0xA000 && n < 0xC000):
                        switch (MBC)
                        {
                            case MemoryBankControllerType.MBC1:
                            case MemoryBankControllerType.MBC2:
                                return RamBanks[CurrentRAMBank][address - 0xA000];
                            case MemoryBankControllerType.MBC3:
                                if (AccessingRam)
                                    return RamBanks[CurrentRAMBank][address - 0xA000];
                                else
                                {
                                    // RTC register
                                    return 0;
                                }
                        }
                        break;
                }
                return Memory[address];
            }

            set
            {
                switch (address)
                {
                    // 0000-1FFF - RAM Enable (Write Only)
                    // Writing to this space is used to enable/disable RAM writing, because it is safer to disable it if not accessing it
                    // For an emulator, we don't really need to keep track of the RAM read/write states

                    case int n when (n >= 0x2000 && n < 0x4000): // 2000-3FFF - ROM Bank Number (Write Only)
                        switch (MBC)
                        {
                            case MemoryBankControllerType.MBC1:
                                int bank1 = value & 0x1F;  // clear lower bits
                                CurrentROMBank &= 0xE0; // clear upper bits
                                CurrentROMBank = (byte)(CurrentROMBank | bank1);

                                // adjust if 0
                                if (CurrentROMBank == 0)
                                    CurrentROMBank++;
                                return;
                            case MemoryBankControllerType.MBC2:
                                int bank2 = value & 0xFF;
                                //if ((value & 0x0100) != 0)
                                CurrentROMBank = bank2;
                                if (CurrentROMBank == 0)
                                    CurrentROMBank++;
                                return;
                            case MemoryBankControllerType.MBC3:
                                int bank3 = value & 0x7F;
                                //if ((value & 0x0100) != 0)
                                CurrentROMBank = bank3;
                                if (CurrentROMBank == 0)
                                    CurrentROMBank++;
                                return;
                            case MemoryBankControllerType.MBC5:
                                // not fully implemented
                                if (address < 0x3000)
                                {
                                    int bank5 = (CurrentROMBank & 0xFF00) | value;
                                }
                                else
                                {
                                    int bank5 = value;
                                    bank5 = (CurrentROMBank & 0xFF) | (bank5 << 4);
                                }
                                return;

                        }
                        return;

                    case int n when (n >= 0x4000 && n < 0x6000): // 4000-5FFF - RAM Bank Number - or - Upper Bits of ROM Bank Number (Write Only)
                        switch (MBC)
                        {
                            // MBC1 only
                            case MemoryBankControllerType.MBC1:
                            case MemoryBankControllerType.MBC2:
                                value &= 0x3; // keep only 2 bits

                                if (AccessingRam)
                                    CurrentRAMBank = value;
                                else
                                {
                                    //if (value == 0)
                                    //    value = 1;
                                    CurrentROMBank &= 0x1F; // clear lower bits                            
                                    CurrentROMBank |= value << 5;
                                }
                                return;
                            case MemoryBankControllerType.MBC3:
                            case MemoryBankControllerType.MBC5:
                                int bank = value & 0x3; // keep only 2 bits
                                int rtc = value & 0xC;
                                if (bank > 0)
                                {
                                    AccessingRam = true;
                                    CurrentRAMBank = bank;
                                }
                                else if (rtc > 0)
                                {
                                    AccessingRam = true;
                                    CurrentRTCRegister = rtc;
                                }
                                return;
                        }

                        return;

                    case int n when (n >= 0x6000 && n < 0x8000): // 6000-7FFF - ROM/RAM Mode Select (Write Only)
                        // MBC1 only
                        AccessingRam = (value & 0x01) == 0x01;
                        return;

                    case int n when (n >= 0xA000 && n < 0xC000): // A000-BFFF - RAM Bank 00-03, if any (Read/Write)
                        switch (MBC)
                        {
                            case MemoryBankControllerType.MBC1:
                            case MemoryBankControllerType.MBC2:
                            case MemoryBankControllerType.MBC5:
                                RamBanks[CurrentRAMBank][address - 40960] = value;
                                return;
                            case MemoryBankControllerType.MBC3:
                                if (AccessingRam)
                                {
                                    RamBanks[CurrentRAMBank][address - 40960] = value;
                                }
                                else
                                {
                                    // RTC registers
                                }
                                return;
                        }
                        return;

                    case 0xFF04: // DIV register, reset if written to
                        Memory[address] = 0;
                        return;

                    case 0xFF00: // I/O gamepad has 4 read-only bits
                        Memory[0xFF00] = (byte)((value & 0xF0) | (Memory[0xFF00] & 0x0F));
                        return;

                    case 0xFF46: // DMA Transfer
                        if (value <= 0xF1)
                        {
                            int startAddr = value * 0x100;
                            for (int i = 0xFE00; i <= 0xFE9F; i++)
                            {
                                if (startAddr < 0x4000)
                                {
                                    Memory[i] = RomBanks[0][startAddr];
                                }
                                else if (startAddr < 0x8000)
                                {
                                    Memory[i] = RomBanks[CurrentROMBank][address - 0x4000];
                                }
                                else if (address >= 0xA000 && address < 0xC000)
                                    Memory[i] = RomBanks[CurrentRAMBank][startAddr];
                                else
                                    Memory[i] = Memory[startAddr];
                                startAddr++;
                            }
                        }
                        return;
                }

                Memory[address] = value;
                // ECHO memory
                if (address >= 0xC000 && address <= 0xDDFF)
                    Memory[address + 0x2000] = value;
            }
        }

        /// <summary>
        /// Increment DIV register (trying to write to 0xFF04 will reset it to 0, so use this method to increment it)
        /// </summary>
        public void IncDIV()
        {
            Memory[0xFF04]++;
        }

        /// <summary>
        /// Update the joypad register without carring about the read-only part
        /// </summary>
        /// <param name="value">The value</param>
        public void UpdateJoypad(byte value)
        {
            Memory[0xFF00] = value;
        }

        /// <summary>
        /// Reset the memory (set everything to 0 and set entry point to the BIOS)
        /// </summary>
        public void Reset()
        {
            IsInBIOS = true;
            CurrentRAMBank = 0;
            CurrentROMBank = 0;
            AccessingRam = false;
            for (int i = 0; i < Memory.Length; i++)
                Memory[i] = 0;
        }

        /// <summary>
        /// Reset everything but the loaded ROM
        /// </summary>
        public void ResetRAM()
        {
            IsInBIOS = true;
            CurrentRAMBank = 0;
            CurrentROMBank = 0;
            AccessingRam = false;
            for (int i = 32768; i < Memory.Length; i++)
                Memory[i] = 0;
        }

        /// <summary>
        /// Initialized the memory to its default value (considering a normal Gameboy hardware)
        /// </summary>
        public void SetGBDefault()
        {
            Memory[0xFF00] = 0x3F; // I/O gamepad, 1 = not pressed
            Memory[0xFF05] = 0x00;
            Memory[0xFF06] = 0x00;
            Memory[0xFF07] = 0x00;
            //Memory[0xFF0F] = 0x08; // IF
            Memory[0xFF10] = 0x80;
            Memory[0xFF11] = 0xBF;
            Memory[0xFF12] = 0xF3;
            Memory[0xFF14] = 0xBF;
            Memory[0xFF16] = 0x3F;
            Memory[0xFF17] = 0x00;
            Memory[0xFF19] = 0xBF;
            Memory[0xFF1A] = 0x7F;
            Memory[0xFF1B] = 0xFF;
            Memory[0xFF1C] = 0x9F;
            Memory[0xFF1E] = 0xBF;
            Memory[0xFF20] = 0xFF;
            Memory[0xFF21] = 0x00;
            Memory[0xFF22] = 0x00;
            Memory[0xFF23] = 0xBF;
            Memory[0xFF24] = 0x77;
            Memory[0xFF25] = 0xF3;
            Memory[0xFF26] = 0xF1; // 0xF0 if SGB
            Memory[0xFF40] = 0x91;
            Memory[0xFF41] = 0x04; // LCD Status, not mentionned in the dock, but supposed to be mode 0 and LYCF = 1 (LYC = LY)
            Memory[0xFF42] = 0x00;
            Memory[0xFF43] = 0x00;
            Memory[0xFF45] = 0x00;
            Memory[0xFF47] = 0xFC;
            Memory[0xFF48] = 0xFF;
            Memory[0xFF49] = 0xFF;
            Memory[0xFF4A] = 0x00;
            Memory[0xFF4B] = 0x00;
            Memory[0xFFFF] = 0x00; // IE
        }

        /// <summary>
        /// Load a ROM
        /// </summary>
        /// <param name="rom">Data array</param>
        public void Load(byte[] rom)
        {
            AccessingRam = false;
            CurrentROMBank = 1;
            CurrentRAMBank = 0;

            // initializing the MBC type
            switch (rom[0x147])
            {
                case 0x00: MBC = MemoryBankControllerType.NoBanking; break;
                case 0x01: MBC = MemoryBankControllerType.MBC1; break;
                case 0x02: MBC = MemoryBankControllerType.MBC1; break;
                case 0x03: MBC = MemoryBankControllerType.MBC1; break;
                // 04: undocumented / doesn't exist
                case 0x05: MBC = MemoryBankControllerType.MBC2; break;
                case 0x06: MBC = MemoryBankControllerType.MBC2; break;
                // 07: undocumented / doesn't exist
                case 0x08: MBC = MemoryBankControllerType.NoBanking; break;
                case 0x09: MBC = MemoryBankControllerType.NoBanking; break;
                // 0A: undocumented / doesn't exist
                case 0x0B: MBC = MemoryBankControllerType.MMM01; break;
                case 0x0C: MBC = MemoryBankControllerType.MMM01; break;
                case 0x0D: MBC = MemoryBankControllerType.MMM01; break;
                // 0E: undocumented / doesn't exist
                case 0x0F: MBC = MemoryBankControllerType.MBC3; break;
                case 0x10: MBC = MemoryBankControllerType.MBC3; break;
                case 0x11: MBC = MemoryBankControllerType.MBC3; break;
                case 0x12: MBC = MemoryBankControllerType.MBC3; break;
                case 0x13: MBC = MemoryBankControllerType.MBC3; break;
                // 14: undocumented / doesn't exist
                case 0x15: MBC = MemoryBankControllerType.MBC4; break; // doesn't exist?
                case 0x16: MBC = MemoryBankControllerType.MBC4; break; // doesn't exist?
                case 0x17: MBC = MemoryBankControllerType.MBC4; break; // doesn't exist?
                // 18: undocumented / doesn't exist
                case 0x19: MBC = MemoryBankControllerType.MBC5; break;
                case 0x1A: MBC = MemoryBankControllerType.MBC5; break;
                case 0x1B: MBC = MemoryBankControllerType.MBC5; break;
                case 0x1C: MBC = MemoryBankControllerType.MBC5; break;
                case 0x1D: MBC = MemoryBankControllerType.MBC5; break;
                case 0x1E: MBC = MemoryBankControllerType.MBC5; break;
                // 1F: undocumented / doesn't exist
                case 0x20: MBC = MemoryBankControllerType.MBC6; break; // doesn't exist?
                // 21: undocumented / doesn't exist
                case 0x22: MBC = MemoryBankControllerType.MBC7; break; // doesn't exist?
                // ...: undocumented / doesn't exist
                case 0xFC: MBC = MemoryBankControllerType.PocketCamera; break;
                case 0xFD: MBC = MemoryBankControllerType.TAMA5; break;
                case 0xFE: MBC = MemoryBankControllerType.HuC3; break;
                case 0xFF: MBC = MemoryBankControllerType.HuC1; break;
            }

            // initializing ROM size and banks
            switch (rom[0x148])
            {
                case 0x00: RomSize = 2; break;
                case 0x01: RomSize = 4; break;
                case 0x02: RomSize = 8; break;
                case 0x03: RomSize = 16; break;
                case 0x04: RomSize = 32; break;
                case 0x05: RomSize = (MBC == MemoryBankControllerType.MBC1 ? 63 : 64); break;
                case 0x06: RomSize = (MBC == MemoryBankControllerType.MBC1 ? 125 : 128); break;
                case 0x07: RomSize = 256; break;
                case 0x08: RomSize = 512; break;
                case 0x52: RomSize = 72; break; // doesn't exist?
                case 0x53: RomSize = 80; break; // doesn't exist?
                case 0x54: RomSize = 96; break; // doesn't exist?
            }

            // initializing the RAM size
            RamSize = 1;
            switch (rom[0x149])
            {
                case 0x03: RamSize = 4; break;
                case 0x04: RamSize = 16; break;
                case 0x05: RamSize = 8; break;
            }

            // initializing ROM banks
            RomBanks = new byte[RomSize][];
            for (int i = 0; i < RomSize; i++)
                RomBanks[i] = new byte[16384];

            // copy banks
            for (int i = 0; i < RomSize; i++)
            {
                for (int j = 0; j < 16384; j++)
                    RomBanks[i][j] = rom[16384 * i + j];
            }

            // initializing RAM banks
            RamBanks = new byte[RamSize][];
            for (int i = 0; i < RamSize; i++)
                RamBanks[i] = new byte[8192];
        }
    }
}
