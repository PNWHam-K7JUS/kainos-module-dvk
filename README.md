# Kainos Voice Keyer (DVK)

A module for [Kainos](https://github.com/PNWHam-K7JUS/Kainos), the Hermes Lite 2 SDR program: a digital voice keyer like SmartSDR's, inside Kainos, in the Kainos look. Install it from **Setup > Modules** if you want it. It needs **Kainos 1.0.12** or later.

On a FlexRadio the voice keyer lives in the radio. The Hermes Lite 2 has nothing like that, so this module does the recording and playback on the PC, through Kainos.

## Using it

It's a tool rather than a mode: open it with the **DVK** button in the left-hand dock (lit while it's open), or **Tools > Voice Keyer (DVK)** (on the menu bar in the Classic layout). It docks under the panadapter like the RTTY and CW terminals, or pops out into its own window.

- **12 slots, F1 to F12.** Press a slot's F-key (or double-click it) to send it. **Esc** stops. Ctrl+F1 to F10 stay Kainos's CWX memories.
- **REC** records the selected slot from your mic, until **STOP** or Esc (up to 2 minutes). The quiet before your first word and after your last is cut off, so a CQ starts at once. The level meter shows your mic while recording: aim for gold on the loudest words, never red.
- **PLAY** sends the selected slot: the radio is keyed, the recording goes out, then it un-keys.
- **PREVIEW** plays the slot to you only, through your speakers.
- **REPEAT** with a gap (3 to 60 s): repeat CQ. The slot is sent, the keyer listens for the gap, then sends it again, until STOP, Esc, or you transmit yourself to answer someone.
- Right-click a slot to **rename** it, set its **level** (+6 to -12 dB against how it was recorded), **import** or **export** a WAV, or **clear** it.

**It sounds like you.** The recording is taken from the mic as Kainos has it (radio mic, sound card, VAC or ASIO, after VAC and before the mic gain), and played back in the same place. So the mic gain and your whole transmit chain (TX EQ, Kainos Audio, AetherVoice, the compressor) treat it exactly as your live voice. If you change your transmit audio, your recordings change with it.

**Safety.** It sends only in voice modes (LSB, USB, DSB, AM, SAM, FM). It stops at once on STOP or Esc, if the radio is un-keyed elsewhere (the MOX button, a foot switch), or if the radio doesn't go into transmit within 2 seconds. A repeat gives up if you transmit in its listening gap.

## Installing

Setup > Modules > **Install from file...** with `kainos-dvk-<version>.zip`, or from the module list once it's published. Recordings are kept in the module's data folder (`Slots\slot01.wav` ...), their names and levels in Kainos's settings.

## How it works

| Part | What it does |
|---|---|
| `DvkModule.cs` | The module: its DVK dock button (`IKainosDock`, with a Tools menu item) and the pane. Asks Kainos for `IKainosVoice` (Kainos 1.0.12); on an older Kainos a Modes menu item says what it needs. |
| `DvkEngine.cs` | Its own thread: recording (`ReadMic`, trimmed at 30 dB under the loudest, 0.1 s kept each side), preview (`PreviewWrite`), sending (`TxStart`, MOX, about 0.25 s kept queued, then un-keyed) and repeat CQ. |
| `Slots.cs` | The 12 recordings: a WAV each, names and levels in the settings; WAV import (resampled to 48 kHz) and export. |
| `Wav.cs` | WAV reading (8/16/24/32-bit PCM or float, mono or stereo) and writing (16-bit mono), and resampling. |
| `UI/` | The pane (buttons, the slot tiles, the mic level meter), drawn in the Kainos colours. |

## Building

```
.\build.ps1
```

It builds the module and its self-test against `Kainos.ModuleApi.dll` from a Kainos build next to this folder (or `-KainosBin <folder>`), runs the self-test (a stand-in Kainos: recording, sending, stopping, repeat, preview, the F-keys, an older Kainos, and the pane drawn), and writes `dist\kainos-dvk-<version>.zip` and `dist\registry-entry.json`.

## Licence

GPL-3.0-or-later. Copyright (C) 2026 Justin Cron K7JUS.
