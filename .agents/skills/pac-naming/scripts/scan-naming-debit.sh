#!/usr/bin/env bash
# 可选提示列表，给命名审查用 — 不是规则引擎。每条命中由 agent 判断
set -uo pipefail

echo "=== pac-naming hint scan (review your diff) ==="
echo

python3 <<'PY'
import re, os
from collections import defaultdict

ROOTS = ["apps/desktop-avalonia/src", "packages/application", "packages/infrastructure", "packages/core"]
SKIP = {"bin", "obj", "node_modules"}

HINTS = [
    ("stacked Internal/Core private", re.compile(r"\bprivate\s+\w[\w<>,\?\s]*\s+(\w*(?:Internal|Core)(?:Async)?)\s*\(")),
    ("Models.cs in DTO/Pages folder", re.compile(r"/(?:Pages|Dialogs|DTOs)/\w+Models\.cs$")),
    ("Converters/*Converters.cs", re.compile(r"/Converters/\w+Converters\.cs$")),
    ("DialogViewModel in Dialogs/", re.compile(r"/ViewModels/Dialogs/\w+DialogViewModel\.cs$")),
]

findings = defaultdict(list)
for root in ROOTS:
    if not os.path.isdir(root):
        continue
    for dp, dns, fns in os.walk(root):
        dns[:] = [d for d in dns if d not in SKIP]
        for fn in fns:
            path = os.path.join(dp, fn)
            rel = path.replace("\\", "/")
            for label, pat in HINTS:
                if pat.pattern.startswith("/") or "Models" in label or "Converters" in label or "Dialog" in label:
                    if pat.search(rel):
                        findings[label].append(rel)
                    continue
                if not fn.endswith(".cs"):
                    continue
                try:
                    text = open(path, encoding="utf-8").read()
                except OSError:
                    continue
                for m in pat.finditer(text):
                    if "ReloadCore" in m.group(0):
                        continue
                    line = text[: m.start()].count("\n") + 1
                    findings[label].append(f"{rel}:{line}")

for label, items in sorted(findings.items()):
    print(f"{label}: {len(items)}")
    for it in items[:12]:
        print(f"  {it}")
    if len(items) > 12:
        print(f"  ... +{len(items) - 12} more")
    print()
PY

echo "Apply pac-naming debit + peer comparison; do not treat hits as auto-fixes."
