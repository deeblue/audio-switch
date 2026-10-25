"""Compare endpoint defaults and visible names before and after state-writing probes."""
from __future__ import annotations

import json
from pathlib import Path
import sys
from typing import Any


def compare_state(before: dict[str, Any], after: dict[str, Any]) -> dict[str, Any]:
    old = {item["endpoint_id"]: item for item in before.get("endpoints", [])}
    new = {item["endpoint_id"]: item for item in after.get("endpoints", [])}
    changes: list[dict[str, Any]] = []
    for endpoint_id in sorted(old.keys() | new.keys()):
        if endpoint_id not in old:
            changes.append({"endpoint_id": endpoint_id, "field": "endpoint_presence", "kind": "physical_state_change",
                            "before": "Not enumerated", "after": new[endpoint_id].get("state", "unknown")})
            continue
        if endpoint_id not in new:
            changes.append({"endpoint_id": endpoint_id, "field": "endpoint_presence", "kind": "physical_state_change",
                            "before": old[endpoint_id].get("state", "unknown"), "after": "Not enumerated"})
            continue
        if old[endpoint_id].get("state") != new[endpoint_id].get("state"):
            changes.append({"endpoint_id": endpoint_id, "field": "state", "kind": "physical_state_change",
                            "before": old[endpoint_id].get("state"), "after": new[endpoint_id].get("state")})
        for field in ("friendly_name", "defaults"):
            if old[endpoint_id].get(field) != new[endpoint_id].get(field):
                changes.append({"endpoint_id": endpoint_id, "field": field,
                                "before": old[endpoint_id].get(field), "after": new[endpoint_id].get(field)})
    return {"matches": not changes, "changes": changes}


def main(argv: list[str] | None = None) -> int:
    args = argv if argv is not None else sys.argv[1:]
    if len(args) != 2:
        print("Usage: compare_state.py BEFORE.json AFTER.json", file=sys.stderr)
        return 2
    before = json.loads(Path(args[0]).read_text(encoding="utf-8"))
    after = json.loads(Path(args[1]).read_text(encoding="utf-8"))
    result = compare_state(before, after)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["matches"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
