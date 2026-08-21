#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
"""Palette brushes must be referenced with DynamicResource, not StaticResource.

A StaticResource inside a Style setter or a ControlTemplate resolves once, when
the style is sealed, and the resolved brush is cached for the life of the
element. Theme switching replaces and then mutates the brushes in
Application.Resources, so a cached reference never changes colour again.

Neither a compile nor a runtime check on the resource dictionary can see this,
because the dictionary does hold the new colours; it is the elements that are
not reading them. A search over the source can see it.
"""
import os
import re
import sys

PALETTE = [
    'Ink', 'InkMuted', 'InkFaint', 'Rule', 'RuleStrong',
    'Surface', 'SurfaceAlt', 'SurfaceRaised', 'SurfaceHover', 'SurfacePress',
    'SurfaceInput', 'SurfaceAlert',
    'Accent', 'AccentDim', 'AccentWarm', 'AccentWarmDim', 'OnAccent',
    'StateGood', 'StateWarn', 'StateBad', 'StateIdle',
]

FILES = ['App.xaml', 'MainWindow.xaml']

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
pattern = re.compile(r'\{StaticResource (' + '|'.join(PALETTE) + r')\}')

bad = []
checked = 0
for name in FILES:
    path = os.path.join(root, name)
    if not os.path.isfile(path):
        print('validate_theme_refs: missing ' + name, file=sys.stderr)
        sys.exit(1)
    with open(path, encoding='utf-8') as handle:
        for number, line in enumerate(handle, 1):
            for match in pattern.finditer(line):
                bad.append('%s:%d: %s must be DynamicResource' %
                           (name, number, match.group(1)))
    checked += 1

if bad:
    print('Theme reference check FAILED:', file=sys.stderr)
    for entry in bad:
        print('  ' + entry, file=sys.stderr)
    print('\nA palette brush read through StaticResource is frozen at the colour '
          'it had when\nthe style was sealed, and will not follow a theme change.',
          file=sys.stderr)
    sys.exit(1)

print('Theme reference check passed: %d palette key(s) across %d file(s) are '
      'all DynamicResource.' % (len(PALETTE), checked))
