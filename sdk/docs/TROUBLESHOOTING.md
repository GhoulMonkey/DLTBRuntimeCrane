# Troubleshooting

- **Script is visible but does not run:** new files are disabled. Enable it in Crane Manager.
- **`CRANE_WRITES_DISABLED`:** the script attempted mutation while the master writes switch
  was off. Enable it only after reviewing the script.
- **`DLTB_STALE_SUBJECT`:** resolve the required subject again after the session is playable.
- **`DLTB_UNKNOWN_PATH`:** check spelling and the minimum Bridge requirement. Use
  `bridge.list(prefix)` and `bridge.describe(path)` against the installed runtime.
- **Lease claim refused:** another script/client may own the exclusive path. Do not loop and
  retry every frame; report the refusal and stand down.
- **Instruction budget exceeded:** remove unbounded work, large loops or recursively triggered
  callbacks. Split work across events only when the semantics permit it.
- **Manager shows failed:** inspect the shared DLTBRuntimeBridge log for the exact Lua error.
- **Validator warns syntax was not checked:** `tools/crane-lua-check.exe` is absent.
  The packaged SDK ships it built; a source checkout builds it with
  `tools-source/build-checker.sh`. A system Lua 5.4 `luac` on `PATH` serves as a
  fallback. Metadata and parameter validation run without either.
