"""End-to-end harness: runs pwsh with ble.ps1 inside a ConPTY and renders its output with pyte."""
import os
import re
import tempfile
import threading
import time

import pyte
import pytest
from winpty import PtyProcess

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODULE = os.path.join(ROOT, "out", "ble", "ble.psd1")

KEYS = {
    "RET": "\r", "TAB": "\t", "S-TAB": "\x1b[Z", "ESC": "\x1b", "DEL": "\x7f", "SP": " ",
    "up": "\x1b[A", "down": "\x1b[B", "right": "\x1b[C", "left": "\x1b[D",
    "home": "\x1b[H", "end": "\x1b[F", "delete": "\x1b[3~",
    "C-left": "\x1b[1;5D", "C-right": "\x1b[1;5C",
}


def key(name):
    if name in KEYS:
        return KEYS[name]
    m = re.fullmatch(r"C-([a-z@\[\]\\^_])", name)
    if m:
        return chr(ord(m.group(1)) & 0x1F)
    m = re.fullmatch(r"M-(.)", name)
    if m:
        return "\x1b" + m.group(1)
    if len(name) == 1:
        return name
    raise ValueError(name)


class Term:
    def __init__(self, cols=100, rows=30, setup=""):
        self.cols, self.rows = cols, rows
        self.screen = pyte.Screen(cols, rows)
        self.stream = pyte.Stream(self.screen)
        self.lock = threading.Lock()
        self.tmp = tempfile.mkdtemp(prefix="ble-e2e-")
        hist = os.path.join(self.tmp, "history.txt").replace("'", "''")
        init = (
            f"Import-Module '{MODULE}'; "
            f"bleopt history_file='{hist}'; "
            "function global:prompt { 'PS> ' }; "
            f"Set-Location '{self.tmp}'; "
            + setup
        )
        self.proc = PtyProcess.spawn(
            ["pwsh", "-NoProfile", "-NoLogo", "-NoExit", "-Command", init],
            dimensions=(rows, cols),
        )
        self.alive = True
        self.reader = threading.Thread(target=self._read, daemon=True)
        self.reader.start()

    def _read(self):
        while self.alive:
            try:
                data = self.proc.read(4096)
            except EOFError:
                break
            except Exception:
                break
            if data:
                with self.lock:
                    self.stream.feed(data)

    def lines(self):
        with self.lock:
            return [l.rstrip() for l in self.screen.display]

    def text(self):
        return "\n".join(self.lines())

    def cursor(self):
        with self.lock:
            return self.screen.cursor.y, self.screen.cursor.x

    def line_at_cursor(self):
        y, _ = self.cursor()
        return self.lines()[y]

    def wait(self, pred, timeout=15.0, msg=""):
        end = time.time() + timeout
        while time.time() < end:
            if pred():
                return
            time.sleep(0.05)
        raise AssertionError(f"timeout waiting for {msg}\n--- screen ---\n{self.text()}")

    def wait_text(self, s, timeout=15.0):
        self.wait(lambda: s in self.text(), timeout, repr(s))

    def wait_prompt(self, timeout=20.0):
        """Waits until the cursor sits right after an empty 'PS> ' prompt (a right prompt may follow)."""
        def ok():
            y, x = self.cursor()
            line = self.lines()[y]
            return x == 4 and line.startswith("PS>") and (line[4:] == "" or line[4:].startswith("    "))
        self.wait(ok, timeout, "empty prompt")

    def send(self, s):
        self.proc.write(s)

    def keys(self, *names, delay=0.08):
        for n in names:
            self.send(key(n))
            time.sleep(delay)

    def type(self, s, delay=0.02):
        for ch in s:
            self.send(ch)
            time.sleep(delay)

    def settle(self, t=0.4):
        time.sleep(t)

    def close(self):
        self.alive = False
        try:
            self.proc.terminate(force=True)
        except Exception:
            pass


@pytest.fixture
def term():
    t = Term()
    try:
        t.wait_prompt()
        yield t
    finally:
        t.close()


@pytest.fixture
def make_term():
    terms = []

    def factory(**kw):
        t = Term(**kw)
        terms.append(t)
        t.wait_prompt()
        return t

    yield factory
    for t in terms:
        t.close()
