# SPDX-License-Identifier: GPL-3.0-only
"""Mirrors IniFile.Read and reports what CraneLoader will show.

Written because a generated annotation can bind to nothing and look fine in the
file: the label says "Famished" while the key it names does not exist in that
section, and the manager renders it greyed out with nobody the wiser.
"""
import io, sys, re


def tokenize(text):
    tokens, cur, quoted, ever = [], [], False, False
    for c in text:
        if c == '"':
            quoted = not quoted
            ever = True
            continue
        if not quoted and c in ' \t':
            if cur:
                tokens.append((''.join(cur), ever))
                cur = []
            ever = False
            continue
        cur.append(c)
    if cur:
        tokens.append((''.join(cur), ever))
    return tokens, quoted


def read(path):
    section = ''
    values = {}
    declared = []          # (section, key, type, opts)
    enable = None
    pending = []
    for raw in io.open(path, encoding='utf-8'):
        line = raw.strip()
        if not line:
            continue
        if line[0] in ';#':
            body = line[1:].strip()
            for tag in ('@param', '@enable', '@name', '@description'):
                if not body.lower().startswith(tag):
                    continue
                rest = body[len(tag):]
                if rest and rest[0] not in ' \t:':
                    continue
                rest = rest.lstrip(' \t:').strip()
                explicit = None
                if rest.startswith('['):
                    close = rest.index(']')
                    explicit = rest[1:close]
                    rest = rest[close + 1:].lstrip()
                if tag == '@param':
                    toks, unbalanced = tokenize(rest)
                    if unbalanced:
                        print('  UNBALANCED QUOTES: ' + line[:70])
                    if len(toks) < 2:
                        continue
                    entry = [explicit if explicit else section, toks[0][0], toks[1][0], rest]
                    declared.append(entry)
                    if explicit is None and section == '':
                        pending.append(entry)
                elif tag == '@enable':
                    enable = [explicit if explicit else section, rest]
                    if explicit is None and section == '':
                        pending.append(enable)
                break
            continue
        if line.startswith('['):
            header = line[1:line.index(']')]
            if section == '':
                for entry in pending:
                    if entry[0] == '':
                        entry[0] = header
                pending = []
            section = header
            continue
        if '=' in line:
            key = line.split('=', 1)[0].strip()
            values.setdefault((section, key), line.split('=', 1)[1].strip())
    return values, declared, enable


path = sys.argv[1]
values, declared, enable = read(path)
problems = 0

print('== %s' % path)
print('   keys: %d   declarations: %d' % (len(values), len(declared)))
if enable:
    ok = (enable[0], enable[1]) in values
    print('   @enable [%s]%s -> %s' % (enable[0], enable[1], 'OK' if ok else 'MISSING'))
    if not ok:
        problems += 1
else:
    print('   @enable: none (no checkbox)')

groups = []
for section, key, kind, rest in declared:
    if (section, key) not in values:
        print('   MISSING: [%s]%s declared but no such key in that section' % (section, key))
        problems += 1
    m = re.search(r'group="([^"]*)"', rest)
    g = m.group(1) if m else ''
    if g not in groups:
        groups.append(g)
    if not re.search(r'label="[^"]*"', rest) and 'label=' not in rest:
        print('   NOTE: [%s]%s has no label' % (section, key))

undeclared = [k for k in values if k not in [(s, key) for s, key, _, _ in declared]
              and not (enable and k == (enable[0], enable[1]))]
print('   groups (%d): %s' % (len(groups), ', '.join(g or '(ungrouped)' for g in groups)))
if undeclared:
    print('   not declared (%d): %s' % (len(undeclared),
          ', '.join('[%s]%s' % k for k in undeclared[:8])))
print('   problems: %d' % problems)
sys.exit(1 if problems else 0)
