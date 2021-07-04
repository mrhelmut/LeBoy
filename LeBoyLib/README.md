LeBoyLib
===================

*LeBoyLib* is the main emulation library and is independent of any graphical, sound, or input backend. You can build it and use it in any kind of C# project.

Here are some code snippet to get started with *LeBoyLib*.

Initializing and loading a ROM:

```csharp
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
```

Executing the emulation:

```csharp
// better to run this from a thread
while(true)
{
    // DecodeAndDispatch emulates the next CPU instruction
    // it returns the number of CPU cycles that have been used
    // and you can use GBZ80.ClockSpeed to synchronize the emulation speed
    emulator.DecodeAndDispatch();
}
```

Getting the backbuffer:

```csharp
// this returns a 32bit BGRA (1 byte per component) encoded byte[] of the full 160x144 GB display
// the first pixel is the leftmost top pixel and it goes line by line
byte[] backbuffer = emulator.GetScreenBuffer();
```

Inputs:

```csharp
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
```csharp