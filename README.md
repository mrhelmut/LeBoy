LeBoy
===================
**Disclaimer:** GAME BOY is a trademark of Nintendo Co., Ltd.

LeBoy is a GB emulator written in pure C#, with an optional MonoGame desktop front-end (but can be easily integrated in other presentation frameworks such as Unity). It is designed to be cross-platform, with a current target set to .NET 5 (currently supporting Windows, MacOS, and Linux, but should already run as-is on iOS, Android as well).

The core emulation library (*LeBoyLib*) is a separated pure C# project that can easily be embedded into any kind of C# project. It is meant to be agnostic of any graphical, sound, or input backend.

LeBoy is not meant for accuracy or speed. It is an educational project with a focus on straightforward, readable, and well documented code to learn more about the basics of making emulators.

----------

What's missing and known issues
-------------

LeBoy is not yet complete. Here's what's currently missing:

- *Sound* is not implemented yet;
- *Real time clock (RTC)* support is absent and queries to clock registers will return 0;
- *GBC and SGB* are not supported, only the base GB (DMG) is fully supported (which is the priority of the project);
- *Memory Bank Controllers (MBCs)* are partially implemented (only MBC1, MBC2, MBC3, and MC5 are supported, without RTC support).

Known issues:

- *OAM priority and read/write*: OAM emulation is not quite accurate, some sprites have wrong priorities and this may result in sprites not displaying, or being displayed above/under other elements.

----------

Building LeBoy
-------------

LeBoy requires .NET 5 and should build as-is.

----------

Using LeBoyLib
-------------

*LeBoyLib* is the main emulation library and is independent of any graphical, sound, or input backend. You can build it and use it in any kind of C# project.

Here are some code snippet to get started with *LeBoyLib*.

Initializing and loading a ROM:

```csharp
GBZ80 emulator = new GBZ80();

// Loading a rom into a byte[]
byte[] rom = System.IO.File.ReadAllBytes("MyRomFile.gb");

// Loading the rom
emulator.Load(rom);
```

Executing the emulation:

```csharp
// suggesting that this part runs from a thread
while(true)
{
    // DecodeAndDispatch emulates the next CPU instruction.
    // It returns the number of CPU cycles that have been used
    // and you can use GBZ80.ClockSpeed to synchronize the emulation speed
    // with the host CPU speed (this example runs uncapped emulation speed).
    emulator.DecodeAndDispatch();
}
```

Getting the backbuffer:

```csharp
// This returns a 32bit BGRA (1 byte per component) encoded byte[] of the full 160x144 GB display.
// The first pixel is the leftmost top pixel and it continues line by line.
byte[] backbuffer = emulator.GetScreenBuffer();
```

Inputs:

```csharp
// Inputs can be updated through the JoypadState array.
// Here's an example using a MonoGame implementation.
GamePadState gamePadState = GamePad.GetState(PlayerIndex.One);

emulator.JoypadState[0] = (gamePadState.DPad.Right == ButtonState.Pressed); // right
emulator.JoypadState[1] = (gamePadState.DPad.Left == ButtonState.Pressed); // left
emulator.JoypadState[2] = (gamePadState.DPad.Up == ButtonState.Pressed); // up
emulator.JoypadState[3] = (gamePadState.DPad.Down == ButtonState.Pressed); // down
emulator.JoypadState[4] = (gamePadState.Buttons.B == ButtonState.Pressed); // B
emulator.JoypadState[5] = (gamePadState.Buttons.A == ButtonState.Pressed); // A
emulator.JoypadState[6] = (gamePadState.Buttons.Back == ButtonState.Pressed); // select
emulator.JoypadState[7] = (gamePadState.Buttons.Start == ButtonState.Pressed); // start
```