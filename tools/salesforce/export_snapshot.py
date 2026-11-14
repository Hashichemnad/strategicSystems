#!/usr/bin/env python3
"""Salesforce export -> JSON snapshot and field-coverage report (release 1.0.2, ADR 0215).

Usage (run isolated, the xlsx files are data):
  python3 -I tools/salesforce/export_snapshot.py snapshot   # docs/salesforce/*.xlsx -> docs/salesforce/adof-export.json
  python3 -I tools/salesforce/export_snapshot.py coverage   # pack + snapshot -> docs/salesforce/field-coverage.md (exit 1 on gaps)

The snapshot is the single source for the fake org's describes (MockSalesforceOrg) and for ShippedPackCoversExportTests.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
SF = ROOT / "docs" / "salesforce"
SNAPSHOT = SF / "adof-export.json"
PACK = ROOT / "src" / "Platform" / "Strategy.Platform.Integration" / "Sync" / "Packs" / "adof-salesforce"
COVERAGE = SF / "field-coverage.md"


def describe_type(sf_type: str) -> tuple[str, str | None]:
    """Salesforce setup type (as exported) -> describe type and referenced object."""
    t = sf_type.strip()
    m = re.match(r"(Lookup|Master-Detail)\(([^)]*)\)", t)
    if m:
        target = m.group(2).split(",")[0].strip() or None
        return "reference", target
    if t.startswith("Formula"):
        inner = re.search(r"\(([^)]*)\)", t)
        kind = (inner.group(1) if inner else "Text").strip()
        return {"Number": "double", "Percent": "percent", "Currency": "currency", "Date": "date", "Date/Time": "datetime",
                "Checkbox": "boolean"}.get(kind, "string"), None
    if t.startswith("Roll-Up Summary"):
        return ("date" if "Date" in t else "double"), None
    for prefix, kind in [("Picklist (Multi-Select)", "multipicklist"), ("Picklist", "picklist"), ("Checkbox", "boolean"),
                         ("Date/Time", "datetime"), ("Date", "date"), ("Long Text Area", "textarea"), ("Rich Text Area", "textarea"),
                         ("Text Area", "textarea"), ("Text", "string"), ("Auto Number", "string"), ("Currency", "currency"),
                         ("Percent", "percent"), ("Number", "double"), ("Email", "email"), ("Phone", "phone"), ("Record Type", "reference")]:
        if t.startswith(prefix):
            return kind, ("RecordType" if prefix == "Record Type" else None)
    return "string", None


def length(sf_type: str) -> int | None:
    m = re.search(r"\((\d+)(?:,\s*\d+)?\)", sf_type)
    return int(m.group(1)) if m and not sf_type.startswith(("Number", "Currency", "Percent")) else None


def snapshot() -> None:
    import openpyxl  # only needed for this step

    export = openpyxl.load_workbook(SF / "Object Data.xlsx", read_only=True)
    review = openpyxl.load_workbook(SF / "Salesforce-mapping-review.xlsx", read_only=True)
    status = {(r[1], r[2]): r[7] for r in list(review["Fields"].iter_rows(values_only=True))[1:] if r[1]}
    labels = {}
    systems = {}
    for row in list(export["Objects"].iter_rows(values_only=True))[1:]:
        if row[0]:
            labels[row[0]] = row[1]
            systems[row[0]] = row[2]
    objects = {name: {"label": labels[name], "system": systems[name], "fields": {}} for name in sorted(labels)}
    by_label = {label: name for name, label in labels.items()}
    for row in list(export["Fields"].iter_rows(values_only=True))[1:]:
        obj, api, label, sf_type, nillable = row[:5]
        if not obj or obj not in objects:
            continue
        kind, ref = describe_type(sf_type or "")
        if ref:
            # The export names the referenced object by its label (Lookup(Audit Log)); resolve to the API name. "Stage" is the old label of
            # PMS_Work_Plan__c (owner decision: Task__c.relatedProject__c points at the work plan).
            ref = by_label.get(ref, {"User Record Access": "UserRecordAccess", "Stage": "PMS_Work_Plan__c"}.get(ref, ref))
        state = status.get((obj, api), "Not mapped")
        category = {"System field": "system", "Calculated in Salesforce": "calculated"}.get(state, "data")
        field = {"label": label, "sfType": sf_type, "type": kind, "category": category}
        if ref:
            field["referenceTo"] = ref
        if length(sf_type or ""):
            field["length"] = length(sf_type)
        if sf_type and "Unique" in sf_type:
            field["unique"] = True
        objects[obj]["fields"][api] = field
    for row in list(export["Picklist Value"].iter_rows(values_only=True))[1:]:
        obj, api, value, label, active = row[:5]
        if obj in objects and api in objects[obj]["fields"]:
            objects[obj]["fields"][api].setdefault("values", []).append({"value": value, "label": label, "active": bool(active)})
    doc = {
        "source": "Owner's Salesforce export of 14 Nov 2026 (docs/salesforce/Object Data.xlsx) with the field categories of "
                  "Salesforce-mapping-review.xlsx. Field shapes and picklist values only, no record data.",
        "objects": objects,
    }
    SNAPSHOT.write_text(json.dumps(doc, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"{SNAPSHOT.relative_to(ROOT)}: {len(objects)} objects, {sum(len(o['fields']) for o in objects.values())} fields")


def pack_objects() -> list[dict]:
    out = []
    for path in sorted(PACK.glob("*.json")):
        out.extend(json.loads(path.read_text(encoding="utf-8")).get("objects", []))
    return out


def coverage() -> int:
    doc = json.loads(SNAPSHOT.read_text(encoding="utf-8"))
    objects = doc["objects"]
    mapped: dict[str, dict[str, list[str]]] = {}
    ignored: dict[str, dict[str, str]] = {}
    problems = []
    for o in pack_objects():
        src = o["sourceObject"]
        if o.get("kind") not in ("record",):
            continue
        if src not in objects:
            problems.append(f"pack object `{o['key']}` uses `{src}`, which is not in the export")
            continue
        for f in o.get("fields", []):
            name = f["source"].split(".")[0]
            if f["source"].endswith(".DeveloperName") and name == "RecordType":
                name = "RecordTypeId"
            if name not in objects[src]["fields"]:
                problems.append(f"`{o['key']}` maps `{src}.{f['source']}`, which does not exist")
            mapped.setdefault(src, {}).setdefault(name, []).append(f"{o['targetType']}.{f['target']}")
        for item in o.get("ignore", []):
            name, _, reason = (item.partition(":") if isinstance(item, str) else (item["field"], ":", item.get("reason", "")))
            ignored.setdefault(src, {})[name.strip()] = reason.strip()
    lines = ["# Salesforce field coverage (ADoF pack)", "",
             "Generated by `python3 -I tools/salesforce/export_snapshot.py coverage` from `adof-export.json` and the shipped pack. "
             "Owner decision (ADR 0215): every Salesforce data field is held here. *system* = handled by the sync (identity, dates, "
             "owner); *calculated* = formula / roll-up, recalculated by this system; *ignored* = listed in the pack with its reason.", ""]
    gaps = 0
    for name, o in objects.items():
        lines += [f"## {name} — {o['label']} ({o['system']})", "", "| Field | Type | Handling |", "|---|---|---|"]
        for api, f in o["fields"].items():
            if f["category"] == "system":
                handling = "system"
            elif f["category"] == "calculated":
                handling = "calculated here"
                if api in mapped.get(name, {}):
                    handling += " · source value kept in " + ", ".join(mapped[name][api])
            elif api in mapped.get(name, {}):
                handling = ", ".join(f"`{t}`" for t in mapped[name][api])
            elif api in ignored.get(name, {}):
                handling = "ignored: " + (ignored[name][api] or "(no reason)")
                if not ignored[name][api]:
                    gaps += 1
            else:
                handling = "**NOT HELD**"
                gaps += 1
            lines.append(f"| `{api}` | {f['sfType']} | {handling} |")
        lines.append("")
    if problems:
        lines += ["## Pack problems", ""] + [f"- {p}" for p in problems] + [""]
    COVERAGE.write_text("\n".join(lines), encoding="utf-8")
    print(f"{COVERAGE.relative_to(ROOT)}: {gaps} data fields not held, {len(problems)} pack problems")
    return 1 if gaps or problems else 0


if __name__ == "__main__":
    step = sys.argv[1] if len(sys.argv) > 1 else "coverage"
    if step == "snapshot":
        snapshot()
        sys.exit(0)
    sys.exit(coverage())
