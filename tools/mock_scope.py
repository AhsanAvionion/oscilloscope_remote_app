#!/usr/bin/env python3
"""
Mock InfiniiVision 3000 X-Series instrument.

Speaks the SCPI subset the ScopeControl app uses, on TCP port 5025, and serves a
synthesised PNG for :DISPlay:DATA? so the whole display path can be exercised
without the real scope on the bench.

    python3 mock_scope.py [--port 5025] [--host 0.0.0.0]

Then point the app at:  TCPIP0::127.0.0.1::5025::SOCKET   (transport = Raw socket)
"""

import argparse
import math
import socket
import struct
import threading
import zlib

IDN = "KEYSIGHT TECHNOLOGIES,MSO-X 3024G,MY00000000,07.30.2021102614\n"

# ----------------------------------------------------------------- PNG writer


def _chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def make_png(width, height, pixel_fn):
    rows = bytearray()
    for y in range(height):
        rows.append(0)                       # filter type 0
        for x in range(width):
            r, g, b = pixel_fn(x, y)
            rows += bytes((r, g, b))
    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n"
            + _chunk(b"IHDR", header)
            + _chunk(b"IDAT", zlib.compress(bytes(rows), 6))
            + _chunk(b"IEND", b""))


CH_COLORS = [(255, 214, 0), (60, 207, 78), (64, 169, 243), (226, 85, 196)]


