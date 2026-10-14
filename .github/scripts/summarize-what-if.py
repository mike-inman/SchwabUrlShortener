"""Emit only change types and ARM resource IDs; never emit resource properties."""
import json
import sys

with open(sys.argv[1], encoding="utf-8") as source:
    preview = json.load(source)

changes = []


def walk(value):
    if isinstance(value, dict):
        kind = value.get("changeType")
        resource_id = value.get("resourceId")
        if kind and resource_id and str(resource_id).startswith("/subscriptions/"):
            changes.append((kind, resource_id))
        for child in value.values():
            walk(child)
    elif isinstance(value, list):
        for child in value:
            walk(child)


walk(preview)
if not changes:
    if isinstance(preview, dict) and preview.get("changes") == []:
        print("### Azure what-if\n\nNo infrastructure changes reported.")
        sys.exit(0)
    print("What-if output had no parseable resource changes; stopping before provision.", file=sys.stderr)
    sys.exit(1)
print("### Azure what-if (resource IDs only)\n")
for kind, resource_id in sorted(set(changes)):
    print(f"- {kind}: `{resource_id}`")
