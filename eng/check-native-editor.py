#!/usr/bin/env python3
"""Exercise the Release editor through X11 input and real GTK portal dialogs."""

import contextlib
import ctypes
import json
import os
import platform
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

from PIL import ImageGrab

ROOT = Path(__file__).resolve().parents[1]


def wait(description, probe):
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        result = probe()
        if result:
            return result
        time.sleep(0.1)
    raise RuntimeError(f"Timed out: {description}")


class Desktop:
    def __init__(self, env, evidence):
        self.env = env
        self.evidence = evidence
        self.steps = []

    def xdo(self, *args, required=True):
        result = subprocess.run(
            ["xdotool", *map(str, args)], env=self.env, capture_output=True, text=True, check=False, timeout=10
        )
        if required and result.returncode:
            raise RuntimeError(result.stderr or f"xdotool failed: {args}")
        return result.stdout.strip()

    def find(self, title):
        candidates = self.xdo("search", "--onlyvisible", "--class", ".", required=False).splitlines()
        return next((w for w in candidates if self.xdo("getwindowname", w, required=False) == title), None)

    def window(self, title):
        return wait(f"window {title}", lambda: self.find(title))

    def focus(self, window):
        self.xdo("windowfocus", "--sync", window)

    def key(self, keys):
        self.xdo("key", "--clearmodifiers", keys)

    def type(self, text):
        self.xdo("type", "--clearmodifiers", "--delay", 10, "--", text)

    def title(self, path=None, dirty=False):
        return f"{path or 'Untitled'}{' *' if dirty else ''} - Tangopad"

    def editor(self, path=None, dirty=False):
        window = self.window(self.title(path, dirty))
        self.focus(window)
        return window

    def picker(self, shortcut, title):
        self.key(shortcut)
        window = self.window(title)
        self.focus(window)
        return window

    def choose(self, path, saving):
        # GTK Save starts in the Name field. Open uses the location field.
        if not saving:
            self.key("ctrl+l")
        self.key("ctrl+a")
        self.type(str(path))
        # GTK posts filename completion after the text input event.
        time.sleep(0.5)
        title = "Save text file" if saving else "Open text file"
        window = self.window(title)
        geometry = dict(line.split("=", 1) for line in self.xdo("getwindowgeometry", "--shell", window).splitlines())
        self.click(window, int(geometry["WIDTH"]) - 50, int(geometry["HEIGHT"]) - 24)
        if path.exists():
            # The first click can dismiss GTK's filename completion popup.
            # A second click cannot accept the centered overwrite dialog.
            time.sleep(0.2)
            if self.find(title) == window:
                self.click(window, int(geometry["WIDTH"]) - 50, int(geometry["HEIGHT"]) - 24)

    def overwrite(self):
        def child():
            windows = self.xdo("search", "--onlyvisible", "--class", "Xdg-desktop-portal-gtk", required=False).splitlines()
            return next((w for w in windows if self.xdo("getwindowname", w, required=False) == ""), None)
        window = wait("native overwrite confirmation", child)
        self.focus(window)
        return window

    def gone(self, title):
        wait(f"closed {title}", lambda: self.find(title) is None)

    def bytes(self, path, expected):
        wait(f"exact bytes in {path.name}", lambda: path.exists() and path.read_bytes() == expected)

    def click(self, window, x, y):
        self.focus(window)
        self.xdo("mousemove", "--window", window, x, y, "click", 1)

    def decision(self, choice):
        window = self.window("Unsaved changes")
        # These positions belong to the app's fixed 460-pixel dialog.
        self.click(window, {"Save": 270, "Discard": 336, "Cancel": 410}[choice], 80)
        self.gone("Unsaved changes")

    def record(self, name):
        ImageGrab.grab(xdisplay=self.env["DISPLAY"]).save(self.evidence / f"{len(self.steps):02}-{name}.png")
        self.steps.append(name)
        print(f"PASS: {name}", flush=True)

    def close(self, window):
        # Send WM_DELETE_WINDOW directly. Xvfb does not run a window manager.
        x11 = ctypes.CDLL("libX11.so.6")
        x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
        x11.XOpenDisplay.restype = ctypes.c_void_p
        x11.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
        x11.XInternAtom.restype = ctypes.c_ulong
        x11.XSendEvent.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_long, ctypes.c_void_p]
        x11.XFlush.argtypes = [ctypes.c_void_p]
        x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
        display = x11.XOpenDisplay(self.env["DISPLAY"].encode())
        if not display:
            raise RuntimeError("Cannot open the test X11 display.")
        try:
            # XClientMessageEvent uses the LP64 Xlib layout on this test host.
            class Message(ctypes.Structure):
                _fields_ = [("type", ctypes.c_int), ("serial", ctypes.c_ulong),
                            ("send_event", ctypes.c_int), ("display", ctypes.c_void_p),
                            ("window", ctypes.c_ulong), ("message_type", ctypes.c_ulong),
                            ("format", ctypes.c_int), ("data", ctypes.c_long * 5)]

            event = Message(type=33, display=display, window=int(window), format=32,
                            message_type=x11.XInternAtom(display, b"WM_PROTOCOLS", 0))
            event.data[0] = x11.XInternAtom(display, b"WM_DELETE_WINDOW", 0)
            if not x11.XSendEvent(display, int(window), 0, 0, ctypes.byref(event)):
                raise RuntimeError("Cannot send WM_DELETE_WINDOW.")
            x11.XFlush(display)
        finally:
            x11.XCloseDisplay(display)


