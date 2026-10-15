"""Run after dotnet build: python tests/IdentitySeedCollisionSmoke.py."""

import contextlib
import json
import os
import pathlib
import secrets
import socket
import sqlite3
import subprocess
import time
import urllib.error
import urllib.request


project = pathlib.Path(__file__).resolve().parents[1] / "sources/ExpenseHub.Api"
database = project / "obj/seed-collision-smoke.db"
assert not database.exists(), "Refusing to overwrite an existing smoke database"
dotnet = pathlib.Path(os.environ.get("DOTNET_ROOT", str(pathlib.Path.home() / ".dotnet10"))) / "dotnet.exe"
environment = os.environ.copy()
environment["ASPNETCORE_ENVIRONMENT"] = "Development"
environment["ConnectionStrings__ExpenseHub"] = "Data Source=" + str(database)
environment["SeedAdmin__Email"] = "admin@expensehub.local"
environment["SeedAdmin__Password"] = ""
attacker_password = secrets.token_urlsafe(24) + chr(65) + chr(97) + chr(49) + chr(33)
with socket.socket() as listener:
    listener.bind(("127.0.0.1", 0))
    address = "http://127.0.0.1:" + str(listener.getsockname()[1])


def request(method, path, body=None, bearer=None):
    headers = {"Content-Type": "application/json"}
    if bearer:
        headers["Authorization"] = "Bearer " + bearer
    message = urllib.request.Request(
        address + path,
        data=None if body is None else json.dumps(body).encode(),
        headers=headers,
        method=method,
    )
    try:
        response = urllib.request.urlopen(message, timeout=5)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        payload = response.read()
        return response.status, json.loads(payload) if payload else None


def start(log):
    process = subprocess.Popen(
        [str(dotnet), "run", "--no-build", "--no-launch-profile", "--urls", address],
        cwd=project,
        env=environment,
        stdout=log,
        stderr=log,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    try:
        for attempt in range(150):
            assert process.poll() is None, "Server failed; inspect obj/seed-collision-smoke.log"
            try:
                if request("GET", "/health")[0] == 200:
                    return process
            except OSError:
                pass
            time.sleep(0.1)
        raise AssertionError("Server startup timed out")
    except BaseException:
        stop(process)
        raise


def stop(process):
    subprocess.run(
        ["taskkill", "/PID", str(process.pid), "/T", "/F"],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        check=False,
    )
    process.wait(timeout=15)


process = None
try:
    with open(project / "obj/seed-collision-smoke.log", "w", encoding="utf-8") as log:
        process = start(log)
        with contextlib.closing(sqlite3.connect(database)) as connection:
            assert connection.execute("SELECT COUNT(*) FROM AspNetUsers").fetchone()[0] == 0
        status, registered = request(
            "POST", "/register", {"email": environment["SeedAdmin__Email"], "password": attacker_password}
        )
        assert status == 200 and registered["roles"] == []
        stop(process)
        process = None

        for configure_seed_password in (False, True):
            environment["SeedAdmin__Password"] = secrets.token_urlsafe(24) if configure_seed_password else ""
            process = start(log)
            status, login = request(
                "POST", "/login", {"email": environment["SeedAdmin__Email"], "password": attacker_password}
            )
            assert status == 200
            status, _ = request("GET", "/api/admin/users", bearer=login["accessToken"])
            assert status == 403, f"Preexisting non-Admin gained administration: HTTP {status}"
            with contextlib.closing(sqlite3.connect(database)) as connection:
                assert connection.execute("SELECT COUNT(*) FROM AspNetUsers").fetchone()[0] == 1
                assert connection.execute("SELECT COUNT(*) FROM AspNetRoles").fetchone()[0] == 5
                assert connection.execute("SELECT COUNT(*) FROM AspNetUserRoles").fetchone()[0] == 0
            print("Seed collision blocked: 403, no role assignment; seed password configured:", configure_seed_password)
            stop(process)
            process = None
finally:
    if process is not None:
        stop(process)
    for suffix in ("", "-wal", "-shm"):
        path = pathlib.Path(str(database) + suffix)
        for attempt in range(50):
            try:
                path.unlink(missing_ok=True)
                break
            except PermissionError:
                time.sleep(0.1)
        else:
            raise AssertionError("Smoke database still locked after shutdown")
