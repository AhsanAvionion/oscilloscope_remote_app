# ScopeControl

A Windows desktop application for driving a Keysight InfiniiVision MSO-X 3024G
oscilloscope over Ethernet. The window is laid out like the instrument itself:
the live display fills the top, the four colour-coded channel strips run along
the bottom, and horizontal, trigger, acquisition and screen controls sit in the
right-hand column.

Tested against an MSO-X 3024G on firmware 07.60. Most of it should work on any
InfiniiVision 3000/4000 X-Series, since they share a command set, but that is
untested.

## What it does

**Vertical** — turn CH1–CH4 on and off, volts/div from 1 mV to 5 V, offset,
AC/DC coupling, probe attenuation, 20 MHz bandwidth limit, invert.

**Horizontal** — time/div from 1 ns to 50 s, delay, time reference.

**Trigger** — Auto or Normal sweep, trigger type, source (CH1–4, external, line,
wave gen), edge slope rising / falling / either / alternating, level with a
Set 50% key, coupling, noise and HF reject.

**Acquisition** — normal, averaging, high resolution, peak detect, average
count, plus Run, Stop, Single, Auto scale, Default setup, Clear display.

**Live display** — pulls the instrument screen as a PNG on a timer, with Vpp and
frequency readouts and a Save as PNG button.

**Follows the front panel** — re-reads every setting on a timer, so if someone
is turning knobs at the bench their changes appear here rather than leaving the
controls showing stale values.

**SCPI console** — hidden by default (F12, or the key in the top strip). Every
command and reply is timestamped, and you can type raw SCPI yourself.

## Requirements

- Windows, .NET Framework 4.8
- An instrument reachable over LAN
- For the VISA transports: Keysight IO Libraries Suite (or NI-VISA)

## Connecting

Three transports, selected in the dropdown. All three take the same address and
send identical SCPI, so nothing above the transport layer changes.

| Transport | Needs | Address form |
| --- | --- | --- |
| **Raw socket : 5025** | nothing | `TCPIP0::10.10.0.222::5025::SOCKET` |
| **VISA.NET** | `Ivi.Visa.dll` + a vendor implementation | `TCPIP0::10.10.0.222::INSTR` |
| **VISA-COM** | IO Libraries (registers `VISA.GlobalRM`) | `TCPIP0::10.10.0.222::INSTR` |

**Start with the raw socket if you are in a hurry.** The instrument speaks plain
SCPI on port 5025 and it needs no drivers, no NuGet packages and no
registration. It is a good way to confirm the network path before debugging
anything else.

The app connects on startup using whatever you used last, and remembers the
address, transport, refresh rate and window layout in
`%APPDATA%\ScopeControl\settings.txt` — plain key=value text you can edit or
delete.

## Building

```
git clone <this repo>
cd ScopeControl
dotnet build -c Release
```

Or open the folder in Visual Studio and press F5. **Build as x64** — the VISA
shared components are 64-bit and a 32-bit process cannot load them. The csproj
already sets `Prefer32Bit=false`.

### VISA.NET is not on NuGet

Restoring `Ivi.Visa` or `Keysight.Visa` from nuget.org fails with NU1101; those
package IDs do not exist. The assemblies ship with IO Libraries, so the project
references `Ivi.Visa.dll` from disk. Find yours:

```powershell
Get-ChildItem "C:\Program Files\IVI Foundation" -Recurse -Filter Ivi.Visa.dll |
  Select-Object -ExpandProperty FullName
```

and set the `IviVisaDll` property in `ScopeControl.csproj`. A wrong path fails
the build with a message naming the path it tried, rather than a wall of
"type or namespace not found".

`Keysight.Visa.dll` is deliberately **not** referenced.
`GlobalResourceManager` locates the vendor implementation at run time, and if
the IVI registration is missing, `VisaImplementationLoader` finds it in the GAC
by reflection.

## Troubleshooting

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

**`DllNotFoundException: ktvisa32.dll`** — a 32-bit build looking for 32-bit
VISA. Untick *Prefer 32-bit* and rebuild.

**Nothing appears and the process exits** — check `ScopeControl-error.log` next
to the exe. Startup exceptions are caught and written there.

**Text is clipped** — the layout scales with DPI via `AutoScaleMode.Dpi`. If
something still overflows at high scaling, removing the `DpiAwareness` line from
`App.config` makes Windows bitmap-scale the whole window instead.

## Known limitations

