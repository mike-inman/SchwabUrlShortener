import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    audit = json.load(source)

findings = set()
for project in audit.get("projects", []):
    for framework in project.get("frameworks", []):
        for category in ("topLevelPackages", "transitivePackages"):
            for package in framework.get(category, []):
                for vulnerability in package.get("vulnerabilities", []):
                    severity = str(vulnerability.get("severity", "")).lower()
                    if severity in ("high", "critical", "3", "4"):
                        findings.add((package.get("id"), package.get("resolvedVersion"), severity,
                                      vulnerability.get("advisoryurl")))

for name, version, severity, advisory in sorted(findings):
    print(f"{severity}: {name} {version} {advisory}")
print(f"Unique High/Critical NuGet advisories: {len(findings)}")
sys.exit(1 if findings else 0)
