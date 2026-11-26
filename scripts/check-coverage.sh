#!/usr/bin/env bash
# Union line-coverage gate (>=70%) — single implementation shared by CI and the
# local release gate so a coverage failure can never be CI-only.
set -euo pipefail
cd "$(dirname "$0")/.."

python3 - <<'PY'
import glob, sys, xml.etree.ElementTree as ET
lines={}
for f in glob.glob('coverage-results/*/coverage.cobertura.xml'):
    for cls in ET.parse(f).getroot().iter('class'):
        fn=cls.get('filename','')
        if 'PubSoft.' not in fn or fn.startswith('tests'): continue
        for line in cls.iter('line'):
            k=(fn,line.get('number'))
            lines[k]=max(lines.get(k,0),int(line.get('hits',0)))
cov=sum(1 for v in lines.values() if v>0)
pct=cov*100/max(len(lines),1)
print(f"UNION line coverage: {cov}/{len(lines)} = {pct:.1f}%")
sys.exit(0 if pct >= 70 else 1)
PY
