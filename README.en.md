# MicroGlow: Bringing the Night Back to a ¥400 Portable Mini LED Monitor

[简体中文](README.md) · **English**

A salvaged BOE panel, a multi-input controller board, and a repair I was a little too stubborn to abandon.

I bought a used portable Mini LED monitor for ¥600 and later received a ¥200 refund. That left me with a ¥400 monitor that was very bright, but whose black backgrounds looked gray and purple whenever bright objects were on screen. The seller said local dimming was enabled by default. What I could see did not match that explanation.

For a while, I considered keeping it as an ordinary IPS display. Then I kept coming back to one question: if the panel already had the capability, what was stopping it from using it?

I opened the enclosure, identified the panel and controller board, and worked with AI assistants through host-side queries, controller firmware, the downstream panel interface, and the backlight startup sequence. On this particular hardware combination, I found an initialization path that produced working local dimming in my own use.

If this counts as mysterious Eastern magic, it mostly consists of buying something for ¥400 and refusing to give up on it.

## What I actually consider working

**Local dimming works in my daily use. The OSD's local-dimming toggle still produces no visible change.**

Those are separate results, and I want both to be clear. Commands and control bits can change on readback without a corresponding change in the black background or halo. I do not know whether a power cycle or another step would make the toggle effective. I have not tested that hypothesis, and I am done pursuing it for now. I am happy with local dimming continuing to work.

The public OSD disables that everyday toggle. Brightness, audio, software scenes, read-only control status and moving test patterns remain available. That is where I am leaving the toggle experiment, while keeping the parts I can use.

## The two photos

### Before: a bright object, with most of the black background still gray and purple

![Before initialization: gray and purple black background](assets/before-local-dimming.jpg)

### After: the bright object remains, and the surrounding screen can finally darken

![My monitor after enabling local dimming](assets/after-local-dimming.jpg)

These are records of my use. Framing, pattern position and phone exposure differ; this is not a controlled measurement. The photos cannot establish contrast ratio, luminance or zone count, and a halo remains visible around the bright object in the second photo. My statement about the effect comes from looking at the physical display.

## The hardware in this case

| Item | What I identified |
| --- | --- |
| Panel | BOE NE160QDM-NM4, label revision V8.5 |
| Controller PCB | JC2795L_R40.1, board sticker JC2795L001 |
| Controller identification | CF23 read from the chip; RTD2795-family evidence. RTK2775 is the external display ID |
| Connection | AMD GPU to the portable monitor over HDMI |
| Signal during the documented checks | 2560 × 1600 at 60 Hz |
| Final cost | ¥600 purchase minus ¥200 refund: ¥400 |

I am not claiming that every ordinary controller board can drive local dimming. This is one identified board, panel and firmware-code version. Other combinations need their own evidence.

## How I found the initialization path

1. **Identify the hardware.** An EDID name is not a reliable substitute for a panel label or controller identification. I checked the labels, PCB markings and actual chip reads together.
2. **Separate picture output from backlight control.** A panel displaying an image does not establish that its local-dimming path has been initialized. My panel responded to a specific downstream control path.
3. **Find the startup conditions.** On this combination, writing Source OUI `00 AA 01`, waiting, and setting bit 3 of panel DPCD `0x344` reached a state in which I observed working local dimming.
4. **Put that sequence into startup.** I added a newly written 72-byte initialization stub to the verified firmware and changed its call site. The total number of differing bytes is also 72. My own installation was backed up and checked by a complete flash readback.

The OUI is part of this compatibility path; it does not turn my controller board into an Intel product. Also, early verbal observations and asynchronous log-arrival timestamps are not a synchronized optical switching measurement.

See the [technical notes](docs/technical-notes.en.md) for patch scope, addresses, compatibility checks and limitations.

## MicroGlow OSD

I also broke the monitor's physical control wheel while opening it. That became a reason to build a Windows control panel.

