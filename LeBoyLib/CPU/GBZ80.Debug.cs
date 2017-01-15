using System;

namespace LeBoyLib
{
    /// <summary>
    /// Emulates a Z80 Gameboy CPU, more specifically a Sharp LR35902 which is a Z80 minus a few instructions, with more logical operations and a sound generator.
    /// </summary>
    public partial class GBZ80
    {
        /// <summary>
        /// Get the full 256x256 background map
        /// </summary>
        /// <param name="MapSelect">Map address select (true = starts @9C00h, false @9800h)</param>
        /// <param name="AddrSelect">Tiles address select (true = unsigned 8000-8FFF, false = signed 8800-97FF)</param>
        /// <returns>A 256x256x4 byte array with RGBA color coded on four bytes</returns>
        public byte[] GetBackground(bool MapSelect, bool AddrSelect)
        {
            byte[] buffer = new byte[256 * 256 * 4];


            byte[] BgPalette = new byte[4];
            byte rawPalette = Memory[0xFF47];
            BgPalette[0] = (byte)(rawPalette & 0x03);
            BgPalette[1] = (byte)((rawPalette & 0x0C) >> 2);
            BgPalette[2] = (byte)((rawPalette & 0x30) >> 4);
            BgPalette[3] = (byte)((rawPalette & 0xC0) >> 6);

            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    // bg0
                    int xTile = x / 8;
                    int yTile = y / 8;
                    int xInTile = x % 8;
                    int yInTile = y % 8;

                    int tileId = xTile + yTile * 32;

                    int TileMapAddr;

                    if (MapSelect == true)
                    {
                        TileMapAddr = 0x9C00;
                    }
                    else
                        TileMapAddr = 0x9800;

                    byte tileNb = Memory[TileMapAddr + tileId];

                    int tileDataStartAddr;
                    if (AddrSelect == true) // unsigned $8000-8FFF
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
                    buffer[(x + y * 256) * 4] = ColorData[0];
                    buffer[(x + y * 256) * 4 + 1] = ColorData[1];
                    buffer[(x + y * 256) * 4 + 2] = ColorData[2];
                    buffer[(x + y * 256) * 4 + 3] = ColorData[3];
                }
            }


            return buffer;
        }

        /// <summary>
        /// Get tile bank 0
        /// </summary>
        /// <returns>A 128x192x4 byte array with RGBA color coded on four bytes</returns>
        public byte[] GetTiles()
        {
            byte[] buffer = new byte[128 * 192 * 4];


            byte[] BgPalette = new byte[4];
            byte rawPalette = Memory[0xFF47];
            BgPalette[0] = (byte)(rawPalette & 0x03);
            BgPalette[1] = (byte)((rawPalette & 0x0C) >> 2);
            BgPalette[2] = (byte)((rawPalette & 0x30) >> 4);
            BgPalette[3] = (byte)((rawPalette & 0xC0) >> 6);

            for (int y = 0; y < 192; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    // bg0
                    int xTile = x / 8;
                    int yTile = y / 8;
                    int xInTile = x % 8;
                    int yInTile = y % 8;

                    int tileId = xTile + yTile * 16;

                    int tileDataStartAddr = 0x8000 + tileId * 16;

                    byte tileData0 = Memory[tileDataStartAddr + yInTile * 2];
                    byte tileData1 = Memory[tileDataStartAddr + yInTile * 2 + 1];

                    tileData0 = (byte)((byte)(tileData0 << xInTile) >> 7);
                    tileData1 = (byte)((byte)(tileData1 << xInTile) >> 7);
                    int colorId = (tileData1 << 1) + tileData0;
                    byte color = (byte)((3 - BgPalette[colorId]) * 85);
                    byte[] ColorData = { color, color, color, 255 }; // B G R
                    buffer[(x + y * 128) * 4] = ColorData[0];
                    buffer[(x + y * 128) * 4 + 1] = ColorData[1];
                    buffer[(x + y * 128) * 4 + 2] = ColorData[2];
                    buffer[(x + y * 128) * 4 + 3] = ColorData[3];
                }
            }


            return buffer;
        }
    }
}
