"""Interactive probe: python tests/e2e/probe.py "keys..." -- prints the screen after each step."""
import sys
import time

sys.path.insert(0, __file__.rsplit("\\", 1)[0].rsplit("/", 1)[0])
from conftest import Term  # noqa: E402

t = Term()
try:
    t.wait_prompt()
    for arg in sys.argv[1:]:
        if arg.startswith("k:"):
            t.keys(*arg[2:].split(","))
        elif arg.startswith("s:"):
            time.sleep(float(arg[2:]))
        else:
            t.type(arg)
        t.settle(0.6)
        print(f"=== after {arg!r} cursor={t.cursor()}")
        print("\n".join(l for l in t.lines()[:12]))
finally:
    t.close()