- Monitor brightness, contrast, RGB gain, and supported color presets, sharpness and monitor-speaker volume.
- Windows default playback-device volume and mute.
- Standard, Office, Night Reading, FPS, Cinema and a saved personal recipe. Selection is matched against four actual settings, with an undo operation.
- Morning, Noon and Night UI themes, selected automatically by time or fixed manually. Themes do not adjust monitor brightness.
- A moving white square, an alternating checkerboard and a moving narrow bar. Space pauses, clicking positions the object, Tab changes the pattern and Esc returns.
- A tray icon, global hotkeys and recovery from invalid physical-monitor handles.

Software scenes are parameter recipes, not the monitor manufacturer's FPS algorithm. FreeSync and HDR show available status and settings links, without pretend switches. The desktop UI is currently in Simplified Chinese; the project documentation is available in Chinese and English.

**The local-dimming toggle has no observed optical effect. The public build sends no E8 local-dimming Set command.** A readback from the research bridge is not proof of optical switching. Without that optional research extension, control status may be unreadable even when the startup initialization and local dimming work.

## Using this repository

This repository contains buildable source and a **local-file-only** startup patch generator. It does not contain complete vendor firmware, patched full images, commercial databases, vendor DLLs or the history of my private research archive.

### Build the OSD

For a ready-to-run Windows x64 package, visit [Releases](https://github.com/shangshang21/microglow-miniled/releases/latest). Extract it and run `MicroGlow.exe`. It is a desktop control application, not a firmware installer.

Requirements: Windows x64, .NET Framework 4.8 and its system C# compiler.

```powershell
powershell -NoProfile -File .\apps\MicroGlow\build.ps1
```

The output is `build/MicroGlow`. Keep `MicroGlow.exe`, `MainWindow.xaml` and `MicroGlow.ico` together. Copy `apps/MicroGlow/install.ps1` into that output directory to install it for the current user and create desktop shortcuts. Exit an older MicroGlow process before using a different build.

The current application matches one unique `RTK2775` display. It is not a universal monitor-control utility. Launching the OSD does not flash firmware or reapply the startup initialization patch.

### Generate a patch from your own backup

Requirements: Python 3.10 or later; no third-party Python packages.

```powershell
py .\tools\build_startup_patch.py --input "my-original-backup.bin" --output "my-local-startup-patch.bin"
```

The generator accepts only the reviewed firmware-code version. It preserves bank C configuration and rejects an unknown version, a repeated patch or an existing output file. Device configuration may vary, but **every byte outside that configuration bank must match the allowlisted version**. It generates a file and a local report; it does not access hardware or flash anything. A matching code digest does not identify your physical board or panel.

I have not turned my low-level flashing workflow into a universal one-click installer. Read the [technical notes](docs/technical-notes.en.md) first. Please do not upload vendor firmware or full device dumps in an issue.

## Why I think this case is useful

The interesting part for me is that this panel did not require me to replace everything with an expensive dedicated controller before I could observe local dimming. On my combination, a multi-input board with audio functions could also produce the effect after the missing initialization was supplied.

That is another direction worth investigating for DIY builds. It does not need a claim of universal compatibility or a flawless one-click repair to be useful.

I have not measured peak luminance, the exact zone count or color accuracy. I have not tested whether power cycling changes the ineffective toggle. I will not turn a failed button experiment into a success story. This is where I am stopping the experiment for now.

## License and provenance

Code is licensed under [GPL-3.0-only](LICENSE). Commercial use is allowed; distributing covered programs or modified versions requires complying with the applicable source and license obligations.

This license **does not grant rights to vendor firmware**. A locally generated image still includes third-party software; licensing the generator does not make the full output image GPL-licensed. The two photographs are provided for this case report and are not included in the code's GPL grant. See [NOTICE](NOTICE.md).

If you have similar hardware, I hope this saves you a few dead ends. My little sun finally does more than just shine.
