using System;

namespace LeBoyLib
{
    /// <summary>
    /// Emulates a Z80 Gameboy CPU, more specifically a Sharp LR35902 which is a Z80 minus a few instructions, with more logical operations and a sound generator.
    /// </summary>
    public partial class GBZ80
    {
        /// <summary>
        /// Emulates LCD operation and timing
        /// </summary>
        private void LCD_Step()
        {
            byte LCDstatus = Memory[0xFF41];
            byte LCDmode = (byte)(LCDstatus & 0x03);
            byte LYCInterrupt = (byte)(LCDstatus & 0x40);
            byte Mode2Interrupt = (byte)(LCDstatus & 0x20);
            byte Mode1Interrupt = (byte)(LCDstatus & 0x10);
            byte Mode0Interrupt = (byte)(LCDstatus & 0x8);

            byte LCDLY = Memory[0xFF44];

            byte IF = Memory[0xFF0F];

            LCDmodeT += t; // time already spent in the current mode during CPU work
            /*
             * Mode 0 is present between 201-207 clks, 2 about 77-83 clks, and 3 about 169-175 clks. A complete cycle through these states takes 456 clks. VBlank lasts 4560 clks. A complete screen refresh occurs every 70224 clks.)
             */

            // control states
            /*
             * FF40 - LCDC - LCD Control (R/W)

              Bit 7 - LCD Display Enable             (0=Off, 1=On)
              Bit 6 - Window Tile Map Display Select (0=9800-9BFF, 1=9C00-9FFF)
              Bit 5 - Window Display Enable          (0=Off, 1=On)
              Bit 4 - BG & Window Tile Data Select   (0=8800-97FF, 1=8000-8FFF)
              Bit 3 - BG Tile Map Display Select     (0=9800-9BFF, 1=9C00-9FFF)
              Bit 2 - OBJ (Sprite) Size              (0=8x8, 1=8x16)
              Bit 1 - OBJ (Sprite) Display Enable    (0=Off, 1=On)
              Bit 0 - BG Display (for CGB see below) (0=Off, 1=On)
            */
            byte LCDcontrol = Memory[0xFF40];
            int LCDenabled = LCDcontrol & 0x80;

            #region LCD mode handling
            if (LCDenabled != 0)
            {
                if (WasLCDDisabled)
                {
                    LCDmode = 2;
                    WasLCDDisabled = false;
                }

                switch (LCDmode)
                {
                    case 0:
                        /*
                         * Mode 0: The LCD controller is in the H-Blank period and
                         *         the CPU can access both the display RAM (8000h-9FFFh)
                         *         and OAM (FE00h-FE9Fh)
                         */
                        if (LCDmodeT >= 204)
                        {
                            LCDmodeT = 0;
                            //if (!IsHalted)
                            LCDLY++;
                            if (LCDLY == 144)
                            {
                                // VBlank interrupt                                
                                IF |= 0x01;
                                // STAT interrupt
                                if (Mode1Interrupt != 0)
                                    IF |= 0x02;


                                LCDmode = 1;
                            }
                            else
                            {
                                // STAT interrupt
                                if (Mode2Interrupt != 0)
                                    IF |= 0x02;

                                LCDmode = 2;
                            }
                        }
                        break;
                    case 1:
                        /*
                         * Mode 1: The LCD contoller is in the V-Blank period (or the
                         *         display is disabled) and the CPU can access both the
                         *         display RAM (8000h-9FFFh) and OAM (FE00h-FE9Fh)
                         */
                        if (LCDLY == 153)
                            LCDLY = 0;
                        if (LCDmodeT >= 456 && LCDLY < 153 && LCDLY > 143)
                        {
                            LCDmodeT = 0;
                            LCDLY++;
                        }
                        else if (LCDmodeT >= 456)
                        {
                            LCDmodeT = 0;
                            LCDmode = 2;

                            // STAT interrupt
                            if (Mode2Interrupt != 0)
                                IF |= 0x02;
                        }
                        break;
                    case 2:
                        /*
                         * Mode 2: The LCD controller is reading from OAM memory.
                         *         The CPU <cannot> access OAM memory (FE00h-FE9Fh)
                         *         during this period.
                         */
                        if (LCDmodeT >= 80)
                        {
                            LCDmode = 3;
                            LCDmodeT = 0;

                        }
                        break;
                    case 3:
                        /*
                         * Mode 3: The LCD controller is reading from both OAM and VRAM,
                         *         The CPU <cannot> access OAM and VRAM during this period.
                         *         CGB Mode: Cannot access Palette Data (FF69,FF6B) either.
                         */
                        if (LCDmodeT >= 172)
                        {
                            // STAT interrupt
                            if (Mode0Interrupt != 0)
                                IF |= 0x02;

                            LCDmode = 0;
                            LCDmodeT = 0;

                            // render buffer
                            if (LCDLY < 144)
                                RenderScanline(LCDLY);
                        }
                        break;
                }
            }
            else
            {
                LCDLY = 0;
                LCDmode = 2;
                LCDmodeT = 0;
                //LCDstatus = 0;
                WasLCDDisabled = true;
            }

            #endregion



            Memory[0xFF44] = LCDLY;
            // LY coincidence
            byte LYC = Memory[0xFF45];
            byte LYCF = (byte)(LCDstatus & 0x04);
            //Console.WriteLine(LYCInterrupt);
            if (LYC == LCDLY)
            {
                LYCF = 0x04;

                // STAT interrupt
                if (LYCInterrupt != 0)
                    IF |= 0x02;
            }
            else
                LYCF = 0;
            if (LCDenabled != 0)
                LCDstatus = (byte)((LCDstatus & 0xF8) | LCDmode | LYCF);
            Memory[0xFF41] = LCDstatus;
            Memory[0xFF0F] = IF;
        }

