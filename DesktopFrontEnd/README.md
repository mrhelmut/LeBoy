DesktopFrontEnd
===================

*DesktopFrontEnd* is a Windows, macOS, and Linux front-end for *LeBoyLib* that implements graphics, sound, and inputs using MonoGame (which itself uses OpenGL, OpenAL, and SDL2).

*DesktopFrontEnd* is .NET 5.0 command line application. It takes the ROM file path as a paramter (or you can alternatively drag and drop the ROM file on the executable).

Debugging the DesktopFrontEnd
-------------

You can specificy a ROM file path for debugging purposes by specifying a fully qualified path in the ```commandLineArgs``` of the *Properties/launcSettings.json* file like this:

```json
{
  "profiles": {
    "DesktopFrontEnd": {
      "commandName": "Project",
      "commandLineArgs": "C:/test_rom.gb"
    }
  }
}
```