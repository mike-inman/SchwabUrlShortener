#!/usr/bin/env python3
"""Bounded, opt-in HTTP baseline for a disposable loopback Compose project.

No response body, redirect destination, bearer token, or request URL is logged.
The script never follows redirects and creates only fresh guest test links.
"""

import argparse
import concurrent.futures
import json
import math
import os
import statistics
import time
import urllib.error
import urllib.parse
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


OPENER = urllib.request.build_opener(NoRedirect)


def request(origin, path, expected, payload=None, token=None):
    headers = {"Content-Type": "application/json"} if payload is not None else {}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(origin + path, data=payload, headers=headers)
    start = time.perf_counter()
    try:
        with OPENER.open(req, timeout=5) as response:
            status = response.status
            body = response.read(4096) if payload is not None else b""
    except urllib.error.HTTPError as error:
        status = error.code
        body = b""
    except (urllib.error.URLError, TimeoutError, OSError):
        status = 0
        body = b""
    return status == expected, status, (time.perf_counter() - start) * 1000, body


def percentile(values, percent):
    ordered = sorted(values)
    return ordered[math.ceil(percent / 100 * len(ordered)) - 1]


def run_case(name, count, concurrency, operation):
    start = time.perf_counter()
    with concurrent.futures.ThreadPoolExecutor(max_workers=concurrency) as pool:
        results = list(pool.map(lambda _: operation(), range(count)))
    elapsed = time.perf_counter() - start
    timings = [result[2] for result in results]
    errors = {}
    for success, status, _, _ in results:
        if not success:
            errors[str(status)] = errors.get(str(status), 0) + 1
    return {
        "case": name,
        "requests": count,
        "concurrency": concurrency,
        "elapsed_s": round(elapsed, 3),
        "throughput_rps": round(count / elapsed, 2),
        "latency_ms": {
            "min": round(min(timings), 2),
            "mean": round(statistics.mean(timings), 2),
            "p50": round(percentile(timings, 50), 2),
            "p95": round(percentile(timings, 95), 2),
            "p99": round(percentile(timings, 99), 2),
            "max": round(max(timings), 2),
        },
        "errors": errors,
        "error_rate": round(sum(errors.values()) / count, 4),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--local-test-only", action="store_true", required=True)
    parser.add_argument("--origin", default="http://localhost:18087")
    parser.add_argument("--requests", type=int, default=40)
    parser.add_argument("--concurrency", type=int, default=4)
    parser.add_argument("--managed-code", help="Fresh disposable managed code pre-seeded in this test project")
    args = parser.parse_args()
    parsed = urllib.parse.urlsplit(args.origin)
    if parsed.scheme != "http" or parsed.hostname not in ("localhost", "127.0.0.1") or parsed.path not in ("", "/") or parsed.query or parsed.fragment or parsed.username or parsed.password:
        parser.error("origin must be a loopback HTTP origin without credentials or path")
    if not 1 <= args.requests <= 500 or not 1 <= args.concurrency <= 8:
        parser.error("requests must be 1..500 and concurrency 1..8")
    if args.managed_code and (not args.managed_code.isascii() or not args.managed_code.isalnum() or not 4 <= len(args.managed_code) <= 12):
        parser.error("managed code must be 4..12 ASCII letters or digits")
    origin = args.origin.rstrip("/")
    payload = json.dumps({"originalUrl": "https://example.com/slice7-test"}).encode()
    first = request(origin, "/api/urls/guest", 201, payload)
    if not first[0]:
        raise SystemExit("guest setup failed (status " + str(first[1]) + ")")
    try:
        code = json.loads(first[3])["shortCode"]
    except (ValueError, KeyError):
        raise SystemExit("guest setup response was invalid")
    guest_path = "/r/" + urllib.parse.quote(code, safe="")
    warm = request(origin, guest_path, 302)
    if not warm[0]:
        raise SystemExit("guest warmup failed (status " + str(warm[1]) + ")")
    cases = [
        run_case("guest_create", args.requests, args.concurrency,
                 lambda: request(origin, "/api/urls/guest", 201, payload)),
        run_case("guest_redirect_warm", args.requests, args.concurrency,
                 lambda: request(origin, guest_path, 302)),
    ]
    if args.managed_code:
        managed_path = "/r/" + args.managed_code
        warm = request(origin, managed_path, 302)
        if not warm[0]:
            raise SystemExit("managed warmup failed (status " + str(warm[1]) + ")")
        cases.append(run_case("managed_redirect_warm", args.requests, args.concurrency,
                              lambda: request(origin, managed_path, 302)))
        token = os.environ.get("SCHWAB_LOAD_BEARER_TOKEN")
        if token:
            cases.append(run_case("managed_detail_authenticated", args.requests, args.concurrency,
                                  lambda: request(origin, "/api/urls/managed/" + args.managed_code, 200, token=token)))
            cases.append(run_case("managed_analytics_authenticated", args.requests, args.concurrency,
                                  lambda: request(origin, "/api/urls/managed/" + args.managed_code + "/analytics", 200, token=token)))
    print(json.dumps({"profile": "bounded-loopback", "cache_state": "warm after one redirect",
                      "authenticated_case": bool(args.managed_code and os.environ.get("SCHWAB_LOAD_BEARER_TOKEN")),
                      "cases": cases}, indent=2))
    if any(case["errors"] for case in cases):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
