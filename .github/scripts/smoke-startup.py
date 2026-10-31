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


def smoke_startup(assembly, dotnet, timeout):
    assembly = Path(assembly).resolve(strict=True)
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
            except BaseException:
                output.flush()
                output.seek(0)
                print(output.read()[-20000:])
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
    parser.add_argument("assembly", help="Path to the built Radarr.Console.dll")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--timeout", type=float, default=90)
    args = parser.parse_args()
    smoke_startup(args.assembly, args.dotnet, args.timeout)
