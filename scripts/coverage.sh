#!/usr/bin/env bash
# Run the full test suite with coverlet collection + report union line coverage.
# Usage: bash scripts/coverage.sh [--open]
set -euo pipefail
OUT="${COVERAGE_DIR:-/tmp/pubsoft-coverage}"
rm -rf "$OUT"
dotnet test PubSoft.sln -c Release --collect:"XPlat Code Coverage" --results-directory "$OUT"
python3 - "$OUT" <<'PY'
import glob, sys, xml.etree.ElementTree as ET
from collections import defaultdict
lines={}
for f in glob.glob(sys.argv[1]+'/*/coverage.cobertura.xml'):
    for cls in ET.parse(f).getroot().iter('class'):
        fn=cls.get('filename','')
        if 'PubSoft.' not in fn or fn.startswith('tests'): continue
        for line in cls.iter('line'):
            k=(fn,line.get('number'))
            lines[k]=max(lines.get(k,0),int(line.get('hits',0)))
cov=sum(1 for v in lines.values() if v>0)
print(f"UNION line coverage: {cov}/{len(lines)} = {cov*100/len(lines):.1f}%")
asm=defaultdict(lambda:[0,0])
for (fn,_),h in lines.items():
    a=fn.split('/')[1]
    asm[a][0]+=(h>0); asm[a][1]+=1
for k,v in sorted(asm.items(),key=lambda x:x[1][0]/max(x[1][1],1))[:15]:
    print(f"  {v[0]*100//v[1]}%  {k}")
PY
