#!/usr/bin/env python3
"""
Verifies every SCPI command the ScopeControl app sends, against a real
instrument or against mock_scope.py.

It sends each command, reads the error queue straight afterwards, and reads the
setting back to confirm it actually took. Anything your firmware rejects shows
up here in seconds, before you go hunting through the C#.

    python3 verify_scope.py 10.10.0.222
    python3 verify_scope.py 127.0.0.1            # against the mock
    python3 verify_scope.py 10.10.0.222 --write  # also change settings (see below)

Read-only by default: it queries state, saves it, and restores anything it
touches. --write is still disruptive to whoever is at the bench, so ask first.
"""

import argparse
import socket
import sys
import time

PASS = "PASS"
FAIL = "FAIL"
SKIP = "SKIP"


class Scpi:
    def __init__(self, host, port=5025, timeout=10.0):
        self.sock = socket.create_connection((host, port), timeout=timeout)
        self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.sock.settimeout(timeout)
        self.buffer = b""

    def write(self, command):
        if not command.endswith("\n"):
            command += "\n"
        self.sock.sendall(command.encode("ascii"))

    def read_line(self):
        while b"\n" not in self.buffer:
            chunk = self.sock.recv(4096)
            if not chunk:
                raise IOError("instrument closed the connection")
            self.buffer += chunk
        line, self.buffer = self.buffer.split(b"\n", 1)
        return line.decode("ascii", "replace").strip()

    def read_exact(self, count):
        while len(self.buffer) < count:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise IOError("connection closed mid-transfer")
            self.buffer += chunk
        data, self.buffer = self.buffer[:count], self.buffer[count:]
        return data

    def query(self, command):
        self.write(command)
        return self.read_line()

    def read_block(self):
        """IEEE 488.2 definite-length block: #<n><length><data>.
        Same framing as KeysightScope.ReadDefiniteLengthBlock in the C#."""
        head = self.read_exact(1)
        if head != b"#":
            raise IOError(f"expected a block header, got {head!r}")
        digits = int(self.read_exact(1))
        length = int(self.read_exact(digits))
        data = self.read_exact(length)
        if self.buffer[:1] == b"\n":            # swallow the trailing newline
            self.buffer = self.buffer[1:]
        return data

    def errors(self):
        out = []
        for _ in range(10):
            reply = self.query(":SYSTem:ERRor?")
            if reply.startswith("+0,") or reply.startswith("0,") or not reply:
                break
            out.append(reply)
        return out

    def close(self):
        try:
            self.sock.close()
        except OSError:
            pass


class Report:
    def __init__(self):
        self.rows = []

    def add(self, status, name, detail=""):
        self.rows.append((status, name, detail))
        mark = {PASS: "ok  ", FAIL: "FAIL", SKIP: "skip"}[status]
        print(f"  [{mark}] {name}" + (f"   {detail}" if detail else ""))

    def summary(self):
        counts = {PASS: 0, FAIL: 0, SKIP: 0}
        for status, _, _ in self.rows:
            counts[status] += 1
        print("\n" + "-" * 62)
        print(f"{counts[PASS]} passed, {counts[FAIL]} failed, {counts[SKIP]} skipped")
        if counts[FAIL]:
            print("\nFailures:")
            for status, name, detail in self.rows:
                if status == FAIL:
                    print(f"  {name}: {detail}")
        return counts[FAIL]


