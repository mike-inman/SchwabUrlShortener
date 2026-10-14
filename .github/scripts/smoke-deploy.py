"""Exercise readiness, guest creation and redirect without logging response bodies."""
import json
import sys
import urllib.error
import urllib.request
from urllib.parse import urljoin, urlparse
from uuid import uuid4

origin = sys.argv[1].rstrip("/") + "/"
parsed = urlparse(origin)
if parsed.scheme != "https" or not parsed.hostname:
    raise SystemExit("PUBLIC_ORIGIN must be an HTTPS origin")


def request(path, method="GET", payload=None):
    data = json.dumps(payload).encode() if payload is not None else None
    headers = {"Content-Type": "application/json"} if data else {}
    return urllib.request.Request(urljoin(origin, path), data=data, headers=headers, method=method)


with urllib.request.urlopen(request("health/ready"), timeout=15) as response:
    if response.status != 200:
        raise SystemExit("Public readiness failed")

code = "ci" + uuid4().hex[:9]
with urllib.request.urlopen(request("api/urls/guest", "POST", {
    "originalUrl": "https://example.com/", "shortCode": code
}), timeout=15) as response:
    if response.status != 201:
        raise SystemExit("Guest creation failed")
    short_url = json.load(response)["shortUrl"]

if urlparse(urljoin(origin, short_url)).hostname != parsed.hostname:
    raise SystemExit("Guest short URL left the public host")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


opener = urllib.request.build_opener(NoRedirect)
try:
    opener.open(request(short_url), timeout=15)
except urllib.error.HTTPError as error:
    if error.code != 302:
        raise SystemExit(f"Guest redirect returned {error.code}") from error
else:
    raise SystemExit("Guest redirect did not return 302")

print("Public readiness, guest create, and redirect passed.")
