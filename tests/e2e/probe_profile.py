"""Probe with the user's real profile: shows text and colors of the line under the cursor."""
import sys
import threading
import time

import pyte
from winpty import PtyProcess

screen = pyte.Screen(120, 30)
stream = pyte.Stream(screen)
lock = threading.Lock()
proc = PtyProcess.spawn(["pwsh", "-NoLogo"], dimensions=(30, 120))


def reader():
    while True:
        try:
            d = proc.read(4096)
        except Exception:
            break
        with lock:
            stream.feed(d)


threading.Thread(target=reader, daemon=True).start()
time.sleep(6)
for text in sys.argv[1:]:
    proc.write(text)
    time.sleep(1.5)
    with lock:
        y = screen.cursor.y
        line = screen.buffer[y]
        cells = [(line[x].data, line[x].fg, line[x].bg) for x in range(screen.columns)]
        print(f"--- after {text!r}: {screen.display[y].rstrip()!r}")
        prev = None
        for ch, fg, bg in cells:
            if ch == " " and fg == "default" and bg == "default":
                continue
            if (fg, bg) != prev:
                print(f"   [{fg}/{bg}]", end="")
                prev = (fg, bg)
            print(ch, end="")
        print()
    proc.write("\x03")
    time.sleep(1)
with lock:
    print("\n".join(l.rstrip() for l in screen.display[:8]))
proc.terminate(force=True)
