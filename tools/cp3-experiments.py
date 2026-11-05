"""E01–E05 reais. Execute na raiz após docker compose pull/up, com .env configurado.

Usa somente Python 3 (biblioteca padrão). Cria dados fictícios por HTTP, preserva
volumes e grava apenas evidências sanitizadas em artifacts/cp3. Não imprime tokens.
"""

import base64
import io
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts" / "cp3"
BASE = os.environ.get("EXPENSEHUB_URL", "http://localhost:5245")
if "--configure" in sys.argv:
    (ROOT / ".env").write_text("IMAGE_TAG=" + os.environ["IMAGE_TAG"] + "\nSEED_ADMIN_EMAIL=cp3-admin@example.test\nSEED_ADMIN_PASSWORD=" + secrets.token_urlsafe(24) + "aA1!\nRABBITMQ_USER=cp3user\nRABBITMQ_PASSWORD=" + secrets.token_urlsafe(24) + "\n")
    sys.exit(0)

CONFIG = {}
for line in (ROOT / ".env").read_text().splitlines():
    if "=" in line and not line.startswith("#"):
        key, value = line.split("=", 1)
        CONFIG[key] = value
CHECKS = []
NS = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}


def check(name, condition, detail=None):
    CHECKS.append({"name": name, "passed": bool(condition), "detail": detail})
    if not condition:
        raise AssertionError(name)


