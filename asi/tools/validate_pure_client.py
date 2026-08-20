# SPDX-License-Identifier: GPL-3.0-only
import re, sys
from pathlib import Path
source = Path(sys.argv[1]).read_text(encoding='utf-8')
code = re.sub(r'/\*.*?\*/|//[^\n]*', '', source, flags=re.S)
for pattern, label in [(r'\bRVA_[A-Z0-9_]+\b', 'RVA'), (r'gamedll_ph_x64_rwdi', 'game DLL'), (r'engine_x64_rwdi', 'engine DLL'), (r'VirtualProtect', 'memory patching'), (r'MinHook|MH_CreateHook|detour', 'detour')]:
    if re.search(pattern, code, re.I): raise SystemExit(f'PURE CLIENT FAIL: {label}')
for token in ('DLTBBridgeGetAPI3', 'register_client', 'manifest.requires',
              'dltbck_request_unregister', 'operation:client.unregister_client',
              'operation:client.report_loaded', 'dltbck_report_loaded',
              'Lua scripting host active',
              'scope->schedule', 'state->read', 'events->subscribe'):
    if token not in code: raise SystemExit(f'PURE CLIENT FAIL: missing {token}')
print('Pure-client validation passed: Crane reaches the game only through ABI 3.')
