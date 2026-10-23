#!/usr/bin/env python3
"""Exercise the Release editor through X11 input and real GTK portal dialogs."""

import contextlib
import ctypes
import hashlib
import struct
import json
import os
import platform
import select
import socket
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

from PIL import ImageGrab

ROOT = Path(__file__).resolve().parents[1]


def read_state(path):
    if not path.exists():
        return None
    data = path.read_bytes()
    if data[:8] != b"TANGOST\0" or hashlib.sha256(data[:-32]).digest() != data[-32:]:
        raise RuntimeError("The binary state header or checksum is invalid.")
    version, wrap, numbers, font, count = struct.unpack_from("<IBBdI", data, 8)
    if version != 1 or count > 10:
        raise RuntimeError("The binary state version or path count is invalid.")
    paths = []
    offset = 26
    for _ in range(count):
        length, = struct.unpack_from("<I", data, offset)
        offset += 4
        paths.append(data[offset:offset + length].decode("utf-8"))
        offset += length
    if offset != len(data) - 32:
        raise RuntimeError("The binary state length is invalid.")
    return {"wordWrap": bool(wrap), "showLineNumbers": bool(numbers), "fontSize": font, "paths": paths}


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

    def conflict(self, choice):
        window = self.window("File changed on disk")
        geometry = dict(line.split("=", 1) for line in self.xdo("getwindowgeometry", "--shell", window).splitlines())
        width, height = int(geometry["WIDTH"]), int(geometry["HEIGHT"])
        self.click(window, width - {"Cancel": 50, "Overwrite": 155, "Reload": 300}[choice], height - 38)
        self.gone("File changed on disk")

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


def exercise_drops(ui, files, launch):
    original = files / "drop λ space.MD"
    original.write_bytes(b"# Dropped\r\ntext")
    other = files / "drop-next.txt"
    other.write_bytes(b"next document")
    invalid = files / "drop-invalid.txt"
    invalid.write_bytes(b"\xff\xfe")

    def drop(window, payload, mime="text/uri-list"):
        source = launch([sys.executable, str(ROOT / "eng/native-drag-source.py"), mime, payload], "drag-source")
        try:
            drag = ui.window("Tangopad drag source")
            ui.xdo("windowmove", window, 0, 0)
            ui.xdo("windowmove", drag, 1030, 700)
            ui.focus(drag)
            ui.xdo("mousemove", "--window", drag, 100, 40, "mousedown", 1)
            for x, y in ((1120, 740), (1000, 500), (800, 200), (100, 120)):
                ui.xdo("mousemove", x, y)
                time.sleep(0.2)
            if not ui.steps:
                ui.record("drop-affordance")
            ui.xdo("mouseup", 1)
            time.sleep(0.5)
        finally:
            source.terminate()
            source.wait(timeout=5)

    def uri(path):
        return path.as_uri() + "\r\n"

    drop(ui.editor(), uri(original))
    window = ui.editor(original)
    ui.record("drop-local-unicode-markdown")
    ui.key("ctrl+End")
    ui.type("!")
    drop(ui.editor(original, dirty=True), uri(other))
    ui.decision("Cancel")
    ui.editor(original, dirty=True)
    ui.key("ctrl+z")
    ui.editor(original)
    ui.record("drop-cancel-preserves-undo")

    for name, payload, mime in (
        ("multiple", uri(original) + uri(other), "text/uri-list"),
        ("folder", uri(files), "text/uri-list"),
        ("remote-uri", "https://example.com/a.md\r\n", "text/uri-list"),
        ("url-text", "https://example.com/a.md", "text/plain"),
    ):
        drop(window, payload, mime)
        ui.editor(original)
        ui.record("drop-reject-" + name)

    drop(window, uri(invalid))
    ui.focus(ui.window("File operation failed"))
    ui.key("Escape")
    ui.gone("File operation failed")
    ui.editor(original)
    ui.record("drop-invalid-utf8")
    ui.key("ctrl+End")
    ui.type("!")
    drop(ui.editor(original, dirty=True), uri(other))
    ui.decision("Save")
    ui.bytes(original, b"# Dropped\r\ntext!")
    ui.editor(other)
    ui.record("drop-save-and-format-transition")
    ui.type("!")
    drop(ui.editor(other, dirty=True), uri(original))
    ui.decision("Discard")
    ui.editor(original)
    ui.bytes(other, b"next document")
    ui.record("drop-discard")
    ui.key("ctrl+n")
    ui.editor()


