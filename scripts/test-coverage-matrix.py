#!/usr/bin/env python3
"""Map PublicAPI surface → test coverage. Type-level: a public type counts as
covered when its simple name appears in that layer's test sources."""
import re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LAYER_MAP = {
    'core': 'PubSoft.Core.Tests', 'platform': 'PubSoft.Platform.Tests',
    'host': 'PubSoft.Host.Tests', 'data': 'PubSoft.Data.Tests',
    'messaging': 'PubSoft.Platform.Tests', 'security': 'PubSoft.Security.Tests',
    'testing': None, 'analyzers': None,
}

def public_types(apidir: Path):
    types = set()
    for f in apidir.parent.glob('PublicAPI.*.txt'):
        for line in f.read_text().splitlines():
            line = line.strip()
            if not line or line.startswith('#') or line.startswith('*REMOVED*'):
                continue
            # member lines always carry '->', '(', or ' = '; bare 'Ns.Type' is a type line
            is_member = '->' in line or '(' in line or ' = ' in line
            head = re.split(r'\(|->| = ', line)[0].strip().rstrip('?')
            m = re.match(r'^((?:[A-Za-z_][\w.]*\.?)+)', head)
            if not m: continue
            parts = m.group(1).rstrip('.').split('.')
            if is_member and len(parts) > 1:
                parts.pop()
            types.add('.'.join(parts))
    return types

def main():
    report = {}
    for layer_dir in sorted(d for d in (ROOT/'src').iterdir() if d.is_dir()):
        layer = LAYER_MAP.get(layer_dir.name)
        for proj in sorted(layer_dir.iterdir()):
            if not proj.is_dir(): continue
            tdir = proj/'obj'
            types = public_types(proj/'PublicAPI.Shipped.txt')
            if not types: continue
            key = (layer_dir.name, proj.name)
            report[key] = types
    # gather test sources per layer dir name
    test_src = {}
    for tdir in (ROOT/'tests').iterdir():
        if tdir.is_dir():
            text = '\n'.join(p.read_text(errors='ignore') for p in tdir.glob('*.cs'))
            test_src[tdir.name] = text
    all_src = '\n'.join(test_src.values())
    total, covered, gaps, misplaced = 0, 0, {}, {}
    for (layer, proj), types in sorted(report.items()):
        tname = LAYER_MAP[layer]
        if tname is None: continue
        layer_src = test_src.get(tname, '')
        miss_global, miss_layer = [], []
        for t in types:
            simple = t.rsplit('.',1)[-1].split('.')[0].rstrip('<>')
            if simple in all_src:
                covered += 1
                if simple not in layer_src:
                    miss_layer.append(t)
            else:
                miss_global.append(t)
        total += len(types)
        if miss_global: gaps[f'{layer}/{proj}'] = miss_global
        if miss_layer: misplaced[f'{layer}/{proj}'] = miss_layer
    print(f'type-level coverage (global): {covered}/{total} = {covered*100//max(total,1)}%')
    for k, miss in gaps.items():
        print(f'\n## {k} ({len(miss)} uncovered)')
        for t in miss[:30]: print('  ', t.rsplit('.',1)[-1])
        if len(miss) > 30: print(f'   ... +{len(miss)-30} more')
    if misplaced:
        print('\n## covered-but-wrong-layer (test lives in different test project)')
        for k, miss in misplaced.items():
            print(f'  {k}: {len(miss)}')

if __name__ == '__main__': main()