def exercise(ui, files, app):
    first, second, output = (files / name for name in ("first.txt", "second.MD", "round trip.markdown"))
    fixture = files / "Unicode input.MARKDOWN"
    fixture.write_bytes("λ café 😀\r\nsecond\nthird\rfourth".encode("utf-8"))
    invalid = files / "invalid.txt"
    invalid.write_bytes(b"\xff\xfeinvalid")
    overwrite = files / "overwrite.txt"
    overwrite.write_bytes(b"keep existing")
    blocked = files / "blocked"
    blocked.mkdir()
    try:
        ui.editor()
        ui.type("typed")
        ui.editor(dirty=True)
        ui.record("typing-and-dirty-title")
        for shortcut, title in (("ctrl+s", "Save text file"), ("ctrl+shift+s", "Save text file"), ("ctrl+o", "Open text file")):
            ui.picker(shortcut, title)
            ui.key("Escape")
            ui.gone(title)
            ui.editor(dirty=True)
        ui.record("cancel-open-save-save-as")

        ui.picker("ctrl+s", "Save text file")
        ui.choose(first, saving=True)
        ui.bytes(first, b"typed")
        ui.editor(first)
        ui.key("ctrl+End")
        ui.key("Return")
        ui.type("edited")
        ui.editor(first, dirty=True)
        ui.key("ctrl+s")
        ui.bytes(first, b"typed\nedited")
        ui.editor(first)
        ui.picker("ctrl+shift+s", "Save text file")
        ui.choose(second, saving=True)
        ui.bytes(second, b"typed\nedited")
        ui.bytes(first, b"typed\nedited")
        ui.editor(second)
        ui.record("save-and-save-as-local-paths")

        ui.type("!")
        ui.editor(second, dirty=True)
        ui.picker("ctrl+shift+s", "Save text file")
        ui.choose(overwrite, saving=True)
        # GTK's overwrite dialog has the same title as the file picker.
        ui.overwrite()
        ui.record("overwrite-prompt")
        ui.key("Escape")
        ui.bytes(overwrite, b"keep existing")
        # Cancelling overwrite returns to the Save picker.
        ui.focus(ui.window("Save text file"))
        ui.key("Escape")
        ui.gone("Save text file")
        ui.editor(second, dirty=True)
        ui.picker("ctrl+shift+s", "Save text file")
        ui.choose(overwrite, saving=True)
        ui.overwrite()
        ui.key("alt+r")
        ui.bytes(overwrite, b"typed\nedited!")
        ui.editor(overwrite)
        ui.record("overwrite-cancel-and-replace")

        ui.picker("ctrl+o", "Open text file")
        ui.choose(fixture, saving=False)
        ui.editor(fixture)
        ui.picker("ctrl+shift+s", "Save text file")
        ui.choose(output, saving=True)
        ui.bytes(output, "λ café 😀\r\nsecond\nthird\rfourth".encode("utf-8"))
        ui.editor(output)
        ui.record("unicode-and-mixed-newlines")

        ui.key("ctrl+End")
        ui.type("!")
        ui.key("ctrl+n")
        ui.decision("Cancel")
        ui.editor(output, dirty=True)
        ui.key("ctrl+n")
        ui.decision("Save")
        ui.editor()
        ui.bytes(output, "λ café 😀\r\nsecond\nthird\rfourth!".encode("utf-8"))
        ui.type("discard me")
        ui.key("ctrl+n")
        ui.decision("Discard")
        ui.editor()
        ui.record("new-cancel-save-discard")

        markdown = files / "highlighting.md"
        markdown_text = b"# Heading\r\n**strong** and *emphasis* and `code`\n[link](https://example.com)\n> quote\n- item\n1. item\n```\n# fenced code\n```\nordinary text"
        markdown.write_bytes(markdown_text)
        ui.picker("ctrl+o", "Open text file")
        ui.choose(markdown, saving=False)
        ui.editor(markdown)
        ui.record("markdown-highlighting")
        plain = files / "highlighting.txt"
        ui.picker("ctrl+shift+s", "Save text file")
        ui.choose(plain, saving=True)
        ui.editor(plain)
        ui.bytes(plain, markdown_text)
        ui.record("markdown-to-plain-text")
        ui.key("ctrl+End")
        ui.type("!")
        ui.key("ctrl+z")
        ui.key("ctrl+s")
        ui.bytes(plain, markdown_text)
        ui.key("ctrl+n")
        ui.editor()
        ui.type("retain me")
        ui.picker("ctrl+o", "Open text file")
        ui.choose(invalid, saving=False)
        ui.decision("Discard")
        error = ui.window("File operation failed")
        ui.record("invalid-utf8-error")
        ui.focus(error)
        ui.key("Escape")
        ui.gone("File operation failed")
        ui.editor(dirty=True)
        ui.picker("ctrl+s", "Save text file")
        restricted = blocked / "denied.txt"
        ui.choose(restricted, saving=True)
        ui.bytes(restricted, b"retain me")
        ui.editor(restricted)
        ui.type("!")
        blocked.chmod(0o500)
        ui.key("ctrl+s")
        error = ui.window("File operation failed")
        ui.record("permission-error")
        ui.focus(error)
        ui.key("Escape")
        ui.gone("File operation failed")
        ui.editor(restricted, dirty=True)
        ui.bytes(restricted, b"retain me")
        blocked.chmod(0o700)
        ui.picker("ctrl+shift+s", "Save text file")
        recovered = files / "recovered.txt"
        ui.choose(recovered, saving=True)
        ui.bytes(recovered, b"retain me!")
        ui.editor(recovered)
        ui.key("ctrl+n")
        ui.editor()
        ui.type("banana banana")
        ui.key("ctrl+h")
        ui.type("ana")
        ui.key("Return")
        ui.key("shift+Return")
        ui.key("Tab")
        ui.type("$1")
        # Query, replacement, case option, previous, next, current, all, close.
        for _ in range(5):
            ui.key("Tab")
        ui.key("space")
        ui.record("find-replace-panel")
        ui.key("Escape")
        ui.picker("ctrl+s", "Save text file")
        searched = files / "replaced.txt"
        ui.choose(searched, saving=True)
        ui.bytes(searched, b"b$1na b$1na")
        ui.editor(searched)
        ui.key("ctrl+z")
        ui.editor(searched, dirty=True)
        ui.key("ctrl+s")
        ui.bytes(searched, b"banana banana")
        ui.editor(searched)
        ui.key("ctrl+f")
        ui.type("absent")
        ui.key("Return")
        ui.key("Escape")
        ui.key("ctrl+s")
        ui.bytes(searched, b"banana banana")
        ui.record("find-replace-literal-grouped-undo-and-cancel")

        wrapped = files / "wrapped.md"
        long_text = ("word λ😀 " * 100 + "\r\nsecond\nthird\rfourth").encode("utf-8")
        wrapped.write_bytes(long_text)
        ui.picker("ctrl+o", "Open text file")
        ui.choose(wrapped, saving=False)
        window = ui.editor(wrapped)
        ui.record("font-size-default")
        for _ in range(5):
            ui.key("ctrl+plus")
        ui.editor(wrapped)
        time.sleep(0.5)
        ui.record("font-size-increased")
        ui.key("ctrl+minus")
        ui.editor(wrapped)
        time.sleep(0.5)
        ui.record("font-size-decreased")
        ui.key("ctrl+0")
        ui.key("ctrl+s")
        ui.bytes(wrapped, long_text)
        ui.editor(wrapped)
        time.sleep(0.5)
        ui.record("font-size-reset-preserves-bytes")
        for _ in range(5):
            ui.key("ctrl+plus")
        # Word Wrap follows Replace in the existing toolbar.
        ui.click(window, 400, 25)
        ui.editor(wrapped)
        ui.record("word-wrap-enabled-long-unicode-line")
        ui.click(window, 120, 150)
        ui.key("ctrl+End")
        ui.type("!")
        ui.editor(wrapped, dirty=True)
        ui.click(window, 400, 25)
        ui.record("word-wrap-disabled-after-edit")
        ui.click(window, 120, 150)
        ui.key("ctrl+z")
        ui.editor(wrapped)
        ui.key("ctrl+s")
        ui.bytes(wrapped, long_text)
        ui.key("ctrl+y")
        ui.editor(wrapped, dirty=True)
        ui.key("ctrl+s")
        ui.bytes(wrapped, long_text + b"!")
        ui.editor(wrapped)
        ui.click(window, 400, 25)
        ui.key("ctrl+n")
        ui.editor()
        ui.picker("ctrl+o", "Open text file")
        ui.choose(wrapped, saving=False)
        window = ui.editor(wrapped)
        ui.record("word-wrap-retained-after-new-and-open")
        ui.record("font-size-retained-after-new-and-open")
        # Navigate while wrapping is enabled. Saved bytes prove the insertion offset.
        ui.click(window, 120, 150)
        for line, prefix in [("2", long_text.split(b"second")[0]),
                             ("3", long_text.split(b"third")[0]),
                             ("999", long_text.split(b"fourth")[0])]:
            ui.picker("ctrl+g", "Go To Line")
            ui.type(line)
            ui.key("Return")
            ui.gone("Go To Line")
            ui.editor(wrapped)
            ui.type("G")
            ui.key("ctrl+s")
            expected = long_text[:len(prefix)] + b"G" + long_text[len(prefix):] + b"!"
            ui.bytes(wrapped, expected)
            ui.key("ctrl+z")
            ui.key("ctrl+s")
            ui.bytes(wrapped, long_text + b"!")
        ui.record("go-to-line-logical-lines-clamp-and-undo")
        ui.key("ctrl+Home")
        ui.key("shift+Right")
        ui.picker("ctrl+g", "Go To Line")
        ui.type("0")
        ui.key("Return")
        ui.window("Go To Line")
        ui.record("go-to-line-invalid-entry")
        ui.key("Escape")
        ui.gone("Go To Line")
        ui.editor(wrapped)
        ui.type("G")
        ui.key("ctrl+s")
        ui.bytes(wrapped, b"G" + long_text[1:] + b"!")
        ui.key("ctrl+z")
        ui.key("ctrl+s")
        ui.bytes(wrapped, long_text + b"!")
        ui.record("go-to-line-cancel-preserves-selection-and-bytes")
        ui.click(window, 400, 25)
        ui.click(window, 120, 150)
        ui.key("ctrl+0")
        ui.key("ctrl+n")
        ui.editor()
        ui.type("retain me")

        window = ui.editor(dirty=True)
        ui.close(window)
        ui.decision("Cancel")
        ui.editor(dirty=True)
        ui.close(window)
        ui.decision("Save")
        picker = ui.window("Save text file")
        ui.focus(picker)
        ui.key("Escape")
        ui.gone("Save text file")
        ui.editor(dirty=True)
        ui.close(window)
        ui.decision("Save")
        ui.focus(ui.window("Save text file"))
        final = files / "close.txt"
        ui.choose(final, saving=True)
        wait("exit after Save", lambda: app.poll() is not None)
        ui.bytes(final, b"retain me")
        if app.returncode != 0:
            raise RuntimeError(f"Editor exited with code {app.returncode}.")
        ui.record("close-cancel-cancelled-picker-save")
    finally:
        blocked.chmod(0o700)