def exercise_recent(ui, files):
    fixture = files / "recent.md"
    fixture.write_bytes(b"# Recent file\r\ntext")
    settings = Path(ui.env["XDG_DATA_HOME"]) / "Tangopad" / "state.bin"

    def paths():
        return (read_state(settings) or {"paths": []})["paths"]

    ui.picker("ctrl+o", "Open text file")
    ui.choose(fixture, saving=False)
    ui.editor(fixture)
    wait("recent path persisted in the fresh profile", lambda: paths() and paths()[0] == str(fixture))
    ui.key("ctrl+n")
    window = ui.editor()
    ui.click(window, 185, 25)
    # Flyout focus starts on the first recent entry.
    ui.key("Return")
    window = ui.editor(fixture)
    ui.record("recent-file-reopen")
    ui.key("ctrl+End")
    ui.type("!")
    window = ui.editor(fixture, dirty=True)
    ui.click(window, 185, 25)
    # Move from the first entry to Clear through the popup's focus cycle.
    ui.key("shift+Tab")
    ui.key("Return")
    wait("cleared recent paths persisted", lambda: paths() == [])
    ui.key("Escape")
    ui.editor(fixture, dirty=True)
    ui.click(window, 120, 150)
    ui.key("ctrl+z")
    ui.editor(fixture)
    ui.bytes(fixture, b"# Recent file\r\ntext")
    ui.record("recent-clear-preserves-document-and-undo")
    ui.key("ctrl+n")
    ui.editor()


def exercise_line_numbers(ui, files):
    fixture = files / "line-numbers.txt"
    original = b"first\r\nsecond\nthird\rlast\n"
    fixture.write_bytes(original)
    ui.picker("ctrl+o", "Open text file")
    ui.choose(fixture, saving=False)
    window = ui.editor(fixture)
    time.sleep(0.3)

    def gutter():
        # The second line has no caret or selection during these checks.
        return ImageGrab.grab(xdisplay=ui.env["DISPLAY"]).crop((0, 76, 20, 88)).tobytes()

    numbered = gutter()
    # Tab from Word Wrap selects Line numbers in both host font layouts.
    ui.click(window, 700, 25)
    ui.key("space")
    ui.key("Tab")
    ui.key("space")
    wait("hidden line-number gutter", lambda: gutter() != numbered)
    hidden = gutter()
    ui.record("line-numbers-hidden")
    ui.key("space")
    wait("restored line-number gutter", lambda: gutter() == numbered)
    ui.record("line-numbers-restored-with-keyboard")
    ui.key("space")
    wait("hidden line-number gutter before navigation", lambda: gutter() == hidden)
    ui.click(window, 120, 150)
    ui.picker("ctrl+g", "Go To Line")
    ui.type("3")
    ui.key("Return")
    ui.gone("Go To Line")
    ui.editor(fixture)
    ui.type("!")
    ui.key("ctrl+s")
    ui.bytes(fixture, b"first\r\nsecond\n!third\rlast\n")
    wait("hidden gutter after navigation", lambda: gutter() == hidden)
    ui.record("line-numbers-hidden-logical-navigation-and-exact-save")
    ui.key("ctrl+z")
    ui.key("ctrl+s")
    ui.bytes(fixture, original)
    # Saved bytes can appear before Save enables the document commands again.
    ui.editor(fixture)


