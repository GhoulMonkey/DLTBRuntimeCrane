# Packaging a CRANE mod

Run:

```powershell
.\tools\package.ps1 .\MyMod.lua -Version 1.0.0
```

The tool validates first and creates:

```text
MyMod-1.0.0.zip
  ph_ft/work/bin/x64/scripts/MyMod.lua
```

That is the complete runtime package for a one-file script. CRANE discovers the deployed
file and Crane Manager shows it disabled until the user enables it.

Do not package:

- `DLTBRuntimeCrane.asi`, `CraneManager.exe` or any CRANE config/status/manifest file;
- `DLTBRuntimeBridge.asi` or native Bridge headers;
- an ASI loader;
- SDK editor definitions, docs or validation tools;
- another mod's scripts.

On Nexus, declare CRANE and the minimum DLTBRuntimeBridge release as requirements. State
whether the mod needs the CRANE writes switch, what it changes, how it cleans up, and which
settings it exposes.

