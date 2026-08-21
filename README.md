# ScopeControl

A Windows desktop application for driving **Keysight InfiniiVision** oscilloscopes
remotely — over LAN, USB or serial. The window is laid out like the instrument
itself: the live display fills the top, colour-coded channel strips run along the
bottom, and horizontal, trigger, run and screen controls sit in the right-hand
column.

Tested against an **MSO-X 3024G** (firmware 07.60) and an **MSO-X 2024A**
(firmware 02.65). Other InfiniiVision 2000/3000/4000 X-Series models share the
command set and should work, but are untested.

---

## Contents

- [Features](#features)
- [The window](#the-window)
- [Requirements](#requirements)
- [Connecting](#connecting)
- [Building](#building)
- [Settings](#settings)
- [Troubleshooting](#troubleshooting)
- [Known limitations](#known-limitations)
- [How it is put together](#how-it-is-put-together)
- [SCPI reference](#scpi-reference)
- [Working without the instrument](#working-without-the-instrument)
- [Licence](#licence)

---

## Features

**Vertical** — CH1–CH4 on and off, volts/div from 1 mV to 5 V, offset, AC/DC
coupling, probe attenuation, 20 MHz bandwidth limit, invert.

**Horizontal** — time/div from 1 ns to 50 s, delay, time reference.

**Trigger** — Auto or Normal sweep, trigger type, source (CH1–4, external, line,
wave gen), edge slope rising / falling / either / alternating, level with a
Set 50% key, coupling, noise and HF reject.

**Math** — a fifth channel strip beside CH1–4 with its own display key, scale and
offset, plus a tab for the operator: add, subtract, multiply, divide, FFT
magnitude and phase, integrate, differentiate, square root, absolute, square, ln,
log, exp, 10ˣ, low pass, high pass, magnify. FFT window, centre and span appear
when a transform is selected.

**Cursors** — the MARKer subsystem: mode, sources, all four positions, and ΔX,
1/ΔX and ΔY readouts. Positions can be typed, or placed by clicking on the
display — put the caret in the X1 field and the next click sets X1.

**Measurements** — 24 measurements across voltage, time and counting, added to
the instrument's own display so they appear in the mirrored screen. Click to add,
click again to remove.

**Live readouts** — two rows under the display, each picking a channel and up to
three measurements of it. Values are tinted to match the trace colour; a slot set
to *none* sends no query.

**Acquisition** — normal, averaging, high resolution, peak detect, average count,
plus Run, Stop, Single, Auto scale, Default setup, Clear display.

**Live display** — pulls the instrument screen as a PNG on a timer, with a Save
as PNG button and an ink-saver toggle.

**Follows the front panel** — re-reads every setting on a separate timer, so
changes made at the bench appear here instead of leaving stale values on screen.
Paused while you are mid-edit so it never snaps a value away under your cursor.

**Model profiles** — the family is detected from `*IDN?`, or chosen by hand, and
filters the math operators and measurements offered.

**SCPI console** — hidden by default (**F12**, or the key in the top strip).
Every command and reply is timestamped, and you can type raw SCPI yourself.

## The window

```
┌────────────────────────────────────────────────────────────────┐
│ ▾ ScopeControl 8.0 · connected to …                            │  collapsible strip
├────────────────────────────────────────────────────────────────┤
│ address │ Find │ transport │ model │ Connect │ Disconnect │ …   │  folds away
├──────────────────────────────────────────┬─────────────────────┤
│                                          │  HORIZONTAL         │
│           instrument display             │  TRIGGER            │
│                                          │  RUN CONTROL        │
│                                          │  SCREEN             │
│ CH1 │ Vpp 4.3 V │ Freq 4.99 kHz │ …      │                     │
│ CH2 │ Vpp 1.1 V │ …             │ …      │                     │
├──────────────────────────────────────────┤                     │
│ [Channels] [Math] [Cursors] [Measurements]                     │
│  CH1   CH2   CH3   CH4   MATH            │                     │
└──────────────────────────────────────────┴─────────────────────┘
```

Two levels of tabs. **Oscilloscope / Setup / About** across the top separate what
you use while probing from what you set once. Inside the working page,
**Channels / Math / Cursors / Measurements** share the bottom strip so the
display keeps as much height as possible.

Each channel key toggles the trace. The small **Grid** key beside it selects that
channel, which is what moves the voltage labels down the left edge of the
instrument screen.

## Requirements

- Windows, .NET Framework 4.8
- An instrument reachable over LAN, USB or serial
- For the VISA transports: Keysight IO Libraries Suite (or NI-VISA)

## Connecting

Four transports, chosen in the connection bar. All of them carry the same SCPI,
so nothing above the transport layer changes.

| Transport | Needs | Address form |
| --- | --- | --- |
| **Raw socket : 5025** | nothing | `TCPIP0::10.10.0.222::5025::SOCKET` |
| **VISA.NET** | `Ivi.Visa.dll` + a vendor implementation | `TCPIP0::10.10.0.222::INSTR` |
| **VISA-COM** | IO Libraries (registers `VISA.GlobalRM`) | `USB0::0x0957::0x1796::MY63400145::0::INSTR` |
| **Serial / FTDI** | nothing | `COM3`, `COM3:9600` or `ASRL3::INSTR` |

**Start with the raw socket if you are in a hurry.** The instrument speaks plain
SCPI on port 5025 with no drivers, no NuGet packages and no registration. It is
the fastest way to confirm the network path before debugging anything else.

**USB** goes through either VISA transport — USBTMC is simply a different
resource string. Press **Find** to enumerate what is attached; a USB address
carries the vendor id, product id and serial number, so it is not something to
type from memory.

The app connects on startup using whatever you used last. If that fails it says
so on the display area and waits, rather than blocking an empty window.

## Building

```bash
git clone <this repo>
cd ScopeControl
dotnet build -c Release
```

Or open the folder in Visual Studio and press F5.

**Build as x64.** The VISA shared components are 64-bit and a 32-bit process
cannot load them. The csproj sets `Prefer32Bit=false` already; a 32-bit build
fails with `DllNotFoundException: ktvisa32.dll`.

### VISA.NET is not on NuGet

Restoring `Ivi.Visa` or `Keysight.Visa` from nuget.org fails with **NU1101** —
those package IDs do not exist. The assemblies ship with IO Libraries, so the
project references `Ivi.Visa.dll` from disk. Find yours:

```powershell
Get-ChildItem "C:\Program Files\IVI Foundation" -Recurse -Filter Ivi.Visa.dll |
  Select-Object -ExpandProperty FullName
```

and set the `IviVisaDll` property in `ScopeControl.csproj`. A wrong path fails
the build with a message naming the path it tried, rather than a wall of
"type or namespace not found".

`Keysight.Visa.dll` is deliberately **not** referenced. `GlobalResourceManager`
locates the vendor implementation at run time, and if the IVI registration is
missing, `VisaImplementationLoader` finds it in the GAC by reflection.

## Settings

Preferences live in `%APPDATA%\ScopeControl\settings.txt` — plain key=value text
you can edit or delete. Written on exit and again whenever a connection
succeeds, so a working address survives even if the app is killed.

| Key | Meaning |
| --- | --- |
| `address`, `transport`, `model` | connection, plus up to eight `recent=` entries |
| `refreshMs`, `autoRefresh` | screen capture timer |
| `followPanel`, `syncMs` | settings re-read timer |
| `inkSaver`, `checkErrors` | screen and error-queue options |
| `showConsole`, `showTopBar` | panel visibility |
| `gratLeft`, `gratTop`, `gratRight`, `gratBottom` | graticule calibration for click-to-place |
| `windowWidth`, `windowHeight`, `maximized` | window geometry |

**A saved file beats a changed code default.** If you edit a default in
`AppSettings.cs` and nothing changes, delete this file.

## Troubleshooting

**Measurements time out and report "no result ready"** — the instrument accepted
the command but had nothing to measure. The usual cause is **Normal sweep with
no trigger**: no acquisition completes, so the measurement never finishes. Switch
Sweep to Auto, or give it a signal that crosses the trigger level.

**"No vendor-specific VISA .NET implementation is installed"** — the IVI shared
components are only the interface. The part that talks to the instrument is
`Keysight.Visa.dll`, an optional component of the IO Libraries installer. Check
the GAC, not Program Files:

```powershell
Get-ChildItem "C:\Windows\Microsoft.NET\assembly" -Recurse -Filter "*.Visa.dll" |
  Select-Object -ExpandProperty FullName
```

If only `Ivi.Visa` is listed, reinstall IO Libraries with VISA.NET support
enabled. Use the raw socket meanwhile.

**A command comes back `-113 Undefined header`** — that model does not have it.
The app records it and stops sending it for the rest of the session. Check the
model in the connection bar; the profile filters what is offered.

**Nothing appears and the process exits** — check `ScopeControl-error.log` next
to the exe. Startup exceptions are caught and written there. Note that cmd
returns to the prompt immediately for any GUI app, so that alone means nothing.

**Text is clipped** — the layout scales with DPI via `AutoScaleMode.Dpi`. If
something still overflows at high scaling, removing the `DpiAwareness` line from
`App.config` makes Windows bitmap-scale the whole window instead.

**Nothing seems to be happening** — the settings poll runs silently by default,
because roughly thirty queries every few seconds would bury the console. Setup →
**Log it in the console anyway**.

## Known limitations

**Softkeys cannot be pressed remotely.** The InfiniiVision command set exposes
settings directly rather than emulating key presses, and there is no query for
what the current softkey labels say. Use the instrument's own browser-based
remote front panel at `http://<ip>/` if you need menu navigation.

**Selecting a channel is a workaround.** The graticule voltage labels belong to
whichever channel the instrument considers selected, and there is no "select
channel" command — `:VIEW` does not do it. Switching a channel's display *on* is
what selects it, so the **Grid** key cycles the display off and on. Expect a
brief flicker on a channel that was already visible.

**Click-to-place needs calibrating once.** Positions are derived from the
captured image, and the graticule sits in a different part of that image on each
model. Cursors tab → tick **Show graticule guide**, nudge the four percentages
until the box lines up, untick. The result is saved.

**Removing one measurement clears them all.** There is no per-measurement delete,
only `:MEASure:CLEar`. Un-clicking one clears the display and re-sends the
survivors, so you will see several commands and a flicker. The instrument also
drops the oldest measurement when its display fills, without telling us, so the
app's idea of what is showing can drift — *Clear all* resyncs.

**Model profiles are best-effort.** The lists of unsupported math operators and
measurements come from documentation, not from testing every firmware. If
something you know works is greyed out, choose *All commands (no filtering)*.

**Scale values off the 1-2-5 list.** The instrument can sit at 420 ns/div after
an autoscale. Rather than snapping to the nearest standard step and showing
something untrue, the exact value is inserted into the dropdown.

## How it is put together

| Path | Role |
| --- | --- |
| `Instrument/IScopeTransport.cs` | Link abstraction: write, read, read N bytes, clear, read a block |
| `Instrument/SocketTransport.cs` | Raw TCP port 5025 |
| `Instrument/VisaTransport.cs` | VISA.NET via `GlobalResourceManager`, plus resource discovery |
| `Instrument/VisaComTransport.cs` | VISA-COM, late bound so it needs no reference |
| `Instrument/SerialTransport.cs` | SCPI over a COM port |
| `Instrument/VisaImplementationLoader.cs` | Finds a vendor implementation when discovery fails |
| `Instrument/BlockReader.cs` | IEEE 488.2 definite-length blocks |
| `Instrument/KeysightScope.cs` | Every SCPI command, serialised behind one gate |
| `Instrument/Measurements.cs` | Measurement catalogue and argument shapes |
| `Instrument/InstrumentProfile.cs` | Per-family capability profiles |
| `Instrument/Eng.cs` | Engineering notation in and out |
| `UI/MainForm.cs` | Layout and event wiring |
| `UI/ChannelPanel.cs`, `UI/MathPanel.cs` | Channel and math strips |
| `UI/MathTab.cs`, `UI/CursorsTab.cs`, `UI/MeasurementsTab.cs`, `UI/AboutTab.cs` | Tab pages |
| `UI/ReadoutRow.cs` | One live readout line under the display |
| `UI/ScopeScreen.cs` | Display area, click mapping, calibration guide |
| `UI/EngBox.cs` | Value entry with step and zero keys |
| `UI/DarkTabControl.cs`, `UI/DarkCheckBox.cs` | Owner-drawn controls the theme cannot reach |
| `AppSettings.cs` | Persisted preferences |
| `tools/mock_scope.py` | Fake instrument on port 5025, for working without hardware |
| `tools/verify_scope.py` | Checks every SCPI command this app sends against a real scope |

**Threading.** Instrument I/O never runs on the UI thread: `KeysightScope` wraps
each call in `Task.Run` behind a `SemaphoreSlim`, so the window stays responsive
and two commands can never interleave on the wire.

**Writes are event-driven.** Nothing is re-sent on a timer. Ink saver is cached
and only written when it changes. The only repeating traffic is the screen
capture, the readout queries and the settings poll.

**Failures are contained.** A query that goes unanswered triggers a device clear
so a late reply cannot be read as the answer to the next command, and the error
queue is read to say why. Each reading in the poll is independent, so one bad
value does not abandon the rest, and a command rejected with `-113` is not sent
again that session.

## SCPI reference

**Vertical**

```
:CHANnel<n>:DISPlay ON|OFF          :CHANnel<n>:SCALe <volts_per_div>
:CHANnel<n>:OFFSet <volts>          :CHANnel<n>:COUPling AC|DC
:CHANnel<n>:PROBe <attenuation>     :CHANnel<n>:BWLimit ON|OFF
:CHANnel<n>:INVert ON|OFF
```

**Horizontal**

```
:TIMebase:SCALe <sec_per_div>       :TIMebase:POSition <seconds>
:TIMebase:REFerence LEFT|CENTer|RIGHt
```

**Trigger**

```
:TRIGger:SWEep AUTO|NORMal          :TRIGger:MODE EDGE|GLITch|PATTern
:TRIGger:EDGE:SOURce CHANnel<n>|EXTernal|LINE|WGEN
:TRIGger:EDGE:SLOPe POSitive|NEGative|EITHer|ALTernate
:TRIGger:EDGE:LEVel <volts>         :TRIGger:COUPling DC|AC|LFReject
:TRIGger:NREJect ON|OFF             :TRIGger:HFReject ON|OFF
```

**Math**

```
:FUNCtion:DISPlay ON|OFF            :FUNCtion:OPERation ADD|SUBTract|MULTiply|DIVide|FFT|…
:FUNCtion:SOURce1 <source>          :FUNCtion:SOURce2 <source>
:FUNCtion:SCALe <per_div>           :FUNCtion:OFFSet <offset>
:FUNCtion:WINDow HANNing|FLATtop|RECTangular|BHARris
:FUNCtion:CENTer <hz>               :FUNCtion:SPAN <hz>
```

`:FUNCtion:SOURce2?` is a command error for one-source operations, and an error
means no reply, so it is only queried for add, subtract, multiply and divide.

**Cursors**

```
:MARKer:MODE OFF|MANual|WAVeform|MEASurement
:MARKer:X1Y1source <source>         :MARKer:X2Y2source <source>
:MARKer:X1Position <s>              :MARKer:X2Position <s>
:MARKer:Y1Position <v>              :MARKer:Y2Position <v>
:MARKer:XDELta?                     :MARKer:YDELta?
```

**Measurements**

```
:MEASure:VPP <source>               :MEASure:FREQuency <source>
:MEASure:VAVerage <interval>,<source>
:MEASure:VRMS <interval>,<type>,<source>
:MEASure:CLEar
```

Argument shapes differ between measurements — VRMS takes an interval *and* a
type, the averaging ones take an interval, most take only a source. Sending the
wrong count is a command error, so the shape is recorded in `Measurements.cs`
rather than assumed at the call site. Adding `?` returns the value instead of
putting it on the display.

**Acquisition, run and screen**

```
:ACQuire:TYPE NORMal|AVERage|HRESolution|PEAK    :ACQuire:COUNt <n>
:RUN   :STOP   :SINGle   :AUToscale   :CDISplay   *RST
:HARDcopy:INKSaver ON|OFF           :DISPlay:DATA? PNG,COLor
:SYSTem:ERRor?
```

Two things learned the hard way and worth keeping:

`:SYSTem:HEADer` is an **Infiniium** command, not an InfiniiVision one. Sending
it leaves `-113` in the error queue, which then gets blamed on whatever fails
next.

`VisaTransport.Open` sets `TerminationCharacterEnabled = false` **on purpose**,
and the screenshot is read as one complete message rather than by byte count. A
USBTMC transfer is a message, not a stream: asking for exactly the header's byte
count leaves the transfer unfinished, and the next command collides with the
remainder.

## Working without the instrument

```bash
python3 tools/mock_scope.py
```

Serves the same SCPI on port 5025, including a synthesised PNG for
`:DISPlay:DATA?`. Point the app at `TCPIP0::127.0.0.1::5025::SOCKET`.

To check a real instrument accepts everything this app sends:

```bash
python3 tools/verify_scope.py 10.10.0.222
```

Read-only by default; `--write` also exercises the setting commands and restores
what it touched.

## Licence

Copyright © 2026 Ahsan Mehmood. All rights reserved.

## Author

**Ahsan Mehmood** — [ahsan.mehmood@outlook.com](mailto:ahsan.mehmood@outlook.com)

Version 8.0, 21 August 2026