def exercise_reload(ui, files):
    # Xvfb maps F5 to keycode 71. Older xdotool versions add Alt for the F5 keysym.
    fixture = files / "reload.md"
    original = b"# Original\r\n"
    external = "# External 😀\r\nλ\nlast\r".encode()
    fixture.write_bytes(original)
    ui.picker("ctrl+o", "Open text file")
    ui.choose(fixture, saving=False)
    ui.editor(fixture)
    fixture.write_bytes(external)
    ui.key("71")
    time.sleep(0.5)
    ui.key("ctrl+End")
    ui.type("!")
    ui.editor(fixture, dirty=True)
    ui.key("ctrl+s")
    ui.editor(fixture)
    ui.bytes(fixture, external + b"!")
    ui.record("reload-external-unicode-and-mixed-newlines")

    ui.type("?")
    ui.editor(fixture, dirty=True)
    ui.key("71")
    ui.decision("Cancel")
    ui.editor(fixture, dirty=True)
    ui.key("ctrl+s")
    ui.editor(fixture)
    ui.bytes(fixture, external + b"!?")
    ui.key("ctrl+z")
    ui.editor(fixture, dirty=True)
    ui.key("ctrl+s")
    ui.editor(fixture)
    ui.bytes(fixture, external + b"!")
    ui.record("reload-cancel-preserves-text-and-undo")

    ui.type("x")
    ui.editor(fixture, dirty=True)
    fixture.write_bytes(original)
    ui.key("71")
    ui.decision("Discard")
    ui.editor(fixture)
    ui.key("ctrl+End")
    ui.type("v")
    ui.editor(fixture, dirty=True)
    ui.key("ctrl+s")
    ui.editor(fixture)
    ui.bytes(fixture, original + b"v")
    ui.key("ctrl+z")
    ui.editor(fixture, dirty=True)
    ui.key("ctrl+s")
    ui.editor(fixture)
    ui.bytes(fixture, original)
    ui.record("reload-discard-resets-undo")
    ui.key("ctrl+n")
    ui.editor()


def exercise_external_notification(ui, files):
    fixture = files / "notification.txt"
    fixture.write_bytes(b"original")
    ui.picker("ctrl+o", "Open text file")
    ui.choose(fixture, saving=False)
    window = ui.editor(fixture)
    geometry = dict(line.split("=", 1) for line in ui.xdo("getwindowgeometry", "--shell", window).splitlines())
    x, y = int(geometry["X"]), int(geometry["Y"])

    def banner_pixels():
        return ImageGrab.grab(xdisplay=ui.env["DISPLAY"]).crop((x, y + 45, x + 500, y + 90))

    time.sleep(0.3)
    before = banner_pixels()
    external = "external 😀\r\nλ\n".encode()
    fixture.write_bytes(external)

    def message_visible():
        after = banner_pixels()
        changed = sum(a != b for a, b in zip(before.tobytes(), after.tobytes()))
        return changed > 1000

    wait("external file notification", message_visible)
    ui.editor(fixture)
    ui.record("external-change-nonblocking-banner")
    # The notification follows the first toolbar row at the default window size.
    ui.click(window, 260, 65)
    time.sleep(0.3)
    ui.click(window, 120, 150)
    ui.key("ctrl+End")
    ui.type("!")
    ui.editor(fixture, dirty=True)
    ui.key("ctrl+s")
    ui.editor(fixture)
    ui.bytes(fixture, external + b"!")
    ui.record("external-change-banner-reload")
    ui.key("ctrl+n")
    ui.editor()


