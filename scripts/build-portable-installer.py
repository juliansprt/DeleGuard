"""Build a PRIVATE one-directory portable package on its target OS. No global installation.

Requires dotnet 10 and PyInstaller in the invoking Python environment. Signing and
platform acceptance are separate release gates; this command never publishes.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys

repo = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--output", default=str(repo / "artifacts" / "portable-build"))
args = parser.parse_args()
rid = {"Windows": "win", "Darwin": "osx", "Linux": "linux"}[platform.system()] + ("-arm64" if platform.machine().lower() in ("aarch64", "arm64") else "-x64")
output = Path(args.output).resolve()
if not output.is_relative_to(repo / "artifacts"):
    raise SystemExit("Build output must stay inside this repository's artifacts directory")
package = output / rid / "DeleGuard"
package.mkdir(parents=True, exist_ok=True)
subprocess.run(["dotnet", "publish", str(repo / "src/Deleguard.Desktop"), "-c", "Release", "-r", rid,
    "--self-contained", "true", "-p:PublishSingleFile=false", "-o", str(package), "--nologo"], check=True)
# A native bootstrap disables CoreCLR's system-temp diagnostic IPC before CLR starts.
# setenv affects this process only; there is no shell profile or persistent variable.
if platform.system() != "Windows":
    os.replace(package / "Deleguard.Desktop", package / ".deleguard-host")
    subprocess.run(["cc", "-O2", "-Wall", "-Wextra", "-Werror", str(repo / "integrations/ide/portable-launcher.c"),
                    "-o", str(package / "Deleguard.Desktop")], check=True)
source = repo / "integrations/ide"
subprocess.run([sys.executable, "-m", "PyInstaller", "--noconfirm", "--clean", "--onedir", "--name", "deleguard-adapter",
    "--distpath", str(output / "worker-dist"), "--workpath", str(output / "worker-build"), "--specpath", str(output),
    "--paths", str(source), "--add-data", str(source / "assets") + os.pathsep + "assets",
    "--add-data", str(source / "deleguard_ide.py") + os.pathsep + ".",
    "--add-data", str(source / "project_setup.py") + os.pathsep + ".", str(source / "portable_entry.py")], check=True)
shutil.copytree(output / "worker-dist/deleguard-adapter", package / "worker", dirs_exist_ok=True)
(package / "README.txt").write_text("DeleGuard portable — PRIVATE VALIDATION BUILD\n\n"
    "Not signed or approved for public distribution.\n"
    "Open Deleguard.Desktop, import the profile from IDE Control, and select a project folder.\n"
    "Each project keeps its own encrypted key, runtime, settings and history.\n"
    "Keep the entire package together. No Python installation is needed.\n"
    "Windows uses a project-local DPAPI file. macOS/Linux require a project password.\n", encoding="utf-8")
archive = Path(shutil.make_archive(str(output / ("deleguard-portable-" + rid)), "zip", package.parent, package.name))
manifest = dict(platform=rid, version="4.0.0", file=archive.name, sha256=hashlib.file_digest(archive.open("rb"),"sha256").hexdigest(),
    signed=False, platform_tested=False, channel="private-validation")
(output / (rid + ".json")).write_text(json.dumps(manifest,indent=2),encoding="utf-8")
print(json.dumps(manifest))