        /// <summary>
        /// Emulates the LCD controller rendering and output the results to ScreenBuffer (BGRA coded on 4 bytes)
        /// </summary>
        private void RenderScanline(int scanline)
        {
            // control states
            /*
             * FF40 - LCDC - LCD Control (R/W)

              Bit 7 - LCD Display Enable             (0=Off, 1=On)
              Bit 6 - Window Tile Map Display Select (0=9800-9BFF, 1=9C00-9FFF)
              Bit 5 - Window Display Enable          (0=Off, 1=On)
              Bit 4 - BG & Window Tile Data Select   (0=8800-97FF, 1=8000-8FFF)
              Bit 3 - BG Tile Map Display Select     (0=9800-9BFF, 1=9C00-9FFF)
              Bit 2 - OBJ (Sprite) Size              (0=8x8, 1=8x16)
              Bit 1 - OBJ (Sprite) Display Enable    (0=Off, 1=On)
              Bit 0 - BG Display (for CGB see below) (0=Off, 1=On)
            */
            byte LCDcontrol = Memory[0xFF40];

            int WindowTileMapSelect = LCDcontrol & 0x40;
            int WindowEnabled = LCDcontrol & 0x20;
            int TileDataSelect = LCDcontrol & 0x10;
            int BgTileMapSelect = LCDcontrol & 0x08;
            int SpriteSize = LCDcontrol & 0x04; // 0 = 8x8 else 8x16
            int SpriteEnabled = LCDcontrol & 0x02;
            int BgEnabled = LCDcontrol & 0x01;

            #region Getting palettes
            byte[] BgPalette = new byte[4];
            byte rawPalette = Memory[0xFF47];
            BgPalette[0] = (byte)(rawPalette & 0x03);
            BgPalette[1] = (byte)((rawPalette & 0x0C) >> 2);
            BgPalette[2] = (byte)((rawPalette & 0x30) >> 4);
            BgPalette[3] = (byte)((rawPalette & 0xC0) >> 6);

            byte[] Obj0Palette = new byte[4];
            rawPalette = Memory[0xFF48];
            Obj0Palette[0] = (byte)(rawPalette & 0x03);
            Obj0Palette[1] = (byte)((rawPalette & 0x0C) >> 2);
            Obj0Palette[2] = (byte)((rawPalette & 0x30) >> 4);
            Obj0Palette[3] = (byte)((rawPalette & 0xC0) >> 6);

            byte[] Obj1Palette = new byte[4];
            rawPalette = Memory[0xFF49];
            Obj1Palette[0] = (byte)(rawPalette & 0x03);
            Obj1Palette[1] = (byte)((rawPalette & 0x0C) >> 2);
            Obj1Palette[2] = (byte)((rawPalette & 0x30) >> 4);
            Obj1Palette[3] = (byte)((rawPalette & 0xC0) >> 6);
            #endregion

            byte ScrollX = Memory[0xFF43];
            byte ScrollY = Memory[0xFF42];
            byte WinY = Memory[0xFF4A];
            byte WinX = (byte)(Memory[0xFF4B] - 7);

            { // scope y
                int y = scanline;
                for (int x = 0; x < 160; x++)
                {
                    // bg0
                    if (BgEnabled != 0)
                    {
                        int xDist = (x + ScrollX) % 256;
                        int yDist = (y + ScrollY) % 256;
                        int xTile = xDist / 8;
                        int yTile = yDist / 8;
                        int xInTile = xDist % 8;
                        int yInTile = yDist % 8;

                        int tileId = xTile + yTile * 32;

                        byte tileNb = Memory[BgTileMapSelect * 0x80 + 0x9800 + tileId];

                        int tileDataStartAddr;
                        if (TileDataSelect != 0) // unsigned $8000-8FFF
                            tileDataStartAddr = 0x8000 + tileNb * 16;
                        else // signed $8800-97FF (9000 = 0)
                        {
                            sbyte id = (sbyte)tileNb;
                            if (id >= 0)
                                tileDataStartAddr = 0x9000 + id * 16;
                            else
                                tileDataStartAddr = 0x8800 + (id + 128) * 16;
                        }

                        byte tileData0 = Memory[tileDataStartAddr + yInTile * 2];
                        byte tileData1 = Memory[tileDataStartAddr + yInTile * 2 + 1];

                        tileData0 = (byte)((byte)(tileData0 << xInTile) >> 7);
                        tileData1 = (byte)((byte)(tileData1 << xInTile) >> 7);
                        int colorId = (tileData1 << 1) + tileData0;
                        byte color = (byte)((3 - BgPalette[colorId]) * 85);
                        byte[] ColorData = { color, color, color, 255 }; // B G R
                        ScreenBuffer[(x + y * 160) * 4] = ColorData[0];
                        ScreenBuffer[(x + y * 160) * 4 + 1] = ColorData[1];
                        ScreenBuffer[(x + y * 160) * 4 + 2] = ColorData[2];
                        ScreenBuffer[(x + y * 160) * 4 + 3] = ColorData[3];
                    }
                    // window
                    if (WindowEnabled != 0 && y >= WinY && x >= WinX)
                    {
                        int xDist = x - WinX;
                        int yDist = y - WinY;
                        int xTile = xDist / 8;
                        int yTile = yDist / 8;
                        int xInTile = xDist % 8;
                        int yInTile = yDist % 8;

                        int tileId = xTile + yTile * 32;

                        byte tileNb = Memory[WindowTileMapSelect * 0x10 + 0x9800 + tileId];

                        int tileDataStartAddr;
                        if (TileDataSelect != 0) // unsigned $8000-8FFF
                            tileDataStartAddr = 0x8000 + tileNb * 16;
                        else // signed $8800-97FF (9000 = 0)
                        {
                            sbyte id = (sbyte)tileNb;
                            if (id >= 0)
                                tileDataStartAddr = 0x9000 + id * 16;
                            else
                                tileDataStartAddr = 0x8800 + (id + 128) * 16;
                        }

                        byte tileData0 = Memory[tileDataStartAddr + yInTile * 2];
                        byte tileData1 = Memory[tileDataStartAddr + yInTile * 2 + 1];

                        tileData0 = (byte)((byte)(tileData0 << xInTile) >> 7);
                        tileData1 = (byte)((byte)(tileData1 << xInTile) >> 7);
                        int colorId = (tileData1 << 1) + tileData0;
                        byte color = (byte)((3 - BgPalette[colorId]) * 85);
                        byte[] ColorData = { color, color, color, 255 }; // B G R
                        ScreenBuffer[(x + y * 160) * 4] = ColorData[0];
                        ScreenBuffer[(x + y * 160) * 4 + 1] = ColorData[1];
                        ScreenBuffer[(x + y * 160) * 4 + 2] = ColorData[2];
                        ScreenBuffer[(x + y * 160) * 4 + 3] = ColorData[3];
                    }
                }
            }

            // sprites            
            if (SpriteEnabled != 0)
            {
                int[] priorityListId = new int[40];
                int[] priorityListX = new int[40];
                for (int i = 0; i < 40; i++)
                {
                    int x = Memory[0xFE01 + i * 4];
                    priorityListId[i] = i;
                    if (x <= 0 || x >= 168)
                        x = -1;
                    priorityListX[i] = x;
                }

                int pos = 1;
                while (pos < 40)
                {
                    if (priorityListX[pos] <= priorityListX[pos - 1])
                        pos++;
                    else
                    {
                        int tmp = priorityListX[pos];
                        priorityListX[pos] = priorityListX[pos - 1];
                        priorityListX[pos - 1] = tmp;
                        tmp = priorityListId[pos];
                        priorityListId[pos] = priorityListId[pos - 1];
                        priorityListId[pos - 1] = tmp;
                        if (pos > 1)
                            pos--;
                        else
                            pos++;
                    }
                }

                for (int i = 0; i < 40; i++)
                {
                    if (priorityListX[i] == -1)
                        break;
                    int id = priorityListId[i];

                    byte SpriteY = Memory[0xFE00 + id * 4];
                    if (SpriteY <= 0 || SpriteY >= 160)
                        continue;
                    byte SpriteX = Memory[0xFE01 + id * 4];
                    int Sprite = Memory[0xFE02 + id * 4];
                    byte SpriteAttr = Memory[0xFE03 + id * 4];

                    int startX = SpriteX - 1;
                    int startY = SpriteY - 9;

                    if (SpriteSize != 0)
                    {
                        Sprite = Sprite & 0xFE;
                        startY = SpriteY - 1;
                    }


                    //int endX = SpriteX - 8;
                    //int endY = SpriteY - 8 - SpriteSize * 2;
                    int stepX = -1;
                    int stepY = -1;
                    if ((SpriteAttr & 0x20) != 0) // X-flip
                    {
                        //startX = Math.Abs(SpriteX - 7);
                        startX = SpriteX - 8;
                        //endX = SpriteX;
                        stepX = 1;
                    }
                    if ((SpriteAttr & 0x40) != 0) // Y-flip
                    {
                        //startY = Math.Abs(SpriteY - (SpriteSize - 1));
                        startY = Math.Abs(SpriteY - 8 - 9 - SpriteSize * 2);
                        //endY = SpriteY;
                        stepY = 1;
                    }

                    // draw
                    int yInTile = 7 + SpriteSize * 2;
                    int xInTile = 7;

                    for (int y = startY; yInTile >= 0; y += stepY)
                    {
                        if (y != scanline)
                        {
                            yInTile--;
                            continue;
                        }

                        byte tileData0 = Memory[0x8000 + Sprite * 16 + yInTile * 2];
                        byte tileData1 = Memory[0x8000 + Sprite * 16 + yInTile * 2 + 1];

                        for (int x = startX; xInTile >= 0; x += stepX)
                        {
                            if (x < 0 || x >= 160)
                            {
                                xInTile--;
                                continue;
                            }

                            byte tileData2 = (byte)((byte)(tileData0 << xInTile) >> 7);
                            byte tileData3 = (byte)((byte)(tileData1 << xInTile) >> 7);
                            int colorId = (tileData3 << 1) + tileData2;
                            if (colorId != 0)
                            {
                                byte[] palette = ((SpriteAttr & 0x10) != 0 ? Obj1Palette : Obj0Palette);
                                byte color = (byte)((3 - palette[colorId]) * 85);
                                byte[] ColorData = { color, color, color, 255 }; // B G R
                                ScreenBuffer[(x + y * 160) * 4] = ColorData[0];
                                ScreenBuffer[(x + y * 160) * 4 + 1] = ColorData[1];
                                ScreenBuffer[(x + y * 160) * 4 + 2] = ColorData[2];
                                ScreenBuffer[(x + y * 160) * 4 + 3] = ColorData[3];
                            }
                            xInTile--;
                        }
                        yInTile--;
                        xInTile = 7;
                    }
                }
            }
        }
    }
}
