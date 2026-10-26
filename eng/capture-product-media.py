#!/usr/bin/env python3
"""Capture the real Linux editor on an isolated X11 display."""

import argparse
import contextlib
import hashlib
import json
import os
from pathlib import Path
import platform
import runpy
import shutil
import subprocess
import sys
import tempfile
import time

from PIL import ImageGrab

ROOT = Path(__file__).resolve().parents[1]
NATIVE = runpy.run_path(str(ROOT / 'eng/check-native-editor.py'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/product-media')
    args = parser.parse_args()
    if sys.platform != 'linux' or os.geteuid() == 0:
        parser.error('Run as a non-root user on Linux.')
    for command in ('Xvfb', 'xdotool', 'dbus-daemon'):
        if not shutil.which(command):
            parser.error(f'Install {command}, then repeat the capture.')
    args.output.mkdir(parents=True, exist_ok=True)
    output = args.output.resolve()
    subprocess.run(['dotnet', 'build', 'src/Editor.App', '-c', 'Release', '--no-restore', '--disable-build-servers', '-m:1'], cwd=ROOT, check=True)
    executable = ROOT / 'src/Editor.App/bin/Release/net10.0/Editor.App'
    sample = ROOT / 'docs/user/samples/field-notes.md'
    with contextlib.ExitStack() as stack:
        scratch = Path(stack.enter_context(tempfile.TemporaryDirectory(prefix='tangopad-media-')))
        env = os.environ.copy()
        env.pop('WAYLAND_DISPLAY', None)
        env.update(LC_ALL='C.UTF-8', GDK_BACKEND='x11', XDG_CURRENT_DESKTOP='GNOME',
                   XDG_CONFIG_HOME=str(scratch / 'config'), XDG_CACHE_HOME=str(scratch / 'cache'),
                   XDG_DATA_HOME=str(scratch / 'data'), XDG_RUNTIME_DIR=str(scratch / 'runtime'))
        Path(env['XDG_RUNTIME_DIR']).mkdir(mode=0o700)
        config = Path(env['XDG_CONFIG_HOME']) / 'xdg-desktop-portal'
        config.mkdir(parents=True)
        (config / 'portals.conf').write_text('[preferred]\ndefault=gtk\n')

        def launch(command, name, **kwargs):
            log = stack.enter_context((output / f'{name}.log').open('w'))
            process = subprocess.Popen(command, env=env, stderr=log, stdout=kwargs.pop('stdout', log), **kwargs)

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

        read_fd, write_fd = os.pipe()
        with os.fdopen(read_fd) as display_number:
            launch(['Xvfb', '-displayfd', str(write_fd), '-screen', '0', '1280x900x24', '-nolisten', 'tcp'],
                   'xvfb', pass_fds=(write_fd,))
            os.close(write_fd)
            env['DISPLAY'] = ':' + display_number.readline().strip()
        bus = launch(['dbus-daemon', '--session', '--nofork', '--print-address'], 'bus',
                     stdout=subprocess.PIPE, text=True)
        env['DBUS_SESSION_BUS_ADDRESS'] = bus.stdout.readline().strip()
        launch(['/usr/libexec/xdg-desktop-portal-gtk'], 'gtk')
        launch(['/usr/libexec/xdg-desktop-portal'], 'portal')
        time.sleep(2)
        app = launch([str(executable)], 'editor')
        ui = NATIVE['Desktop'](env, output)
        ui.editor()
        ui.picker('ctrl+o', 'Open text file')
        ui.choose(sample, saving=False)
        window = ui.editor(sample)
        ui.xdo('windowmove', window, 0, 0)
        ui.xdo('windowsize', '--sync', window, 1040, 760)
        # View > Appearance > Light uses the real menu and keyboard routing.
        ui.key('alt+v')
        ui.key('End')
        ui.key('Right')
        ui.key('Home')
        ui.key('Down')
        ui.key('Return')
        ui.command(window, 'preview')

        records = []

        def capture(name):
            time.sleep(1)
            ui.xdo('mousemove', 1200, 850)
            shot = ImageGrab.grab(xdisplay=env['DISPLAY']).crop((0, 0, 1040, 760))
            target = output / name
            shot.save(target, optimize=True)
            records.append({'file': name, 'width': shot.width, 'height': shot.height,
                            'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})

        capture('markdown.png')
        ui.key('ctrl+h')
        ui.type('draft')
        capture('search.png')
        ui.key('Escape')
        ui.close(ui.editor(sample))
        NATIVE['wait']('editor exit', lambda: app.poll() is not None)
        if app.returncode != 0:
            raise RuntimeError(f'The editor exited with code {app.returncode}.')
        manifest = {
            'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
            'sourceDiffSha256': hashlib.sha256(subprocess.check_output(['git', 'diff', 'HEAD', '--', 'src',
                                                                       'Directory.Build.props', 'Directory.Packages.props'], cwd=ROOT)).hexdigest(),
            'executableSha256': hashlib.sha256(executable.read_bytes()).hexdigest(),
            'assemblySha256': hashlib.sha256(executable.with_name(executable.name + '.dll').read_bytes()).hexdigest(),
            'host': platform.freedesktop_os_release().get('PRETTY_NAME', platform.system()),
            'architecture': platform.machine(), 'session': 'Xvfb, X11, 100% scale, Light, 1040 x 760',
            'sample': 'docs/user/samples/field-notes.md',
            'sampleSha256': hashlib.sha256(sample.read_bytes()).hexdigest(),
            'releaseCandidateValidated': False,
            'images': records,
        }
        (output / 'capture.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print(f'Captured two development images in {output}. Inspect both images before publication.')


if __name__ == '__main__':
    main()
