#!/usr/bin/env python3
"""Audit Linux AT-SPI controls and native display scaling in an isolated session."""

import argparse
import contextlib
import ctypes
import json
import os
from pathlib import Path
import platform
import runpy
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

from PIL import ImageGrab

ROOT = Path(__file__).resolve().parents[1]


class AtSpi:
    """Read the native accessibility tree through the public libatspi C API."""

    class Error(ctypes.Structure):
        _fields_ = [("domain", ctypes.c_uint), ("code", ctypes.c_int), ("message", ctypes.c_char_p)]

    class Rectangle(ctypes.Structure):
        _fields_ = [("x", ctypes.c_int), ("y", ctypes.c_int), ("width", ctypes.c_int), ("height", ctypes.c_int)]

    def __init__(self):
        self.api = ctypes.CDLL("libatspi.so.0")
        self.glib = ctypes.CDLL("libglib-2.0.so.0")
        self.objects = ctypes.CDLL("libgobject-2.0.so.0")
        self.glib.g_free.argtypes = [ctypes.c_void_p]
        self.glib.g_error_free.argtypes = [ctypes.c_void_p]
        self.objects.g_object_unref.argtypes = [ctypes.c_void_p]
        pointer = ctypes.c_void_p
        error = ctypes.POINTER(pointer)
        signatures = {
            "atspi_get_desktop": ([ctypes.c_int], pointer),
            "atspi_accessible_get_name": ([pointer, error], pointer),
            "atspi_accessible_get_role_name": ([pointer, error], pointer),
            "atspi_accessible_get_process_id": ([pointer, error], ctypes.c_uint),
            "atspi_accessible_get_child_count": ([pointer, error], ctypes.c_int),
            "atspi_accessible_get_child_at_index": ([pointer, ctypes.c_int, error], pointer),
            "atspi_accessible_get_text_iface": ([pointer], pointer),
            "atspi_accessible_get_component_iface": ([pointer], pointer),
            "atspi_accessible_get_state_set": ([pointer], pointer),
            "atspi_state_set_contains": ([pointer, ctypes.c_int], ctypes.c_int),
            "atspi_component_get_extents": ([pointer, ctypes.c_int, error], pointer),
        }
        for name, (arguments, result) in signatures.items():
            function = getattr(self.api, name)
            function.argtypes = arguments
            function.restype = result

    def call(self, name, *arguments):
        error = ctypes.c_void_p()
        result = getattr(self.api, name)(*arguments, ctypes.byref(error))
        if error.value:
            try:
                message = ctypes.cast(error, ctypes.POINTER(self.Error)).contents.message.decode("utf-8")
                raise RuntimeError(message)
            finally:
                self.glib.g_error_free(error)
        return result

    def text(self, name, node):
        value = self.call(name, node)
        if not value:
            return ""
        try:
            return ctypes.string_at(value).decode("utf-8")
        finally:
            self.glib.g_free(value)

    def snapshot(self, pid):
        records = []
        desktop = self.api.atspi_get_desktop(0)
        if not desktop:
            raise RuntimeError("The AT-SPI desktop is unavailable.")

        def walk(node, depth=0, within_editor=False):
            if depth > 40 or len(records) >= 1000:
                raise RuntimeError("The accessibility tree exceeds the audit limit.")
            record = {
                "name": self.text("atspi_accessible_get_name", node),
                "role": self.text("atspi_accessible_get_role_name", node),
                "textInterface": bool(self.api.atspi_accessible_get_text_iface(node)),
            }
            record["withinEditor"] = within_editor or record["name"] == "Document editor"
            component = self.api.atspi_accessible_get_component_iface(node)
            if component:
                # ATSPI_COORD_TYPE_SCREEN is 0 in the public AT-SPI ABI.
                pointer = self.call("atspi_component_get_extents", component, 0)
                if pointer:
                    try:
                        rectangle = ctypes.cast(pointer, ctypes.POINTER(self.Rectangle)).contents
                        record["bounds"] = [rectangle.x, rectangle.y, rectangle.width, rectangle.height]
                    finally:
                        self.glib.g_free(pointer)
            states = self.api.atspi_accessible_get_state_set(node)
            if states:
                try:
                    # ATSPI_STATE_CHECKED is 4 in the public AT-SPI ABI.
                    record["checked"] = bool(self.api.atspi_state_set_contains(states, 4))
                finally:
                    self.objects.g_object_unref(states)
            records.append(record)
            for index in range(self.call("atspi_accessible_get_child_count", node)):
                child = self.call("atspi_accessible_get_child_at_index", node, index)
                if child:
                    try:
                        walk(child, depth + 1, record["withinEditor"])
                    finally:
                        self.objects.g_object_unref(child)

        try:
            for index in range(self.call("atspi_accessible_get_child_count", desktop)):
                application = self.call("atspi_accessible_get_child_at_index", desktop, index)
                if application:
                    try:
                        if self.call("atspi_accessible_get_process_id", application) == pid:
                            walk(application)
                    finally:
                        self.objects.g_object_unref(application)
        finally:
            self.objects.g_object_unref(desktop)
        return records