def exercise_conflicts(ui, files):
    fixture = files / "conflict.txt"
    original = b"original"
    external = b"external"
    fixture.write_bytes(original)
    ui.picker("ctrl+o", "Open text file")
    ui.choose(fixture, saving=False)
    ui.editor(fixture)
    ui.key("ctrl+End")
    ui.type("!")
    ui.editor(fixture, dirty=True)
    timestamp = fixture.stat()
    fixture.write_bytes(external)
    os.utime(fixture, ns=(timestamp.st_atime_ns, timestamp.st_mtime_ns))
    ui.key("ctrl+s")
    ui.window("File changed on disk")
    ui.record("conflict-dialog")
    ui.conflict("Cancel")
    ui.editor(fixture, dirty=True)
    ui.bytes(fixture, external)
    ui.key("ctrl+z")
    ui.editor(fixture)
    ui.key("ctrl+y")
    ui.editor(fixture, dirty=True)
    ui.record("conflict-cancel-keeps-text-and-undo")
    ui.key("ctrl+s")
    ui.conflict("Overwrite")
    ui.editor(fixture)
    ui.bytes(fixture, original + b"!")
    ui.record("conflict-deliberate-overwrite")
    ui.type("?")
    ui.editor(fixture, dirty=True)
    fixture.write_bytes(external)
    ui.key("ctrl+s")
    ui.conflict("Reload")
    ui.decision("Discard")
    ui.editor(fixture)
    ui.key("ctrl+End")
    ui.type("+")
    ui.key("ctrl+s")
    ui.editor(fixture)
    ui.bytes(fixture, external + b"+")
    ui.record("conflict-reload-uses-unsaved-confirmation")
    fixture.unlink()
    ui.type("x")
    ui.editor(fixture, dirty=True)
    ui.key("ctrl+s")
    ui.conflict("Cancel")
    if fixture.exists():
        raise RuntimeError("Cancel recreated a missing file.")
    ui.key("ctrl+s")
    ui.conflict("Overwrite")
    ui.editor(fixture)
    ui.bytes(fixture, external + b"+x")
    ui.record("conflict-missing-file-needs-overwrite")
    ui.key("ctrl+n")
    ui.editor()


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
        ui.conflict("Overwrite")
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

        with socket.socket() as server:
            server.bind(("127.0.0.1", 0))
            server.listen()
            asset = ui.evidence / "preview-local.svg"
            asset.write_text('<svg xmlns="http://www.w3.org/2000/svg"><text>local secret</text></svg>')
            markdown = files / "highlighting.md"
            markdown_text = b"# Heading\r\n**strong** and *emphasis* and `code`\n[link](https://example.com)\n> quote\n- item\n1. item\n```\n# fenced code\n```\nordinary text"
            markdown_text += f"\n\n![remote](http://127.0.0.1:{server.getsockname()[1]}/image) ![local]({asset.as_uri()})\n\n<script>alert(1)</script>".encode()
            markdown.write_bytes(markdown_text)
            ui.picker("ctrl+o", "Open text file")
            ui.choose(markdown, saving=False)
            window = ui.editor(markdown)
            ui.record("markdown-highlighting")
            libc = ctypes.CDLL(None, use_errno=True)
            watch = libc.inotify_init1(os.O_NONBLOCK | os.O_CLOEXEC)
            if watch < 0:
                raise OSError(ctypes.get_errno(), "Cannot create the preview file-access monitor.")
            try:
                # IN_ACCESS and IN_OPEN detect reads of the referenced local asset.
                if libc.inotify_add_watch(watch, os.fsencode(asset), 0x21) < 0:
                    raise OSError(ctypes.get_errno(), "Cannot monitor the preview asset.")
                ui.click(window, 794, 25)
                time.sleep(0.5)
                if select.select([server, watch], [], [], 0)[0]:
                    raise RuntimeError("Preview accessed a network or local-file resource.")
                ui.record("markdown-preview-shown-without-resource-access")
            finally:
                os.close(watch)
        ui.click(window, 120, 100)
        ui.key("ctrl+End")
        addition = "!"
        ui.type(addition)
        ui.key("ctrl+s")
        ui.bytes(markdown, markdown_text + addition.encode())
        time.sleep(0.5)
        ui.record("markdown-preview-live-edit-and-save")
        ui.key("ctrl+f")
        ui.type("Heading")
        ui.key("Escape")
        ui.key("ctrl+plus")
        ui.key("ctrl+0")
        ui.key("ctrl+z")
        ui.key("ctrl+s")
        ui.bytes(markdown, markdown_text)
        ui.click(window, 794, 25)
        ui.record("markdown-preview-hidden-with-undo-and-find")
        ui.click(window, 794, 25)
        time.sleep(0.5)
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
        for extension, source in (
            ("JSON", '{\r\n  "text": "λ café 🐈", "escaped": "a\\\"b",\n  "values": [true, null, -1.25e+2]\r\n}'),
            ("XML", '<?xml version="1.0"?>\r\n<Root title="a &amp; b">\n<!-- comment -->\n<![CDATA[<raw>]]>λ café 🐈</Root>'),
            ("XAML", '<Panel xmlns="urn:test" Title="λ café 🐈">\r\n  <Text Text="{Binding Name}" />\n</Panel>'),
            ("YAML", 'title: "λ café 🐈"\r\nvalues: [true, null, -1.25e+2]\ncopy: &base {enabled: false} # comment\n'),
            ("YML", "title: 'it''s YAML'\r\ncopy: *base\n"),
            ("CSV", 'name,value,empty\r\n"λ café 🐈","a""b,c\ncontinued",001\r\n=SUM(A1),true,\n'),
            ("TSV", 'name\tvalue\tempty\r\n"λ café 🐈"\t"a""b\tc\ncontinued"\t001\r\n=SUM(A1)\ttrue\t\n'),
        ):
            structured = files / f"structured.{extension}"
            structured_bytes = source.encode("utf-8")
            structured.write_bytes(structured_bytes)
            ui.picker("ctrl+o", "Open text file")
            ui.choose(structured, saving=False)
            ui.editor(structured)
            ui.record(f"{extension.lower()}-source-highlighting")
            ui.key("ctrl+End")
            ui.type("!")
            ui.key("ctrl+z")
            ui.key("ctrl+s")
            ui.bytes(structured, structured_bytes)
            destination = files / f"structured-{extension.lower()}.txt"
            ui.picker("ctrl+shift+s", "Save text file")
            ui.choose(destination, saving=True)
            ui.editor(destination)
            ui.bytes(destination, structured_bytes)
            ui.record(f"{extension.lower()}-to-plain-preserves-bytes-and-undo")
        with socket.socket() as server:
            server.bind(("127.0.0.1", 0))
            server.listen()
            asset = ui.evidence / "textile-preview-local.svg"
            asset.write_text('<svg xmlns="http://www.w3.org/2000/svg"><text>local secret</text></svg>')
            textile = files / "page.TEXTILE"
            textile_text = "\n".join([
                "h1. Textile preview",
                "",
                "{{toc}}",
                "",
                "*strong* and _emphasis_ and +underline+ and -strike- and @code@",
                "",
                "|_. Name|_. Value|",
                "|first|λ café 😀|",
                "|[[sandbox:Page|Wiki cell]]|{{include(Page|{{plugin(x)}})}}|",
                "",
                "bq. quote",
                "",
                "* item",
                "# ordered",
                "",
                "----",
                "",
                "p. paragraph",
                "",
                f'"remote":http://127.0.0.1:{server.getsockname()[1]}/link',
                f'!http://127.0.0.1:{server.getsockname()[1]}/image(remote image)!',
                f"!{asset.as_uri()}(local image)!",
                "",
                "[[sandbox:WikiPage|Wiki caption]] #123 ##123 #123-6 #123#note-6",
                'attachment:"file name.zip"',
                f"{{{{include(http://127.0.0.1:{server.getsockname()[1]}/include)}}}}",
                f"{{{{thumbnail({asset.as_uri()})}}}}",
                "{{child_pages}} {{custom_plugin(remote)}}",
                "{{collapse(Details)",
                "h2. Hidden heading",
                "{{include(Remote)}}",
                "}}",
                "",
                "<script>alert(1)</script>",
            ]).encode()
            textile.write_bytes(textile_text)
            ui.picker("ctrl+o", "Open text file")
            ui.choose(textile, saving=False)
            window = ui.editor(textile)
            ui.record("textile-highlighting")
            watch = libc.inotify_init1(os.O_NONBLOCK | os.O_CLOEXEC)
            if watch < 0:
                raise OSError(ctypes.get_errno(), "Cannot create the Textile preview file-access monitor.")
            try:
                if libc.inotify_add_watch(watch, os.fsencode(asset), 0x21) < 0:
                    raise OSError(ctypes.get_errno(), "Cannot monitor the Textile preview asset.")
                ui.click(window, 794, 25)
                time.sleep(0.5)
                if select.select([server, watch], [], [], 0)[0]:
                    raise RuntimeError("Textile preview accessed a network or local-file resource.")
                ui.record("textile-preview-shown-without-resource-access")
                ui.record("redmine-references-and-local-contents-without-resource-access")
                ui.click(window, 120, 100)
                ui.key("ctrl+End")
                ui.type("!")
                ui.key("ctrl+s")
                ui.bytes(textile, textile_text + b"!")
                time.sleep(0.5)
                ui.record("textile-preview-live-edit-and-save")
                ui.key("ctrl+z")
                ui.key("ctrl+s")
                ui.bytes(textile, textile_text)
                ui.click(window, 794, 25)
                ui.record("textile-preview-hidden-with-undo")
                ui.click(window, 794, 25)
                time.sleep(0.5)
                if select.select([server, watch], [], [], 0)[0]:
                    raise RuntimeError("Textile preview accessed a resource after an edit or toggle.")
                textile_plain = files / "page.txt"
                ui.picker("ctrl+shift+s", "Save text file")
                ui.choose(textile_plain, saving=True)
                ui.editor(textile_plain)
                ui.bytes(textile_plain, textile_text)
                ui.record("textile-to-plain-text")
                ui.key("ctrl+End")
                ui.type("!")
                ui.key("ctrl+z")
                ui.key("ctrl+s")
                ui.bytes(textile_plain, textile_text)
            finally:
                os.close(watch)
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
        # Query, replacement, case, whole word, regex, previous, next, current, all, close.
        for _ in range(7):
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
        ui.click(window, 700, 25)
        ui.editor(wrapped)
        ui.record("word-wrap-enabled-long-unicode-line")
        ui.click(window, 120, 150)
        ui.key("ctrl+End")
        ui.type("!")
        ui.editor(wrapped, dirty=True)
        ui.click(window, 700, 25)
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
        ui.click(window, 700, 25)
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
        ui.click(window, 700, 25)
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


