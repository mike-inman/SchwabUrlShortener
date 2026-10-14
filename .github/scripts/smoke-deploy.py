"""Exercise public release routes without logging response bodies or credentials."""
import json
import os
import sys
import urllib.error
import urllib.request
from urllib.parse import urljoin, urlparse
from uuid import uuid4

origin = sys.argv[1].rstrip("/") + "/"
parsed = urlparse(origin)
if parsed.scheme != "https" or not parsed.hostname or parsed.path != "/" or parsed.query or parsed.fragment:
    raise SystemExit("PUBLIC_ORIGIN must be an HTTPS origin root")
read_only = "--read-only" in sys.argv[2:]

summary_path = os.environ.get("GITHUB_STEP_SUMMARY")


def report(name, passed, detail=""):
    line = f"- Smoke {name}: {'passed' if passed else 'FAILED'}{f' ({detail})' if detail else ''}."
    print(line)
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as summary:
            summary.write(line + "\n")
    if not passed:
        raise SystemExit(1)


def request(path, method="GET", payload=None, base=origin):
    data = json.dumps(payload).encode() if payload is not None else None
    headers = {"Content-Type": "application/json"} if data else {}
    return urllib.request.Request(urljoin(base, path), data=data, headers=headers, method=method)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


opener = urllib.request.build_opener(NoRedirect)


def get_status(path):
    try:
        with opener.open(request(path), timeout=15) as response:
            return response.status
    except urllib.error.HTTPError as error:
        return error.code
    except urllib.error.URLError:
        return 0


for path, name in (("health/live", "liveness"), ("health/ready", "readiness"),
                   ("favicon.ico", "favicon")):
    status = get_status(path)
    report(name, status == 200, f"HTTP {status}")

www_origin = f"https://www.{parsed.hostname}/"
try:
    with opener.open(request("x", base=www_origin), timeout=15) as response:
        report("www redirect", False, f"HTTP {response.status}")
except urllib.error.HTTPError as error:
    destination = error.headers.get("Location", "")
    report("www redirect", error.code == 301 and destination == urljoin(origin, "x"),
           f"HTTP {error.code}")
except urllib.error.URLError:
    report("www redirect", False, "connection error")

if read_only:
    print("Read-only public checks passed; guest creation was not attempted.")
    raise SystemExit(0)

code = "ci" + uuid4().hex[:9]
try:
    with urllib.request.urlopen(request("api/urls/guest", "POST", {
        "originalUrl": "https://example.com/", "shortCode": code
    }), timeout=15) as response:
        report("guest create", response.status == 201, f"HTTP {response.status}")
        short_url = json.load(response)["shortUrl"]
except urllib.error.HTTPError as error:
    report("guest create", False, f"HTTP {error.code}")
except urllib.error.URLError:
    report("guest create", False, "connection error")

short_parsed = urlparse(urljoin(origin, short_url))
report("guest URL host", short_parsed.hostname == parsed.hostname and short_parsed.path == f"/r/{code}")


try:
    with opener.open(request(short_url), timeout=15) as response:
        report("guest redirect", False, f"HTTP {response.status}")
except urllib.error.HTTPError as error:
    destination = error.headers.get("Location", "")
    report("guest redirect", error.code == 302 and destination == "https://example.com/",
           f"HTTP {error.code}")
except urllib.error.URLError:
    report("guest redirect", False, "connection error")

if os.environ.get("SMOKE_EXPECT_FAIL") == "true":
    report("manual rollback exercise", False, "intentional failure after public checks")
