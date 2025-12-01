# Agent instructions for Loaders

- Use `rg` instead of `ls -R` or `grep -R` when searching the repository.
- Keep logging noise minimal; prefer concise debug messages that explain why a decision was made.
- When adjusting obfuscation rewrites, ensure type usages and declarations stay consistent, and document any mapping assumptions directly in code comments if non-obvious.
- Update `Compilation failed errors.list` and `obf.list` only when running the pipeline; do not hand-edit their contents.
- Add meaningful tests or reproduction snippets to commit messages or PR descriptions if you fix an obfuscation gap.
