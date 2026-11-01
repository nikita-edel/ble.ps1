"""The part of pywinpty's PtyProcess the tests use, on a Linux/macOS pty."""
import fcntl
import os
import pty
import select
import signal
import struct
import termios


class PtyProcess:
    def __init__(self, pid, fd):
        self.pid, self.fd = pid, fd

    @classmethod
    def spawn(cls, argv, dimensions=(24, 80)):
        pid, fd = pty.fork()
        if pid == 0:
            os.environ.setdefault("TERM", "xterm-256color")
            os.execvp(argv[0], argv)
        fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack("HHHH", dimensions[0], dimensions[1], 0, 0))
        return cls(pid, fd)

    def setwinsize(self, rows, cols):
        fcntl.ioctl(self.fd, termios.TIOCSWINSZ, struct.pack("HHHH", rows, cols, 0, 0))

    def read(self, size=4096):
        while True:
            if select.select([self.fd], [], [], 0.2)[0]:
                try:
                    data = os.read(self.fd, size)
                except OSError:
                    raise EOFError from None
                if not data:
                    raise EOFError
                return data.decode("utf-8", "replace")

    def write(self, s):
        os.write(self.fd, s.encode())

    def terminate(self, force=False):
        try:
            os.kill(self.pid, signal.SIGKILL if force else signal.SIGTERM)
            os.waitpid(self.pid, 0)
        except OSError:
            pass
        try:
            os.close(self.fd)
        except OSError:
            pass
