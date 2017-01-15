LeBoy
===================
**Disclaimer:** GAME BOY is a trademark of Nintendo Co., Ltd.

LeBoy is a GB emulator written in C# and using MonoGame as a graphical and sound output. It is designed to be cross-platform (currently supporting Windows, MacOS, and Linux, but should already run as-is on iOS, Android, and consoles). The core emulation library (*LeBoyLib*) is a separated pure C# project that can easily be embedded into any kind of C# project.

LeBoy is not meant for accuracy and speed, it is a pet project which initially started for educational purposes.

----------

What's missing & known issues
-------------

LeBoy is not yet complete. Here's what's missing:

- *Sound*: it currently doesn't generate sound;
- *Real time clock (RTC)*: clock support is absent and queries to clock registers will return 0;
- *GBC & SGB*: only the base GB (DMG) is fully supported;
- *Some Memory Bank Controllers (MBCs)*: only MBC1, MBC2, MBC3, and MC5 are supported (without RTC support).

Known issues:

- *OAM priority and read/write*: OAM emulation is not quite accurate yet, some sprites have wrong priorities and this may result in sprites not displaying, or being displayed above/under other elements.

----------

Future plans
-------------

LeBoy is not yet complete. Here's what's missing:

- Fixing any rendering issues;
- Implementing sound;
- Implementing missing MBCs;
- Refactoring *GBMemory* and optimize memory read/write to enhance performance;
- Refactoring the LCD controller emulation to be less hacky and faster;

GBC support is not a priority for now.

----------

Building LeBoy
-------------

LeBoy requires a bleeding edge version of [MonoGame](http://monogame.net/). Make sure that you are either using the latest *development build* installer or building MonoGame from sources (*develop* branch).

----------

Using LeBoyLib
-------------

*LeBoyLib* is the main emulation library and is independent of MonoGame. You can build it and use it in any kind of C# project.

Here are some code snippet to get started with *LeBoyLib*.

Initializing and loading a ROM:

    GBZ80 emulator = new GBZ80();
    
    // loading a rom into a byte[]
    using (FileStream fs = new FileStream("MyRomFile.gb", FileMode.Open))
    {
        using (BinaryReader br = new BinaryReader(fs))
        {
            byte[] rom = new byte[fs.Length];
            for (int i = 0; i < fs.Length; i++)
                rom[i] = br.ReadByte();
            // loading the rom
            emulator.Load(rom);
        }
    }

Executing the emulation:

    // better to run this from a thread
    while(true)
    {
        // DecodeAndDispatch emulates the next CPU instruction
        // it returns the number of CPU cycles that have been used
        // and you can use GBZ80.ClockSpeed to synchronize the emulation speed
        emulator.DecodeAndDispatch();
    }

Getting the backbuffer:

    // this returns a BGRA encoded byte[] of the full 160x144 GB display
    byte[] backbuffer = emulator.GetScreenBuffer();

Inputs:

    // inputs can be updated through the JoypadState array
    // here's an example using MonoGame
    GamePadState gamePadState = GamePad.GetState(PlayerIndex.One);

    emulator.JoypadState[0] = (gamePadState.DPad.Right == ButtonState.Pressed); // right
    emulator.JoypadState[1] = (gamePadState.DPad.Left == ButtonState.Pressed); // left
    emulator.JoypadState[2] = (gamePadState.DPad.Up == ButtonState.Pressed); // up
    emulator.JoypadState[3] = (gamePadState.DPad.Down == ButtonState.Pressed); // down
    emulator.JoypadState[4] = (gamePadState.Buttons.B == ButtonState.Pressed); // B
    emulator.JoypadState[5] = (gamePadState.Buttons.A == ButtonState.Pressed); // A
    emulator.JoypadState[6] = (gamePadState.Buttons.Back == ButtonState.Pressed); // select
    emulator.JoypadState[7] = (gamePadState.Buttons.Start == ButtonState.Pressed); // start
