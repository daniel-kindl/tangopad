#!/usr/bin/env python3
"""Exercise native AppImage updates with signed local feeds and real application-state storage."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parent.parent


def run(*args, **kwargs):
    return subprocess.run([str(arg) for arg in args], cwd=ROOT, check=True, **kwargs)


def main():
    evidence = ROOT / "artifacts/update-engine"
    evidence.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="tangopad-update-") as temporary:
        work = Path(temporary)
        project = work / "probe"
        project.mkdir()
        (project / "Probe.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
<ItemGroup><ProjectReference Include="{ROOT}/src/Editor.Infrastructure/Editor.Infrastructure.csproj" />
<Compile Include="{ROOT}/src/Editor.Infrastructure/Updates/SignedReleaseSource.cs" Link="SignedReleaseSource.cs" />
<Compile Include="{ROOT}/src/Editor.Infrastructure/Updates/UpdateHandoff.cs" Link="UpdateHandoff.cs" /></ItemGroup>
</Project>''')
        (project / "Program.cs").write_text('''using System.Security.Cryptography;
using System.Text.Json;
using Editor.Infrastructure;
using Editor.Infrastructure.Updates;
using Velopack;
using Velopack.Sources;
VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
DesktopApplicationUpdater.WaitForPendingUpdate();
string statePath = UserDataLocation.StatePath([args[3]], DesktopApplicationUpdater.GetDistributionDirectory());
AppStateStore state = new(statePath);
await state.Ready;
if (state.StorageError is not null) { throw new IOException(state.StorageError); }
state.Preferences.Update(wordWrap: true, fontSize: 17);
state.History.Record(args[4]);
await Task.WhenAll(state.Preferences.FlushAsync(), state.History.FlushAsync());
if (state.Preferences.StorageFailed || state.History.StorageFailed) { throw new IOException("State flush failed."); }
UpdateManager manager = new(new SimpleFileSource(new DirectoryInfo(args[0])), new UpdateOptions { ExplicitChannel = "linux-x64" });
File.WriteAllText(args[2], JsonSerializer.Serialize(new { version = manager.CurrentVersion!.ToString(), statePath,
    wrap = state.Preferences.Current.WordWrap, font = state.Preferences.Current.FontSize, recent = state.History.Paths }));
if (args.Length > 5 && args[5] == "apply") {
    byte[] bytes = File.ReadAllBytes(Path.Combine(args[0], "releases.linux-x64.json"));
    byte[] signature = File.ReadAllBytes(Path.Combine(args[0], "releases.linux-x64.json.sig"));
    _ = SignedReleaseSource.VerifyFeed(bytes, signature, File.ReadAllText(args[1]), "linux-x64");
    UpdateInfo update = await manager.CheckForUpdatesAsync() ?? throw new Exception("No update found.");
    await manager.DownloadUpdatesAsync(update);
    using UpdateHandoff handoff = UpdateHandoff.Begin(DesktopApplicationUpdater.GetDistributionDirectory()!);
    manager.ApplyUpdatesAndRestart(update.TargetFullRelease, args[..5]);
}
''')
        key = work / "private.pem"
        public = work / "public.pem"
        run("openssl", "genpkey", "-algorithm", "EC", "-pkeyopt", "ec_paramgen_curve:P-256", "-out", key)
        run("openssl", "pkey", "-in", key, "-pubout", "-out", public)
        run("dotnet", "tool", "restore")
        run("dotnet", "publish", project / "Probe.csproj", "-r", "linux-x64", "--self-contained", "true",
            "-p:PublishSingleFile=true", f"-p:ReleaseLockDirectory={work}/locks", "-o", work / "publish")
        feed = work / "feed"
        initial = work / "initial.AppImage"
        newer = f"0.2.{int(time.time())}"
        for version in ("0.1.0", newer):
            run("dotnet", "tool", "run", "vpk", "--", "pack", "--packId", "TangopadDesktop", "--packVersion", version,
                "--packDir", work / "publish", "--mainExe", "Probe", "--runtime", "linux-x64", "--channel", "linux-x64",
                "--icon", ROOT / "eng/release/tangopad.svg", "--delta", "None", "--outputDir", feed)
            if version == "0.1.0":
                shutil.copy2(feed / "TangopadDesktop-linux-x64.AppImage", initial)
        # The production feed contains only the newest full stable package.
        feed_file = feed / "releases.linux-x64.json"
        metadata = json.loads(feed_file.read_bytes())
        metadata["Assets"] = [item for item in metadata["Assets"] if item["Version"] == newer]
        feed_file.write_text(json.dumps(metadata))
        run("openssl", "dgst", "-sha256", "-sign", key, "-out", str(feed_file) + ".sig", feed_file)
        results = []
        for mode in ("--installed", "--portable"):
            directory = work / mode[2:]
            directory.mkdir()
            app = directory / "Tangopad.AppImage"
            shutil.copy2(initial, app)
            app.chmod(0o755)
            marker = directory / "tangopad.portable"
            marker.write_bytes(b"preserve marker")
            sibling = directory / "data/keep.bin"
            sibling.parent.mkdir()
            sibling.write_bytes(b"preserve sibling data")
            document = directory / "document.txt"
            document.write_bytes("keep λ café\r\n".encode())
            result_path = directory / "result.json"
            env = dict(os.environ, APPIMAGE_EXTRACT_AND_RUN="1", XDG_DATA_HOME=str(work / "installed-state"))
            arguments = [app, feed, public, result_path, mode, document]
            with (evidence / f"{mode[2:]}.log").open("w") as log:
                run(*arguments, env=env, stdout=log, stderr=log)
                state_path = Path(json.loads(result_path.read_bytes())["statePath"])
                before = state_path.read_bytes()
                result_path.unlink()
                run(*arguments, "apply", env=env, stdout=log, stderr=log)
                deadline = time.monotonic() + 30
                while time.monotonic() < deadline:
                    if result_path.is_file() and json.loads(result_path.read_bytes())["version"] == newer:
                        break
                    time.sleep(0.1)
                else:
                    raise RuntimeError(f"The {mode} update did not restart on the new version. Read {log.name}.")
            result = json.loads(result_path.read_bytes())
            assert result["wrap"] is True and result["font"] == 17 and result["recent"] == [str(document)]
            assert state_path.read_bytes() == before
            assert marker.read_bytes() == b"preserve marker"
            assert sibling.read_bytes() == b"preserve sibling data"
            assert document.read_bytes() == "keep λ café\r\n".encode()
            assert hashlib.sha256(app.read_bytes()).digest() == hashlib.sha256((feed / "TangopadDesktop-linux-x64.AppImage").read_bytes()).digest()
            results.append({"mode": mode, "version": result["version"], "state_preserved": True, "document_preserved": True})
        (evidence / "result.json").write_text(json.dumps(results, indent=2) + "\n")
        print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