def audit():
    if sys.platform != "linux" or os.geteuid() == 0:
        raise RuntimeError("NOT TESTED: Run this audit as a non-root Linux user.")
    for command in ("Xvfb", "xdotool", "dbus-daemon", "gdbus"):
        if not shutil.which(command):
            raise RuntimeError(f"NOT TESTED: Install {command}, then repeat the audit.")
    launcher = Path("/usr/libexec/at-spi-bus-launcher")
    if not launcher.is_file():
        raise RuntimeError("NOT TESTED: Install at-spi2-core, then repeat the audit.")
    executable = ROOT / "src/Editor.App/bin/Release/net10.0/Editor.App"
    if not executable.is_file():
        raise RuntimeError("Build Editor.App in Release, then repeat the audit.")
    (ROOT / "artifacts").mkdir(exist_ok=True)
    evidence = Path(tempfile.mkdtemp(prefix="native-accessibility-", dir=ROOT / "artifacts"))
    desktop = runpy.run_path(str(ROOT / "eng/check-native-editor.py"))["Desktop"]
    report = {
        "automatedChecks": "FAIL",
        "host": platform.freedesktop_os_release()["PRETTY_NAME"],
        "architecture": platform.machine(),
        "scales": [],
        "remainingAcceptance": ["Orca speech and navigation", "Native Windows and macOS accessibility", "OS high-contrast mode", "Physical monitor transitions"],
    }
    try:
        with contextlib.ExitStack() as stack:
            env = os.environ.copy()
            for key in ("WAYLAND_DISPLAY", "AT_SPI_BUS_ADDRESS", "AVALONIA_SCREEN_SCALE_FACTORS", "AVALONIA_GLOBAL_SCALE_FACTOR"):
                env.pop(key, None)
            runtime = evidence / "runtime"
            runtime.mkdir(mode=0o700)
            env.update(
                XDG_RUNTIME_DIR=str(runtime),
                XDG_CONFIG_HOME=str(evidence / "service-config"),
                XDG_DATA_HOME=str(evidence / "service-data"),
                XDG_CACHE_HOME=str(evidence / "service-cache"),
                LC_ALL="C.UTF-8",
                NO_AT_BRIDGE="0",
            )

            def launch(command, name, **options):
                log = stack.enter_context((evidence / (name + ".log")).open("w"))
                process = subprocess.Popen(command, env=env, start_new_session=True, stderr=log,
                                           stdout=options.pop("stdout", log), **options)

                def stop():
                    if process.poll() is None:
                        with contextlib.suppress(ProcessLookupError):
                            os.killpg(process.pid, signal.SIGTERM)
                        try:
                            process.wait(timeout=5)
                        except subprocess.TimeoutExpired:
                            with contextlib.suppress(ProcessLookupError):
                                os.killpg(process.pid, signal.SIGKILL)
                            process.wait()
                stack.callback(stop)
                return process

            # Exclude host service directories so no desktop portal mounts are activated.
            bus_config = ET.Element("busconfig")
            ET.SubElement(bus_config, "type").text = "session"
            ET.SubElement(bus_config, "listen").text = "unix:tmpdir=" + str(evidence)
            policy = ET.SubElement(bus_config, "policy", context="default")
            ET.SubElement(policy, "allow", own="*")
            ET.SubElement(policy, "allow", send_destination="*")
            ET.SubElement(policy, "allow", receive_sender="*")
            config = evidence / "bus.conf"
            ET.ElementTree(bus_config).write(config)
            bus = launch(["dbus-daemon", "--config-file=" + str(config), "--nofork", "--print-address"],
                         "bus", stdout=subprocess.PIPE, text=True)
            env["DBUS_SESSION_BUS_ADDRESS"] = bus.stdout.readline().strip()
            read_fd, write_fd = os.pipe()
            with os.fdopen(read_fd) as display:
                launch(["Xvfb", "-displayfd", str(write_fd), "-screen", "0", "3200x2000x24", "-dpi", "96", "-nolisten", "tcp"],
                       "xvfb", pass_fds=(write_fd,))
                os.close(write_fd)
                env["DISPLAY"] = ":" + display.readline().strip()
            launch([str(launcher), "--launch-immediately"], "accessibility-bus")
            deadline = time.monotonic() + 15
            while True:
                enabled = subprocess.run(["gdbus", "call", "--session", "--dest", "org.a11y.Bus", "--object-path", "/org/a11y/bus",
                                          "--method", "org.freedesktop.DBus.Properties.Set", "org.a11y.Status", "IsEnabled", "<true>"],
                                         env=env, capture_output=True, text=True, timeout=5)
                if enabled.returncode == 0:
                    break
                if time.monotonic() >= deadline:
                    raise RuntimeError("The private accessibility bus did not start. " + enabled.stderr)
                time.sleep(0.1)


            for scale in (1, 1.25, 1.5, 2):
                name = str(scale).replace(".", "-")
                profile = evidence / ("profile-" + name)
                env.update(AVALONIA_GLOBAL_SCALE_FACTOR=str(scale), XDG_DATA_HOME=str(profile), XDG_CONFIG_HOME=str(profile))
                app = launch([str(executable)], "editor-" + name)
                ui = desktop(env, evidence)
                window = ui.editor()
                geometry = dict(line.split("=", 1) for line in ui.xdo("getwindowgeometry", "--shell", window).splitlines())
                actual = [int(geometry[key]) for key in ("WIDTH", "HEIGHT")]
                expected = [round(1040 * scale), round(760 * scale)]
                if any(abs(a - b) > 2 for a, b in zip(actual, expected)):
                    raise RuntimeError(f"Native size {actual} does not match scale {scale}: {expected}.")

                ui.key("alt+v")
                ui.key("End")
                ui.key("Right")
                ui.key("Home")
                ui.key("Down")
                ui.key("Return")
                ui.type("DPI acceptance text")
                ui.editor(dirty=True)

                def snapshot():
                    result = subprocess.run([sys.executable, "-B", __file__, "--snapshot-pid", str(app.pid)],
                                            env=env, capture_output=True, text=True, timeout=20)
                    if result.returncode != 0:
                        (evidence / ("snapshot-" + name + ".log")).write_text(result.stderr)
                        raise RuntimeError("The AT-SPI snapshot failed. " + result.stderr.strip())
                    return json.loads(result.stdout)

                deadline = time.monotonic() + 15
                while True:
                    nodes = snapshot()
                    (evidence / ("tree-" + name + ".json")).write_text(json.dumps(nodes, indent=2) + "\n")
                    if any(node["name"] == "Document editor" for node in nodes):
                        break
                    if app.poll() is not None or time.monotonic() >= deadline:
                        raise RuntimeError("The editor did not expose its accessibility tree.")
                    time.sleep(0.1)
                buttons = {node["name"]: node for node in nodes if node["role"] in ("button", "push button")}
                for label in ("New", "Open", "Save", "Save As", "Find", "Replace", "Undo", "Redo"):
                    if label not in buttons:
                        raise RuntimeError(f"The accessible button is missing: {label}.")
                    if buttons[label].get("bounds", [0, 0, 0, 0])[2] < (31 * scale):
                        raise RuntimeError(f"The accessible bounds do not match display scaling: {label}.")
                editor = next((node for node in nodes if node["name"] == "Document editor"), None)
                if not editor:
                    raise RuntimeError("The accessible document name is missing.")
                ui.key("alt+v")
                ui.key("Return")
                changed = snapshot()
                wrap = next(node for node in changed if node["name"] == "Word Wrap" and node["role"] == "check box")
                if not wrap["checked"]:
                    raise RuntimeError("The accessible Word Wrap state did not change after keyboard input.")
                ui.xdo("windowsize", "--sync", window, round(400 * scale), round(600 * scale))
                time.sleep(0.2)
                narrow = snapshot()
                origin = dict(line.split("=", 1) for line in ui.xdo("getwindowgeometry", "--shell", window).splitlines())
                for node in narrow:
                    if node["name"] in ("New", "Open", "Save", "Word Wrap", "Line numbers") and node["role"] in ("button", "push button", "check box"):
                        x, y, width, height = node["bounds"]
                        if (x < int(origin["X"]) or y < int(origin["Y"])
                                or x + width > int(origin["X"]) + round(400 * scale) + 2
                                or y + height > int(origin["Y"]) + round(600 * scale) + 2):
                            raise RuntimeError(f"The control extends past the narrow window at scale {scale}: {node['name']}.")
                x, y = int(origin["X"]), int(origin["Y"])
                for theme, canvas in (("light", (255, 255, 255)), ("dark", (28, 32, 40))):
                    if theme == "dark":
                        ui.key("alt+v")
                        ui.key("End")
                        ui.key("Right")
                        ui.key("End")
                        ui.key("Return")
                    time.sleep(0.2)
                    shot = ImageGrab.grab(xdisplay=env["DISPLAY"])
                    if shot.getpixel((x + round(200 * scale), y + round(300 * scale)))[:3] != canvas:
                        raise RuntimeError(f"The {theme} canvas did not render at scale {scale}.")
                    shot.crop((x, y, x + round(400 * scale), y + round(600 * scale))).save(evidence / ("narrow-" + name + "-" + theme + ".png"))
                ui.key("ctrl+a")
                ui.key("BackSpace")
                ui.close(ui.editor())
                app.wait(timeout=15)
                if app.returncode != 0:
                    raise RuntimeError(f"The editor exited with code {app.returncode}.")
                report["scales"].append({"scale": scale, "physicalSize": actual, "themes": ["light", "dark"], "editorTextInterface": any(node["withinEditor"] and node["textInterface"] for node in nodes)})
                print(f"PASS: native controls and scaling at {scale * 100:g}% in light and dark", flush=True)
            if not all(case["editorTextInterface"] for case in report["scales"]):
                report["remainingAcceptance"].insert(0, "The source editor does not expose an AT-SPI Text interface")
            report["automatedChecks"] = "PASS"
            for remaining in report["remainingAcceptance"]:
                print("REMAINING: " + remaining, flush=True)
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        (evidence / "result.json").write_text(json.dumps(report, indent=2) + "\n")
        print(f"Evidence: {evidence}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshot-pid", type=int, help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.snapshot_pid is not None:
        print(json.dumps(AtSpi().snapshot(args.snapshot_pid)))
    else:
        audit()


if __name__ == "__main__":
    main()