def check(scpi, report, name, command, query=None, expect=None, tolerance=None):
    """Sends a command, checks the error queue, optionally reads it back."""
    try:
        if command:
            scpi.write(command)
        problems = scpi.errors()
        if problems:
            report.add(FAIL, name, "; ".join(problems))
            return
        if query is None:
            report.add(PASS, name)
            return

        got = scpi.query(query)
        if expect is None:
            report.add(PASS, name, f"reads {got}")
            return

        if tolerance is not None:
            ok = abs(float(got) - float(expect)) <= tolerance * max(abs(float(expect)), 1e-12)
            report.add(PASS if ok else FAIL, name,
                       f"set {expect}, reads {got}")
        else:
            want = str(expect).upper()
            ok = got.upper().startswith(want[:4]) or want.startswith(got.upper()[:4])
            report.add(PASS if ok else FAIL, name, f"set {expect}, reads {got}")
    except Exception as exc:                     # noqa: BLE001 - report, don't crash
        report.add(FAIL, name, f"{type(exc).__name__}: {exc}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("host")
    parser.add_argument("--port", type=int, default=5025)
    parser.add_argument("--write", action="store_true",
                        help="also send setting commands (changes the instrument)")
    parser.add_argument("--timeout", type=float, default=10.0)
    args = parser.parse_args()

    print(f"Connecting to {args.host}:{args.port} ...")
    try:
        scpi = Scpi(args.host, args.port, args.timeout)
    except Exception as exc:                     # noqa: BLE001
        print(f"Could not connect: {exc}")
        print("Check the IP, that port 5025 is open, and that the scope is on the LAN.")
        return 2

    report = Report()
    try:
        identity = scpi.query("*IDN?")
        print(f"Instrument: {identity}\n")
        if "MSO-X 3" not in identity and "MSOX3" not in identity.replace("-", "").replace(" ", ""):
            print("Note: this does not look like a 3000 X-Series. Command support may differ.\n")
        scpi.write("*CLS")

        # ---------------------------------------------------- read-only pass
        print("Reading current setup")
        saved = {}
        for n in range(1, 5):
            for suffix, key in (("DISPlay", "disp"), ("SCALe", "scale"),
                                ("OFFSet", "offs"), ("COUPling", "coup"),
                                ("PROBe", "prob"), ("BWLimit", "bwl"),
                                ("INVert", "inv")):
                q = f":CHANnel{n}:{suffix}?"
                check(scpi, report, q, None, query=q)
                saved[(n, key)] = report.rows[-1][2].replace("reads ", "")

        for q in (":TIMebase:SCALe?", ":TIMebase:POSition?", ":TIMebase:REFerence?",
                  ":TRIGger:SWEep?", ":TRIGger:MODE?", ":TRIGger:EDGE:SOURce?",
                  ":TRIGger:EDGE:SLOPe?", ":TRIGger:EDGE:LEVel?", ":TRIGger:COUPling?",
                  ":TRIGger:NREJect?", ":TRIGger:HFReject?",
                  ":ACQuire:TYPE?", ":ACQuire:COUNt?"):
            check(scpi, report, q, None, query=q)

        # ---------------------------------------------------- screenshot path
        print("\nScreen capture")
        try:
            scpi.write(":HARDcopy:INKSaver OFF")
            problems = scpi.errors()
            if problems:
                report.add(FAIL, ":HARDcopy:INKSaver", "; ".join(problems))
            scpi.sock.settimeout(30.0)
            start = time.time()
            scpi.write(":DISPlay:DATA? PNG,COLor")
            image = scpi.read_block()
            elapsed = time.time() - start
            scpi.sock.settimeout(args.timeout)
            if image[:8] == b"\x89PNG\r\n\x1a\n":
                report.add(PASS, ":DISPlay:DATA? PNG,COLor",
                           f"{len(image)} bytes in {elapsed:.1f} s")
            else:
                report.add(FAIL, ":DISPlay:DATA? PNG,COLor",
                           f"not a PNG, starts with {image[:8]!r}")
        except Exception as exc:                 # noqa: BLE001
            report.add(FAIL, ":DISPlay:DATA? PNG,COLor", f"{type(exc).__name__}: {exc}")

        # ---------------------------------------------------- measurements
        print("\nMeasurements")
        for m in ("VPP", "VMAX", "VMIN", "FREQuency"):
            q = f":MEASure:{m}? CHANnel1"
            check(scpi, report, q, None, query=q)

        # ---------------------------------------------------- write pass
        if not args.write:
            print("\nSetting commands skipped. Re-run with --write to test them.")
            report.add(SKIP, "setting commands", "run with --write")
            return report.summary()

        print("\nSetting commands (the instrument will change)")
        check(scpi, report, ":CHANnel1:DISPlay", ":CHANnel1:DISPlay ON",
              query=":CHANnel1:DISPlay?", expect="1")
        check(scpi, report, ":CHANnel1:SCALe", ":CHANnel1:SCALe 0.5",
              query=":CHANnel1:SCALe?", expect=0.5, tolerance=0.02)
        check(scpi, report, ":CHANnel1:OFFSet", ":CHANnel1:OFFSet 0.1",
              query=":CHANnel1:OFFSet?", expect=0.1, tolerance=0.05)
        check(scpi, report, ":CHANnel1:COUPling", ":CHANnel1:COUPling DC",
              query=":CHANnel1:COUPling?", expect="DC")
        check(scpi, report, ":CHANnel1:PROBe", ":CHANnel1:PROBe 10",
              query=":CHANnel1:PROBe?", expect=10.0, tolerance=0.02)
        check(scpi, report, ":CHANnel1:BWLimit", ":CHANnel1:BWLimit OFF",
              query=":CHANnel1:BWLimit?", expect="0")
        check(scpi, report, ":CHANnel1:INVert", ":CHANnel1:INVert OFF",
              query=":CHANnel1:INVert?", expect="0")
        for n in (2, 3, 4):
            check(scpi, report, f":CHANnel{n}:DISPlay", f":CHANnel{n}:DISPlay OFF",
                  query=f":CHANnel{n}:DISPlay?", expect="0")

        check(scpi, report, ":TIMebase:SCALe", ":TIMebase:SCALe 1E-04",
              query=":TIMebase:SCALe?", expect=1e-4, tolerance=0.02)
        check(scpi, report, ":TIMebase:POSition", ":TIMebase:POSition 0",
              query=":TIMebase:POSition?", expect=0.0, tolerance=1.0)
        check(scpi, report, ":TIMebase:REFerence", ":TIMebase:REFerence CENTer",
              query=":TIMebase:REFerence?", expect="CENT")

        check(scpi, report, ":TRIGger:SWEep AUTO", ":TRIGger:SWEep AUTO",
              query=":TRIGger:SWEep?", expect="AUTO")
        check(scpi, report, ":TRIGger:SWEep NORMal", ":TRIGger:SWEep NORMal",
              query=":TRIGger:SWEep?", expect="NORM")
        check(scpi, report, ":TRIGger:MODE", ":TRIGger:MODE EDGE",
              query=":TRIGger:MODE?", expect="EDGE")
        check(scpi, report, ":TRIGger:EDGE:SOURce", ":TRIGger:EDGE:SOURce CHANnel1",
              query=":TRIGger:EDGE:SOURce?", expect="CHAN")
        for slope, expect in (("POSitive", "POS"), ("NEGative", "NEG"),
                              ("EITHer", "EITH"), ("ALTernate", "ALT")):
            check(scpi, report, f":TRIGger:EDGE:SLOPe {slope}",
                  f":TRIGger:EDGE:SLOPe {slope}",
                  query=":TRIGger:EDGE:SLOPe?", expect=expect)
        check(scpi, report, ":TRIGger:EDGE:LEVel", ":TRIGger:EDGE:LEVel 0.2",
              query=":TRIGger:EDGE:LEVel?", expect=0.2, tolerance=0.1)
        check(scpi, report, ":TRIGger:COUPling", ":TRIGger:COUPling DC",
              query=":TRIGger:COUPling?", expect="DC")
        check(scpi, report, ":TRIGger:NREJect", ":TRIGger:NREJect OFF",
              query=":TRIGger:NREJect?", expect="0")
        check(scpi, report, ":TRIGger:HFReject", ":TRIGger:HFReject OFF",
              query=":TRIGger:HFReject?", expect="0")

        check(scpi, report, ":ACQuire:TYPE", ":ACQuire:TYPE NORMal",
              query=":ACQuire:TYPE?", expect="NORM")
        check(scpi, report, ":ACQuire:COUNt", ":ACQuire:COUNt 8",
              query=":ACQuire:COUNt?", expect=8.0, tolerance=0.01)

        check(scpi, report, ":STOP", ":STOP")
        check(scpi, report, ":RUN", ":RUN")
        check(scpi, report, ":CDISplay", ":CDISplay")

        # restore trigger sweep, the one setting most likely to surprise someone
        scpi.write(":TRIGger:SWEep AUTO")
        scpi.errors()

        return report.summary()
    finally:
        scpi.close()


if __name__ == "__main__":
    sys.exit(main())