def render_screen(state, ink_saver):
    """A 512x300 stand-in for the instrument display: graticule plus one trace
    per enabled channel, scaled by that channel's volts/div."""
    w, h = 512, 300
    bg = (255, 255, 255) if ink_saver else (0, 0, 0)
    grid = (200, 200, 200) if ink_saver else (52, 56, 60)

    traces = []
    for n in range(1, 5):
        ch = state["chan"][n]
        if not ch["disp"]:
            continue
        amplitude = (h / 8.0) * (0.8 / ch["scale"])      # 0.8 Vpp test signal
        amplitude = max(2.0, min(amplitude, h / 2.0 - 4))
        offset_px = ch["offset"] / ch["scale"] * (h / 8.0)
        traces.append((n, amplitude, offset_px))

    def pixel(x, y):
        for n, amp, off in traces:
            cycles = 3.0
            cy = h / 2 - off - amp * math.sin(2 * math.pi * cycles * x / w + n)
            if abs(y - cy) < 1.6:
                return CH_COLORS[n - 1]
        if x % (w // 10) == 0 or y % (h // 8) == 0:
            return grid
        return bg

    return make_png(w, h, pixel)


# --------------------------------------------------------------- scope state


def new_state():
    return {
        "chan": {n: {"disp": n == 1, "scale": 1.0, "offset": 0.0,
                     "coup": "DC", "probe": 10.0, "bw": 0, "inv": 0}
                 for n in range(1, 5)},
        "tb": {"scale": 1e-3, "pos": 0.0, "ref": "CENT", "mode": "MAIN"},
        "trig": {"sweep": "AUTO", "mode": "EDGE", "src": "CHAN1",
                 "slope": "POS", "level": 0.0, "coup": "DC",
                 "nrej": 0, "hfrej": 0},
        "acq": {"type": "NORM", "count": 8},
        "inksaver": 0,
        "running": True,
        "errors": [],
    }


def short(word):
    """SCPI long form -> the short form the instrument echoes back."""
    return "".join(c for c in word if c.isupper() or c.isdigit()) or word.upper()


def num(token, default=0.0):
    try:
        return float(token)
    except (TypeError, ValueError):
        return default


def on_off(token):
    return 1 if token.strip().upper() in ("1", "ON", "TRUE") else 0


class Session:
    def __init__(self, state):
        self.s = state

    def handle(self, line):
        """Returns bytes to send back, or None for a command with no response."""
        line = line.strip()
        if not line:
            return None
        upper = line.upper()
        parts = line.split(None, 1)
        head = parts[0].upper()
        arg = parts[1].strip() if len(parts) > 1 else ""

        # ---- IEEE 488.2 common
        if head == "*IDN?":
            return IDN.encode()
        if head == "*RST":
            self.s.update(new_state())
            return None
        if head in ("*CLS",):
            self.s["errors"].clear()
            return None
        if head == "*OPC?":
            return b"1\n"

        if head.startswith(":SYSTEM:ERROR") or head.startswith(":SYST:ERR"):
            if self.s["errors"]:
                return (self.s["errors"].pop(0) + "\n").encode()
            return b'+0,"No error"\n'
        if head.startswith(":SYSTEM:HEADER") or head.startswith(":SYST:HEAD"):
            return b"0\n" if head.endswith("?") else None

        # ---- channels
        if upper.startswith(":CHAN"):
            return self.channel(head, arg)

        # ---- timebase
        if upper.startswith(":TIM"):
            return self.timebase(head, arg)

        # ---- trigger
        if upper.startswith(":TRIG"):
            return self.trigger(head, arg)

        # ---- acquire
        if upper.startswith(":ACQ"):
            return self.acquire(head, arg)

        # ---- run control
        if head == ":RUN":
            self.s["running"] = True
            return None
        if head == ":STOP":
            self.s["running"] = False
            return None
        if head == ":SINGLE" or head == ":SING":
            self.s["running"] = False
            return None
        if head in (":AUTOSCALE", ":AUT"):
            for n in range(1, 5):
                self.s["chan"][n]["scale"] = 0.2
            self.s["tb"]["scale"] = 1e-4
            return None
        if head in (":CDISPLAY", ":CDIS"):
            return None

        # ---- hardcopy / display
        if head.startswith(":HARDCOPY:INKSAVER") or head.startswith(":HARD:INKS"):
            if head.endswith("?"):
                return f"{self.s['inksaver']}\n".encode()
            self.s["inksaver"] = on_off(arg)
            return None
        if head.startswith(":DISPLAY:DATA") or head.startswith(":DISP:DATA"):
            png = render_screen(self.s, self.s["inksaver"])
            header = f"#{len(str(len(png)))}{len(png)}".encode()
            return header + png + b"\n"

        # ---- measurements
        if upper.startswith(":MEAS"):
            return self.measure(head, arg)

        self.s["errors"].append(f'-113,"Undefined header; {line}"')
        return None

    # --------------------------------------------------------------- groups

    def channel(self, head, arg):
        digits = "".join(c for c in head.split(":")[1] if c.isdigit())
        n = int(digits) if digits else 1
        if n not in self.s["chan"]:
            self.s["errors"].append('-222,"Data out of range; channel"')
            return None
        ch = self.s["chan"][n]
        field = head.split(":")[2].rstrip("?").upper()
        query = head.endswith("?")

        table = {
            "DISPLAY": ("disp", "bool"), "DISP": ("disp", "bool"),
            "SCALE": ("scale", "num"), "SCAL": ("scale", "num"),
            "OFFSET": ("offset", "num"), "OFFS": ("offset", "num"),
            "COUPLING": ("coup", "enum"), "COUP": ("coup", "enum"),
            "PROBE": ("probe", "num"), "PROB": ("probe", "num"),
            "BWLIMIT": ("bw", "bool"), "BWL": ("bw", "bool"),
            "INVERT": ("inv", "bool"), "INV": ("inv", "bool"),
            "IMPEDANCE": ("imp", "enum"), "IMP": ("imp", "enum"),
        }
        if field not in table:
            self.s["errors"].append(f'-113,"Undefined header; :CHANnel{n}:{field}"')
            return None

        key, kind = table[field]
        if query:
            value = ch.get(key, 0)
            if kind == "num":
                return f"{value:+.4E}\n".encode()
            if kind == "bool":
                return f"{int(value)}\n".encode()
            return f"{value}\n".encode()

        if kind == "num":
            v = num(arg)
            if key == "scale" and not (1e-3 <= v <= 5.0):
                self.s["errors"].append('-222,"Data out of range; :CHANnel:SCALe"')
                v = min(max(v, 1e-3), 5.0)
            ch[key] = v
        elif kind == "bool":
            ch[key] = on_off(arg)
        else:
            ch[key] = short(arg)
        return None

    def timebase(self, head, arg):
        field = head.split(":")[2].rstrip("?").upper()
        query = head.endswith("?")
        tb = self.s["tb"]
        if field in ("SCALE", "SCAL"):
            if query:
                return f"{tb['scale']:+.4E}\n".encode()
            v = num(arg)
            if not (1e-9 <= v <= 50):
                self.s["errors"].append('-222,"Data out of range; :TIMebase:SCALe"')
                v = min(max(v, 1e-9), 50.0)
            tb["scale"] = v
            return None
        if field in ("POSITION", "POS"):
            if query:
                return f"{tb['pos']:+.4E}\n".encode()
            tb["pos"] = num(arg)
            return None
        if field in ("REFERENCE", "REF"):
            if query:
                return f"{tb['ref']}\n".encode()
            tb["ref"] = short(arg)
            return None
        if field in ("MODE",):
            if query:
                return f"{tb['mode']}\n".encode()
            tb["mode"] = short(arg)
            return None
        self.s["errors"].append(f'-113,"Undefined header; :TIMebase:{field}"')
        return None

    def trigger(self, head, arg):
        segments = head.split(":")
        query = head.endswith("?")
        tr = self.s["trig"]
        field = segments[2].rstrip("?").upper()

        if field in ("SWEEP", "SWE"):
            if query:
                return f"{tr['sweep']}\n".encode()
            tr["sweep"] = short(arg)
            return None
        if field == "MODE":
            if query:
                return f"{tr['mode']}\n".encode()
            tr["mode"] = short(arg)
            return None
        if field in ("COUPLING", "COUP"):
            if query:
                return f"{tr['coup']}\n".encode()
            tr["coup"] = short(arg)
            return None
        if field in ("NREJECT", "NREJ"):
            if query:
                return f"{tr['nrej']}\n".encode()
            tr["nrej"] = on_off(arg)
            return None
        if field in ("HFREJECT", "HFR"):
            if query:
                return f"{tr['hfrej']}\n".encode()
            tr["hfrej"] = on_off(arg)
            return None
        if field == "EDGE" and len(segments) > 3:
            sub = segments[3].rstrip("?").upper()
            if sub in ("SOURCE", "SOUR"):
                if query:
                    return f"{tr['src']}\n".encode()
                tr["src"] = short(arg)
                return None
            if sub in ("SLOPE", "SLOP"):
                if query:
                    return f"{tr['slope']}\n".encode()
                tr["slope"] = short(arg)
                return None
            if sub in ("LEVEL", "LEV"):
                if query:
                    return f"{tr['level']:+.4E}\n".encode()
                tr["level"] = num(arg.split(",")[0])
                return None
        self.s["errors"].append(f'-113,"Undefined header; :TRIGger:{field}"')
        return None

    def acquire(self, head, arg):
        field = head.split(":")[2].rstrip("?").upper()
        query = head.endswith("?")
        acq = self.s["acq"]
        if field == "TYPE":
            if query:
                return f"{acq['type']}\n".encode()
            acq["type"] = short(arg)
            return None
        if field in ("COUNT", "COUN"):
            if query:
                return f"{int(acq['count'])}\n".encode()
            acq["count"] = int(num(arg, 8))
            return None
        self.s["errors"].append(f'-113,"Undefined header; :ACQuire:{field}"')
        return None

    def measure(self, head, arg):
        field = head.split(":")[2].rstrip("?").upper()
        source = (arg or "CHAN1").strip().upper()
        digits = "".join(c for c in source if c.isdigit())
        n = int(digits) if digits else 1
        if n in self.s["chan"] and not self.s["chan"][n]["disp"]:
            return b"+9.99999E+37\n"          # instrument's "no result" value
        if field in ("VPP",):
            return b"+8.00000E-01\n"
        if field in ("VMAX", "VMAX?"):
            return b"+4.00000E-01\n"
        if field in ("VMIN",):
            return b"-4.00000E-01\n"
        if field in ("FREQUENCY", "FREQ"):
            return b"+1.00000E+03\n"
        return b"+9.99999E+37\n"


def serve_client(conn, addr, state, verbose):
    conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    session = Session(state)
    buffer = b""
    try:
        while True:
            data = conn.recv(4096)
            if not data:
                break
            buffer += data
            while b"\n" in buffer:
                line, buffer = buffer.split(b"\n", 1)
                text = line.decode("ascii", "replace")
                reply = session.handle(text)
                if verbose:
                    shown = reply[:60] + b"..." if reply and len(reply) > 60 else reply
                    print(f"  {addr[0]} -> {text!r}   <- {shown!r}")
                if reply:
                    conn.sendall(reply)
    except (ConnectionResetError, BrokenPipeError):
        pass
    finally:
        conn.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=5025)
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args()

    state = new_state()
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((args.host, args.port))
    server.listen(5)
    print(f"Mock MSO-X 3024G listening on {args.host}:{args.port}")
    print(f"Point the app at TCPIP0::127.0.0.1::{args.port}::SOCKET (Raw socket transport)")

    try:
        while True:
            conn, addr = server.accept()
            if not args.quiet:
                print(f"Session opened from {addr[0]}")
            threading.Thread(target=serve_client,
                             args=(conn, addr, state, not args.quiet),
                             daemon=True).start()
    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        server.close()


if __name__ == "__main__":
    main()
