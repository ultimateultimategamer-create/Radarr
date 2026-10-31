#!/usr/bin/env python3
"""Boot a real Radarr process with disposable data and exercise its HTTP API."""
import argparse
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET


def find_assembly(path):
    """Accepts the app's dll or a build output folder. On Linux and macOS the console app is built as
    Radarr.dll inside a runtime folder (for example _output/net10.0/linux-x64), on Windows as Radarr.Console.dll."""
    path = Path(path)

    if path.is_file():
        return path.resolve()

    candidates = [
        candidate for name in ("Radarr.Console.dll", "Radarr.dll")
        for candidate in sorted(path.rglob(name))
        if "Radarr.Update" not in candidate.parts and "publish" not in candidate.parts
    ]

    if not candidates:
        raise FileNotFoundError(f"Could not find Radarr.Console.dll or Radarr.dll under {path}")

    return candidates[0].resolve()


def annotate_failure(title, text):
    escaped = text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
    print(f"::error title={title}::{escaped}")


def smoke_startup(assembly, dotnet, timeout):
    assembly = find_assembly(assembly)
    print(f"Starting {assembly}")
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]

    with tempfile.TemporaryDirectory(prefix="radarr-startup-") as temporary:
        data = Path(temporary)
        api_key = secrets.token_hex(16)
        config = ET.Element("Config")
        for key, value in {
            "BindAddress": "127.0.0.1",
            "Port": str(port),
            "EnableSsl": "False",
            "LaunchBrowser": "False",
            "ApiKey": api_key,
            "AuthenticationMethod": "External",
            "AnalyticsEnabled": "False",
            "UpdateAutomatically": "False",
        }.items():
            ET.SubElement(config, key).text = value
        ET.ElementTree(config).write(data / "config.xml", encoding="utf-8", xml_declaration=True)

        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

        def get_json(path):
            request = urllib.request.Request(
                f"http://127.0.0.1:{port}/api/v3/{path}",
                headers={"X-Api-Key": api_key},
            )
            with opener.open(request, timeout=3) as response:
                return json.load(response)

        with (data / "startup.log").open("w+", encoding="utf-8") as output:
            process = subprocess.Popen(
                [dotnet, str(assembly), "-nobrowser", "-nosingleinstancecheck", f"-data={data}"],
                cwd=assembly.parent,
                stdin=subprocess.DEVNULL,
                stdout=output,
                stderr=subprocess.STDOUT,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
            )
            try:
                deadline = time.monotonic() + timeout
                last_error = "No HTTP response"
                while time.monotonic() < deadline:
                    if process.poll() is not None:
                        raise RuntimeError(f"Radarr exited during startup with code {process.returncode}")
                    try:
                        status = get_json("system/status")
                        if not status.get("version"):
                            raise RuntimeError("The status API did not return a Radarr version")
                        break
                    except (OSError, urllib.error.URLError, ValueError) as error:
                        last_error = str(error)
                        time.sleep(0.5)
                else:
                    raise RuntimeError(f"Radarr did not become ready within {timeout}s: {last_error}")

                # This controller resolves both movie services and queries the migrated versions table.
                if get_json("movieversion?movieId=1") != []:
                    raise RuntimeError("A fresh database unexpectedly contains movie versions")
                time.sleep(1)
                if process.poll() is not None:
                    raise RuntimeError("Radarr stopped immediately after becoming ready")
                get_json("system/status")
                print("Startup smoke test passed: status and movie-version APIs are responsive.")
            except BaseException as error:
                output.flush()
                output.seek(0)
                log = output.read()[-20000:]
                print(log)
                annotate_failure("Radarr startup failed", f"{error}\n{log[-12000:]}")
                raise
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=15)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=5)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("assembly", help="Path to the built Radarr.Console.dll/Radarr.dll, or the build output folder")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--timeout", type=float, default=90)
    args = parser.parse_args()

    try:
        smoke_startup(args.assembly, args.dotnet, args.timeout)
    except FileNotFoundError as error:
        annotate_failure("Startup smoke test failed", str(error))
        raise