def exercise_preferences(ui, files, launch, executable, settings=None, arguments=()):
    mode = "portable" if settings else "installed"
    settings = settings or Path(ui.env["XDG_DATA_HOME"]) / "Tangopad" / "state.bin"
    settings.unlink(missing_ok=True)
    fixture = files / "preferences.txt"
    original = ("first\n" + "wrapped presentation " * 80 + "\nlast\n").encode()
    fixture.write_bytes(original)

    def open_fixture():
        ui.editor()
        ui.picker("ctrl+o", "Open text file")
        ui.choose(fixture, saving=False)
        return ui.editor(fixture)

    def pixels():
        shot = ImageGrab.grab(xdisplay=ui.env["DISPLAY"])
        return (shot.crop((680, 10, 940, 40)).tobytes(), shot.crop((0, 100, 800, 250)).tobytes())

    app = launch([str(executable), *arguments], f"preferences-{mode}-first", cwd=files)
    window = open_fixture()
    time.sleep(0.5)
    defaults = pixels()
    ui.click(window, 700, 25)
    ui.key("Tab")
    ui.key("space")
    ui.click(window, 200, 80)
    ui.key("ctrl+plus")
    ui.key("ctrl+plus")
    ui.key("ctrl+plus")
    wait("persisted presentation changes", lambda: settings.exists() and all(read_state(settings)[key] == value for key, value in
         {"wordWrap": True, "showLineNumbers": False, "fontSize": 18}.items()))
    # Keep the caret outside the compared text region in both processes.
    ui.key("ctrl+Home")
    time.sleep(0.5)
    changed = pixels()
    if changed[0] == defaults[0] or changed[1] == defaults[1]:
        raise RuntimeError("Preference input did not change the native controls.")
    ui.record(f"preferences-{mode}-before-relaunch")
    ui.close(window)
    wait("first preference process exit", lambda: app.poll() is not None)
    if app.returncode != 0:
        raise RuntimeError(f"Editor exited with code {app.returncode}.")
    # Simulate updater replacement of the application executable while state stays in place.
    before = settings.read_bytes()
    replacement = executable.with_name("Editor.App.update")
    shutil.copy2(executable, replacement)
    os.replace(replacement, executable)
    if settings.read_bytes() != before:
        raise RuntimeError("The simulated update changed persistent state.")
    app = launch([str(executable), *arguments], f"preferences-{mode}-restored", cwd=files)
    window = ui.editor()
    ui.click(window, 185, 25)
    ui.key("Return")
    window = ui.editor(fixture)
    ui.key("ctrl+Home")
    wait("restored native preference controls", lambda: pixels() == changed)
    ui.bytes(fixture, original)
    ui.record(f"preferences-{mode}-restored-after-process-relaunch")
    ui.close(window)
    wait("restored preference process exit", lambda: app.poll() is not None)
    if app.returncode != 0:
        raise RuntimeError(f"Editor exited with code {app.returncode}.")


