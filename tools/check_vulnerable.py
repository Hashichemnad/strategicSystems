"""Reads `dotnet list package --vulnerable --format json` on stdin; exits 1 on any High/Critical advisory."""
import json
import sys

data = json.load(sys.stdin)
bad = set()
for project in data.get("projects", []):
    for framework in project.get("frameworks", []):
        for kind in ("topLevelPackages", "transitivePackages"):
            for package in framework.get(kind, []):
                for vuln in package.get("vulnerabilities", []):
                    if vuln.get("severity") in ("High", "Critical"):
                        bad.add("{}: {} {} {} {}".format(
                            project["path"], package["id"], package["resolvedVersion"], vuln["severity"], vuln.get("advisoryurl", "")))
if bad:
    print("\n".join(sorted(bad)))
    sys.exit(1)
print("No High/Critical NuGet vulnerabilities.")