**Softkeys cannot be pressed remotely.** The InfiniiVision command set exposes
settings directly rather than emulating key presses, and there is no query for
what the current softkey labels say. Use the instrument's own browser-based
remote front panel at `http://<ip>/` if you need menu navigation.

**Selecting a channel is a workaround.** The graticule voltage labels down the
left edge belong to whichever channel the instrument considers selected, and
there is no "select channel" command. Switching a channel's display on is what
selects it, so the **Grid** key cycles the display off and on. Expect a brief
flicker on a channel that was already visible.

**Scale values off the 1-2-5 list.** The instrument can sit at 420 ns/div after
an autoscale. Rather than snapping to the nearest standard step and showing
something untrue, the exact value is inserted into the dropdown.

## Project layout

| Path | Role |
| --- | --- |
| `Instrument/IScopeTransport.cs` | Link abstraction: write a line, read a line, read N bytes |
| `Instrument/SocketTransport.cs` | Raw TCP port 5025 |
| `Instrument/VisaTransport.cs` | VISA.NET via `GlobalResourceManager` |
| `Instrument/VisaComTransport.cs` | VISA-COM, late bound so it needs no reference |
| `Instrument/VisaImplementationLoader.cs` | Finds a vendor implementation when discovery fails |
| `Instrument/KeysightScope.cs` | Every SCPI command, serialised behind one gate |
| `Instrument/Eng.cs` | Engineering notation in and out |
| `UI/MainForm.cs` | Layout and event wiring |
| `UI/ChannelPanel.cs` | One vertical channel strip |
| `UI/ScopeScreen.cs` | Display area; draws a graticule until an image arrives |
| `UI/EngBox.cs` | Value entry with step and zero keys |
| `AppSettings.cs` | Persisted preferences |
| `tools/mock_scope.py` | Fake instrument on port 5025, for working without hardware |
| `tools/verify_scope.py` | Checks every SCPI command this app sends against a real scope |

Instrument I/O never runs on the UI thread: `KeysightScope` wraps each call in
`Task.Run` behind a `SemaphoreSlim`, so the window stays responsive and two
commands can never interleave on the wire.

## SCPI reference

Vertical

```
:CHANnel<n>:DISPlay ON|OFF          :CHANnel<n>:SCALe <volts_per_div>
:CHANnel<n>:OFFSet <volts>          :CHANnel<n>:COUPling AC|DC
:CHANnel<n>:PROBe <attenuation>     :CHANnel<n>:BWLimit ON|OFF
:CHANnel<n>:INVert ON|OFF
```

Horizontal

```
:TIMebase:SCALe <sec_per_div>       :TIMebase:POSition <seconds>
:TIMebase:REFerence LEFT|CENTer|RIGHt
```

Trigger

```
:TRIGger:SWEep AUTO|NORMal          :TRIGger:MODE EDGE|GLITch|PATTern
:TRIGger:EDGE:SOURce CHANnel<n>|EXTernal|LINE|WGEN
:TRIGger:EDGE:SLOPe POSitive|NEGative|EITHer|ALTernate
:TRIGger:EDGE:LEVel <volts>         :TRIGger:COUPling DC|AC|LFReject
:TRIGger:NREJect ON|OFF             :TRIGger:HFReject ON|OFF
```

Acquisition, run and screen

```
:ACQuire:TYPE NORMal|AVERage|HRESolution|PEAK    :ACQuire:COUNt <n>
:RUN   :STOP   :SINGle   :AUToscale   :CDISplay   *RST
:HARDcopy:INKSaver ON|OFF           :DISPlay:DATA? PNG,COLor
:MEASure:VPP? <source>              :MEASure:FREQuency? <source>
:SYSTem:ERRor?
```

The screenshot arrives as an IEEE 488.2 definite-length block
(`#8<8 digits><png bytes>`). Note that `VisaTransport.Open` sets
`TerminationCharacterEnabled = false` on purpose — leave it off, or the first
`0x0A` byte inside the PNG will truncate the transfer.

## Working without the instrument

```
python3 tools/mock_scope.py
```

Serves the same SCPI on port 5025, including a synthesised PNG for
`:DISPlay:DATA?`. Point the app at `TCPIP0::127.0.0.1::5025::SOCKET`.

To check a real instrument accepts everything this app sends:

```
python3 tools/verify_scope.py 10.10.0.222
```

Read-only by default; `--write` also exercises the setting commands.

## Licence

MIT