def exercise_whole_word(ui, files):
    path = files / "whole-word.md"
    original = "# cat_cat Cat cat\r\ncat’s 😀cat😀 e\u0301 cat𐐀\n"
    path.write_bytes(original.encode("utf-8"))
    ui.picker("ctrl+o", "Open text file")
    ui.choose(path, saving=False)
    ui.editor(path)
    ui.key("ctrl+h")
    ui.type("cat")
    ui.key("Tab")
    ui.key("ctrl+a")
    ui.type("dog")
    ui.key("Tab")
    ui.key("space")  # Match case.
    ui.key("Tab")
    ui.key("space")  # Match whole word.
    time.sleep(0.2)
    ui.record("whole-word-options")
    # Regex is before navigation. Replace current is disabled until a match is selected.
    for _ in range(4):
        ui.key("Tab")
    ui.key("space")  # Replace all.
    ui.key("Escape")
    ui.key("ctrl+s")
    ui.bytes(path, "# cat_cat Cat dog\r\ndog’s 😀dog😀 e\u0301 cat𐐀\n".encode("utf-8"))
    ui.editor(path)
    ui.key("ctrl+z")
    ui.key("ctrl+s")
    ui.bytes(path, original.encode("utf-8"))
    ui.editor(path)
    ui.key("ctrl+f")
    ui.type("e")
    ui.key("Return")
    ui.key("shift+Return")
    ui.record("whole-word-combining-mark-no-match")
    ui.key("Escape")
    ui.key("ctrl+s")
    ui.bytes(path, original.encode("utf-8"))
    ui.record("whole-word-unicode-replace-and-grouped-undo")


