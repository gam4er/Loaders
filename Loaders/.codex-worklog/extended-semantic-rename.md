# Extended Semantic Rename / BeLeo Worklog

## 2026-07-30

- Started implementation from accepted plan.
- Current baseline inspected: `InMemCompiler` has `--out-assignment-methods`, project metadata resolver, and out-assignment helpers already generated with obfuscated names.
- Next: add name-provider abstraction, BeLeo embedded resource loading, extended semantic rename service, CLI wiring, README update, and validation.
- Added shared name-provider wiring, BeLeo provider over embedded plain-text `2600-0.txt`, and the first version of `ExtendedSymbolRenameService`.
- Wired `--rename-extended-symbols` and `--BeLeo` through `InMemCompiler`; class maps now write to the output folder.
- Built Release x64 successfully. CLI help shows `--out-assignment-methods`, `--rename-extended-symbols`, and `--BeLeo`.
- Synthetic fixture with full flag combination compiles; BeLeo names appear for extended symbols and out-assignment helpers, while override/DllImport/interface contract methods are skipped.
- README updated with the actual pipeline diagram and a Codecepticon scope comparison.
- Rebuilt after defensive extended-rename skip handling; Release x64 still succeeds.
- Seatbelt baseline output path `E:\Documents\GitHub\obf\Seatbelt` is currently locked by another process, so acceptance was run in fresh sibling outputs.
- Seatbelt `--out-assignment-methods` succeeded in `E:\Documents\GitHub\obf\Seatbelt_codex`; Seatbelt printed its usual banner and `ERROR: Error running command "--help"` but Loaders exited 0 and emitted `Output.exe`.
- Seatbelt `--out-assignment-methods --BeLeo` succeeded in `E:\Documents\GitHub\obf\Seatbelt_codex_beleo`; grep confirmed helper calls/methods use War and Peace names and no `Microsoft[0-9A-F]{13}` identifiers remained in `.cs` files.
- Diagnostic Seatbelt `--rename-extended-symbols` run in `E:\Documents\GitHub\obf\Seatbelt_codex_extended` timed out after 5 minutes before compile; the process was stopped. The partial output reached at least namespace/type work and wrote `class-map.csv`, so remaining issue is extended-mode throughput on large projects.