def request(method, path, body=None, token=None, expected=200, raw=False):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(BASE + path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
    try:
        response = urllib.request.urlopen(req, timeout=35)
    except urllib.error.HTTPError as error:
        response = error
    data = response.read()
    check(f"{method} {path}: HTTP {expected}", response.status == expected, response.status)
    if raw:
        return data, dict(response.headers)
    return json.loads(data) if data else None


def compose(*args):
    result = subprocess.run(["docker", "compose", *args], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=240)
    if result.returncode:
        raise RuntimeError(f"docker compose {' '.join(args)} failed: {result.stderr[-1500:]}")
    return result.stdout


def login(email, password):
    return request("POST", "/login", {"email": email, "password": password})["accessToken"]


def account(admin, role, suffix):
    email = f"cp3-{role.lower()}-{suffix}@example.test"
    password = secrets.token_urlsafe(24) + "aA1!"
    request("POST", "/register", {"email": email, "password": password})
    users = request("GET", "/api/admin/users", token=admin)
    user = next(user for user in users if user["email"] == email)
    roles = [role] if role != "Auditor" else ["Auditor", "Approver", "Finance"]
    request("PUT", f"/api/admin/users/{user['id']}/roles", {"roles": roles}, admin, 200)
    return login(email, password)


def expense(token, reviewer, amount, date, state):
    row = request("POST", "/api/expenses", {"description": "CP3 fictional expense", "amount": amount, "expenseDate": date, "categoryId": 1}, token, 201)
    if state in ("Approved", "Paid"):
        request("POST", f"/api/expenses/{row['id']}/submit", token=token)
        request("POST", f"/api/expenses/{row['id']}/approve", token=reviewer)
    if state == "Paid":
        request("POST", f"/api/expenses/{row['id']}/pay", token=reviewer)
    return row["id"]


def report(token, scope="Mine"):
    data, headers = request("POST", "/api/reports", {"fromDate": "2026-09-01", "toDate": "2026-09-30", "scope": scope}, token, 202, True)
    row = json.loads(data)
    check("202 Location points to saved request", headers.get("Location", "").endswith("/api/reports/" + row["id"]))
    return row["id"]


def completed(token, report_id):
    for _ in range(90):
        row = request("GET", f"/api/reports/{report_id}", token=token)
        if row["status"] == "Completed":
            return row
        check("report did not fail", row["status"] != "Failed")
        time.sleep(1)
    raise TimeoutError(report_id)


def broker(path, body=None):
    credentials = base64.b64encode(f"{CONFIG['RABBITMQ_USER']}:{CONFIG['RABBITMQ_PASSWORD']}".encode()).decode()
    req = urllib.request.Request("http://localhost:15672/api/" + path, data=None if body is None else json.dumps(body).encode(), headers={"Authorization": "Basic " + credentials, "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=20) as response:
        return json.load(response)


def verify_excel(data, row):
    with zipfile.ZipFile(io.BytesIO(data)) as book:
        sheet = ET.fromstring(book.read("xl/worksheets/sheet1.xml"))
        strings = ET.fromstring(book.read("xl/sharedStrings.xml"))
        shared = ["".join(item.itertext()) for item in strings]
        cells = {cell.attrib["r"]: cell for cell in sheet.findall(".//s:c", NS)}
        def value(address):
            cell = cells[address]
            text = cell.findtext("s:v", namespaces=NS)
            return shared[int(text)] if cell.attrib.get("t") == "s" else float(text)
        check("Excel identifier matches", value("B1") == row["id"])
        check("Excel headers", [value(x) for x in ("A7", "B7", "C7")] == ["Status", "Count", "Amount"])
        for index, total in enumerate(row["result"]["byStatus"], 8):
            check("Excel numeric row " + total["status"], value(f"A{index}") == total["status"] and value(f"B{index}") == total["count"] and value(f"C{index}") == total["amount"] and cells[f"C{index}"].attrib.get("t", "n") == "n")
        check("Excel totals equal persisted result", value("B13") == row["result"]["totalCount"] and value("C13") == row["result"]["totalAmount"])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    admin = login(CONFIG["SEED_ADMIN_EMAIL"], CONFIG["SEED_ADMIN_PASSWORD"])
    suffix = uuid.uuid4().hex[:10]
    employee = account(admin, "Employee", suffix)
    other = account(admin, "Employee", suffix + "b")
    auditor = account(admin, "Auditor", suffix)
    expense(employee, auditor, 100, "2026-09-01", "Approved")
    expense(employee, auditor, 50, "2026-09-30", "Approved")
    expense(employee, auditor, 25, "2026-09-15", "Paid")
    expense(other, auditor, 200, "2026-09-15", "Approved")
    expense(employee, auditor, 500, "2026-08-31", "Paid")

    first = report(employee)
    row = completed(employee, first)
    check("E01 Mine equals 3 expenses and 175", row["result"]["totalCount"] == 3 and row["result"]["totalAmount"] == 175)
    data, headers = request("GET", f"/api/reports/{first}/excel", token=employee, raw=True)
    check("E01 xlsx MIME and attachment", headers.get("Content-Type") == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" and ".xlsx" in headers.get("Content-Disposition", ""))
    verify_excel(data, row)
    (OUT / "E01-report.xlsx").write_bytes(data)
    (OUT / "E01-result.json").write_text(json.dumps(row, indent=2), encoding="utf-8")

    compose("stop", "worker")
    waiting = report(employee)
    pending = request("GET", f"/api/reports/{waiting}", token=employee)
    check("E02 same id stays Pending with stopped worker", pending["status"] == "Pending")
    request("GET", f"/api/reports/{waiting}/excel", token=employee, expected=409)
    queue = {}
    for _ in range(20):
        queue = broker("queues/%2F/expensehub.reports")
        if queue.get("messages_ready", 0) >= 1:
            break
        time.sleep(1)
    check("E02 RabbitMQ Ready >= 1", queue.get("messages_ready", 0) >= 1)
    compose("up", "-d", "worker")
    resumed = completed(employee, waiting)
    (OUT / "E02.json").write_text(json.dumps({"id": waiting, "before": pending, "ready": queue.get("messages_ready"), "after": resumed}, indent=2), encoding="utf-8")

    before = request("GET", f"/api/reports/{first}", token=employee)
    compose("restart", "worker")
    publication = broker("exchanges/%2F/amq.default/publish", {"properties": {"delivery_mode": 2, "content_type": "application/json"}, "routing_key": "expensehub.reports", "payload": json.dumps({"reportId": first}), "payload_encoding": "string"})
    check("E03 duplicate routed", publication["routed"])
    duplicate_logs = ""
    for _ in range(30):
        duplicate_logs = compose("logs", "--no-color", "worker")
        if f"Report {first} duplicate ignored" in duplicate_logs:
            break
        time.sleep(1)
    check("E03 new worker recognized persisted duplicate", f"Report {first} duplicate ignored" in duplicate_logs)
    after = request("GET", f"/api/reports/{first}", token=employee)
    check("E03 no changed result or timestamps", before == after)
    duplicate_excel, _ = request("GET", f"/api/reports/{first}/excel", token=employee, raw=True)
    verify_excel(duplicate_excel, before)
    (OUT / "E03.json").write_text(json.dumps({"reportId": first, "before": before, "after": after, "payload": {"reportId": first}, "routed": publication["routed"]}, indent=2), encoding="utf-8")

    request("GET", "/api/reports", expected=401)
    request("POST", "/api/reports", {"fromDate": "2026-09-01", "toDate": "2026-09-30", "scope": "Global"}, employee, 403)
    request("POST", "/api/reports", {"fromDate": "2026-09-30", "toDate": "2026-09-01", "scope": "Mine"}, employee, 400)
    request("POST", "/api/reports", {"fromDate": "bad", "toDate": "2026-09-30", "scope": "Mine"}, employee, 400)
    for token in (other, auditor):
        request("GET", f"/api/reports/{first}", token=token, expected=404)
        request("GET", f"/api/reports/{first}/excel", token=token, expected=404)
        check("E04 other list hides first report", all(item["id"] != first for item in request("GET", "/api/reports", token=token)))
    global_id = report(auditor, "Global")
    global_row = completed(auditor, global_id)
    check("E04 Global includes other employee", global_row["result"]["totalCount"] >= 4 and global_row["result"]["totalAmount"] >= 375)
    other_id = report(other)
    other_row = completed(other, other_id)
    check("E04 second Mine includes only second employee", other_row["result"]["totalCount"] == 1 and other_row["result"]["totalAmount"] == 200)
    (OUT / "E04.json").write_text(json.dumps({"mineId": first, "global": global_row, "secondMine": other_row}, indent=2), encoding="utf-8")

    compose("up", "-d", "--force-recreate", "web", "worker")
    for _ in range(60):
        try:
            persisted = request("GET", f"/api/reports/{first}", token=employee)
            break
        except (urllib.error.URLError, ConnectionError):
            time.sleep(1)
    else:
        raise TimeoutError("web recreation")
    check("E05 same token and result survive recreation", persisted == before)
    (OUT / "E05.json").write_text(json.dumps({"imageTag": CONFIG["IMAGE_TAG"], "reportId": first, "persisted": persisted}, indent=2), encoding="utf-8")
    (OUT / "containers.json").write_text(compose("ps", "--format", "json"), encoding="utf-8")
    (OUT / "worker.log").write_text(duplicate_logs + "\n" + compose("logs", "--no-color", "--tail", "80", "worker"), encoding="utf-8")
    (OUT / "web.log").write_text(compose("logs", "--no-color", "--tail", "80", "web"), encoding="utf-8")
    (OUT / "checks.json").write_text(json.dumps(CHECKS, indent=2), encoding="utf-8")
    print(json.dumps({"passed": len(CHECKS), "failed": 0, "E01": first, "E02": waiting, "Global": global_id, "imageTag": CONFIG["IMAGE_TAG"]}, indent=2))


if __name__ == "__main__":
    try:
        main()
    finally:
        OUT.mkdir(parents=True, exist_ok=True)
        (OUT / "checks.json").write_text(json.dumps(CHECKS, indent=2), encoding="utf-8")