def main():
    if sys.platform != "linux" or os.geteuid() == 0 or ctypes.sizeof(ctypes.c_long) != 8:
        raise RuntimeError("NOT TESTED: Run as a non-root user on 64-bit Linux with X11 test dependencies.")
    for command in ("Xvfb", "xdotool", "dbus-daemon"):
        if not shutil.which(command):
            raise RuntimeError(f"NOT TESTED: Install {command}, then repeat this command.")
    for path in ("/usr/libexec/xdg-desktop-portal", "/usr/libexec/xdg-desktop-portal-gtk"):
        if not Path(path).is_file():
            raise RuntimeError(f"NOT TESTED: Install {Path(path).name}, then repeat this command.")
    executable = ROOT / "src/Editor.App/bin/Release/net10.0/Editor.App"
    if not executable.is_file():
        raise RuntimeError("Build Editor.App in Release, then repeat this command.")
    evidence = Path(tempfile.mkdtemp(prefix="native-editor-", dir=ROOT / "artifacts"))
    with contextlib.ExitStack() as stack:
        scratch = Path(stack.enter_context(tempfile.TemporaryDirectory(prefix="session-", dir=evidence)))
        env = os.environ.copy()
        env.pop("WAYLAND_DISPLAY", None)
        env.update(LC_ALL="C.UTF-8", GDK_BACKEND="x11", XDG_CURRENT_DESKTOP="GNOME", XDG_CONFIG_HOME=str(scratch / "config"),
                   XDG_CACHE_HOME=str(scratch / "cache"), XDG_DATA_HOME=str(scratch / "data"), XDG_RUNTIME_DIR=str(scratch / "runtime"))
        Path(env["XDG_RUNTIME_DIR"]).mkdir(mode=0o700)
        config = Path(env["XDG_CONFIG_HOME"]) / "xdg-desktop-portal"
        config.mkdir(parents=True)
        (config / "portals.conf").write_text("[preferred]\ndefault=gtk\n")

        def launch(command, name, **kwargs):
            log = stack.enter_context((evidence / f"{name}.log").open("w"))
            process = subprocess.Popen(command, env=env, stderr=log, stdout=kwargs.pop("stdout", log), **kwargs)
            def stop():
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()
            stack.callback(stop)
            return process

        # The server allocates a free display. No existing desktop receives input.
        read_fd, write_fd = os.pipe()
        with os.fdopen(read_fd) as display_number:
            launch(["Xvfb", "-displayfd", str(write_fd), "-screen", "0", "1280x900x24", "-nolisten", "tcp"],
                   "xvfb", pass_fds=(write_fd,))
            os.close(write_fd)
            env["DISPLAY"] = ":" + display_number.readline().strip()
        bus = launch(["dbus-daemon", "--session", "--nofork", "--print-address"], "bus", stdout=subprocess.PIPE, text=True)
        env["DBUS_SESSION_BUS_ADDRESS"] = bus.stdout.readline().strip()
        launch(["/usr/libexec/xdg-desktop-portal-gtk"], "gtk")
        launch(["/usr/libexec/xdg-desktop-portal"], "portal")
        time.sleep(2)
        app = launch([str(executable)], "editor")
        ui = Desktop(env, evidence)
        result = "FAIL"
        try:
            files = scratch / "files"
            files.mkdir()
            exercise(ui, files, app)
            # A separate dirty document checks the Discard close branch.
            app = launch([str(executable)], "editor-discard")
            window = ui.editor()
            ui.type("discard on close")
            ui.close(window)
            ui.decision("Discard")
            wait("exit after Discard", lambda: app.poll() is not None)
            if app.returncode != 0:
                raise RuntimeError(f"Editor exited with code {app.returncode}.")
            ui.record("close-discard")
            result = "PASS"
        except Exception:
            ImageGrab.grab(xdisplay=env["DISPLAY"]).save(evidence / "failure.png")
            raise
        finally:
            report = {
                "result": result,
                "host": platform.freedesktop_os_release()["PRETTY_NAME"],
                "architecture": platform.machine(),
                "session": f"Xvfb {env['DISPLAY']}, GTK portal, private D-Bus",
                "steps": ui.steps,
            }
            (evidence / "result.json").write_text(json.dumps(report, indent=2) + "\n")
            print(f"Evidence: {evidence}", flush=True)


if __name__ == "__main__":
    main()
