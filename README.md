![RGB Unify](cover/bmc-cover-2560x640.png)

# RGB Unify

**One simple app for all the RGB lighting in your Windows PC.** RGB Unify finds your motherboard, fans, LED strips,
graphics card, keyboard, mouse and (optionally) RAM, and syncs them all to one effect. It can also remove the
vendor RGB apps that fight over the same lights.

## Download

Grab **`RGBUnify-Setup.exe`** from the [latest release](../../releases/latest) and run it.

Windows SmartScreen may warn that the app isn't signed: click **More info → Run anyway**.

## Features

- **Whole-PC effects:** Rainbow Wave, Color Cycle, Solid, Breathing, Two-Color Wave, Comet, Starlight, Music Sync, Off.
  Effects flow across every device as if they were one strip.
- **3 profiles:** switch from the window or the tray icon, rename them.
- **Fans:** give fans their own look with a hub-style "Next fan effect" button, set **each fan its own colour**,
  **line up fans** mounted at different angles, and choose whether RGB Unify or your hub's button is in charge.
- **ARGB headers:** set "fans × LEDs per fan"; make JARGB 1 and 2 **match each other** or **flow through** as one strip.
- **Clean Up:** finds about 28 brands of RGB software, services and drivers (iCUE, MSI Center, Armoury Crate, SignalRGB...)
  and pauses or uninstalls them, with an optional restore point.
- **Graphics card check:** tells you if your GPU's lights were found, and why not if they weren't.
- **6 themes:** Midnight, Neon, Ocean, Sunset, Forest, Light. Opens full screen. A "?" next to every section explains it.
- Starts with Windows (in the tray) without UAC prompts.

## How it works

RGB Unify is a Windows Forms app (C#, .NET Framework 4.8, no extra runtime). Hardware access is done by
[OpenRGB](https://openrgb.org) (GPL-2.0), which RGB Unify downloads and runs hidden in the background, then drives
through OpenRGB's SDK network protocol. Motherboard, GPU and RAM lighting need the signed
[PawnIO](https://pawnio.eu) driver, which RGB Unify offers to install.

### RAM safety

On some DDR5 kits, repeated RAM scans by OpenRGB have corrupted the memory's SPD chip and stopped PCs from booting
([OpenRGB #4934](https://gitlab.com/CalcProgrammer1/OpenRGB/-/work_items/4934)). So RAM control is **off by default**.
When turned on, RAM is scanned **at most once per Windows boot**. All of OpenRGB's RAM detectors are switched off for
every other engine start.

## Building from source

No SDK needed. The C# compiler that ships with Windows is enough.

```
cd src
build.bat
```

This produces `RGBUnify-Setup.exe` (the installer, with the app embedded) in the folder above `src`.
The code sticks to C# 5 syntax so the built-in `csc.exe` can compile it.

| File | What it does |
|---|---|
| `Program.cs` | Single instance, `--tray` start, show/exit signals |
| `MainForm*.cs` | The window: Home (Lighting, Fans, Devices), Clean Up, Settings, tray |
| `Engine.cs` | Runs OpenRGB, render loop, zones and fans, RAM safety gate |
| `OpenRgbClient.cs` | OpenRGB SDK client (protocol v4) |
| `Effects.cs` | The effects and the Music Sync audio meter |
| `Conflicts.cs` | Clean Up scanner and remover |
| `Settings.cs` | Settings, profiles, startup task |
| `Theme.cs` | Colour themes |
| `Setup.cs` | Installer and uninstaller |

## Support

RGB Unify is free. If it helped you, you can [buy me a coffee](https://buymeacoffee.com/banana16x) ☕

## License

[GPL-3.0](LICENSE). OpenRGB and PawnIO are separate programs with their own licences.