def exercise_regex(ui, files):
    path = files / "regex.md"
    original = "# cat12 cat_cat3\r\nCAT4 😀cat5😀\n"
    path.write_bytes(original.encode("utf-8"))
    ui.picker("ctrl+o", "Open text file")
    ui.choose(path, saving=False)
    ui.editor(path)
    ui.key("ctrl+h")
    ui.type(r"cat(\d+)")
    ui.key("Tab")
    ui.type("dog$1!")
    ui.key("Tab")  # Match case stays off.
    ui.key("Tab")
    ui.key("space")  # Match whole word excludes cat_cat3.
    ui.key("Tab")
    ui.key("space")  # Use regular expressions.
    time.sleep(0.5)
    ui.record("regex-captures-ready")
    # Previous, next, and Replace all. Replace current has no selected match.
    for _ in range(3):
        ui.key("Tab")
    ui.key("space")
    time.sleep(0.5)
    ui.key("Escape")
    ui.key("ctrl+s")
    expected = "# dog12! cat_cat3\r\ndog4! 😀dog5!😀\n".encode("utf-8")
    ui.bytes(path, expected)
    ui.editor(path)
    ui.key("ctrl+z")
    ui.key("ctrl+s")
    ui.bytes(path, original.encode("utf-8"))
    ui.key("ctrl+y")
    ui.key("ctrl+s")
    ui.bytes(path, expected)
    ui.record("regex-capture-replacement-grouped-undo-redo-exact-bytes")
    ui.key("ctrl+f")
    ui.type("(")
    time.sleep(0.5)
    ui.record("regex-invalid-pattern-feedback")
    ui.key("Escape")
    ui.key("ctrl+s")
    ui.bytes(path, expected)
    ui.key("ctrl+h")
    ui.type("(?=dog)")
    time.sleep(0.5)
    ui.record("regex-empty-match-feedback")
    ui.key("Escape")
    ui.key("ctrl+s")
    ui.bytes(path, expected)


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
            exercise_drops(ui, files, launch)
            exercise_recent(ui, files)
            exercise_line_numbers(ui, files)
            exercise_reload(ui, files)
            exercise_external_notification(ui, files)
            exercise_conflicts(ui, files)
            exercise(ui, files, app)
            whole_word_app = launch([str(executable)], "editor-whole-word")
            ui.editor()
            exercise_whole_word(ui, files)
            ui.close(ui.editor(files / "whole-word.md"))
            wait("exit after whole-word checks", lambda: whole_word_app.poll() is not None)
            if whole_word_app.returncode != 0:
                raise RuntimeError(f"Editor exited with code {whole_word_app.returncode}.")
            regex_app = launch([str(executable)], "editor-regex")
            ui.editor()
            exercise_regex(ui, files)
            ui.close(ui.editor(files / "regex.md"))
            wait("exit after regex checks", lambda: regex_app.poll() is not None)
            if regex_app.returncode != 0:
                raise RuntimeError(f"Editor exited with code {regex_app.returncode}.")
            installed = scratch / "installed"
            shutil.copytree(executable.parent, installed)
            exercise_preferences(ui, files, launch, installed / executable.name)
            installed_state = Path(env["XDG_DATA_HOME"]) / "Tangopad" / "state.bin"
            installed_before = installed_state.read_bytes()
            portable = scratch / "portable"
            shutil.copytree(executable.parent, portable)
            (portable / "tangopad.portable").touch()
            exercise_preferences(ui, files, launch, portable / executable.name, portable / "data" / "state.bin")
            if installed_state.read_bytes() != installed_before:
                raise RuntimeError("Portable mode changed installed state.")
            # A separate dirty document checks the Discard close branch.
            app = launch([str(executable)], "editor-discard")
            window = ui.editor()
            ui.click(window, 120, 100)
            ui.type("discard on close")
            window = ui.editor(dirty=True)
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
