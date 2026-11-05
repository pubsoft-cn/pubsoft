#!/bin/bash
# README ↔ code drift scan: doc-claims not in source (stale) + public
# symbols in source absent from README (code newer than docs).
# Exit 1 on drift. Generated-surface rows are informational.
cd "$(dirname "$0")/.."
python3 - <<'PY'
import os,re,glob,sys
rows=0
for rmd in glob.glob('src/*/*/README.md'):
    d=os.path.dirname(rmd); pkg=os.path.basename(d)
    src=' '.join(open(f,encoding='utf8',errors='ignore').read() for f in glob.glob(d+'/**/*.cs',recursive=True) if '/bin/' not in f and '/obj/' not in f and 'PublicAPI' not in f)
    rm=open(rmd,encoding='utf8').read()
    # doc claims (backticked identifiers that look like API names)
    claimed={c for c in re.findall(r'`(AddPubSoft\w+|UsePubSoft\w+|[A-Z]\w*Options|\w*Provider|\w*Extensions)\(?`?',rm) if len(c)>6}
    stale=sorted(c for c in claimed if c not in src)
    # extension methods + option props in code but not documented
    code_ext=set(re.findall(r'public static \w[\w<>?, ]* (AddPubSoft\w+|UsePubSoft\w+)',src))
    code_opts=set(re.findall(r'public [\w<>?]+ (\w+) \{ get; set; \}',src))
    missing=sorted(x for x in code_ext|code_opts if x not in rm and len(x)>4)
    # doc text mentions (anti-pattern narrative) are not stale claims — require backticked
    if stale or missing:
        rows+=1; print(f'== {pkg}')
        for x in stale: print(f'   STALE(doc-only): {x}')
        for x in missing: print(f'   MISSING(in-code): {x}')
sys.exit(1 if rows else 0)
PY
