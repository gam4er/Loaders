# Protector Pipeline And Flags

[Russian version](protector-pipeline.ru.md)

This document expands the short pipeline shown in the README. It focuses on how the protector changes behavior based on CLI flags.

## Top-Level Flow

```mermaid
flowchart TD
    A["CLI: Loaders.exe"] --> B["Normalize paths"]
    B --> C{"Source valid?\nExactly one .csproj?"}
    C -- "no" --> X["Fail with path/project error"]
    C -- "yes" --> D["Resolve output folder"]
    D --> E{"Requested output exists?"}
    E -- "yes" --> F["Use conflict-safe suffix"]
    E -- "no" --> G["Use requested output"]
    F --> H["Copy source tree"]
    G --> H
    H --> I["Load project metadata"]
    I --> J["Project-wide string resource pass"]
    J --> K["Reload patched project"]
    K --> L["Optional assignment rewrite"]
    L --> M["Optional symbol renaming"]
    M --> N["Direct Roslyn Emit"]
    N --> O{"Console executable?"}
    O -- "yes" --> P["Smoke-run --help"]
    O -- "no" --> Q["Finish"]
    P --> Q
```

## Assignment Rewrite Flag

`--out-assignment-methods` runs before symbol renaming. Later member/method renaming skips generated out-assignment helpers when this flag is active.

```mermaid
flowchart LR
    A["After string resource pass"] --> B{"--out-assignment-methods?"}
    B -- "no" --> C["Leave local assignments unchanged"]
    B -- "yes" --> D["Find safe local initializers"]
    D --> E["Generate helper method"]
    E --> F["Rewrite declaration/call with out parameter"]
    C --> G["Continue to symbol rename decision"]
    F --> G
```

## Symbol Rename Modes

```mermaid
flowchart TD
    A["After string/resource and optional assignment rewrite"] --> B{"--skip-symbol-renaming?"}
    B -- "yes" --> C["Skip overload injection, class rename,\nmethod rename, extended rename"]
    C --> Z["Compile"]
    B -- "no" --> D["Inject method overloads"]
    D --> E{"--rename-extended-symbols?"}
    E -- "no" --> F["Collect class map"]
    F --> G["Rename classes"]
    G --> H["Fix constructors"]
    H --> I["Collect method entries"]
    I --> J["Rename methods"]
    J --> Z
    E -- "yes" --> K["Semantic namespace/type rename"]
    K --> L["Fix constructors"]
    L --> M["Semantic member/parameter/local rename"]
    M --> Z
```

## Output Folder Behavior

The protector does not delete previous output roots. If the requested output exists, it appends a numeric suffix and reports the actual path.

```mermaid
flowchart LR
    A["Requested output path"] --> B{"Exists?"}
    B -- "no" --> C["Use path as-is"]
    B -- "yes" --> D["Try _01"]
    D --> E{"Exists?"}
    E -- "yes" --> F["Try next suffix"]
    F --> E
    E -- "no" --> G["Use suffixed path"]
```

## Flag Matrix

| Flags | String resource | Assignment rewrite | Symbol rename | Notes |
| --- | --- | --- | --- | --- |
| none | Yes | No | Default class/method flow | Original broad obfuscation path, but symbol rename can be project-sensitive. |
| `--out-assignment-methods` | Yes | Yes | Default class/method flow | Generated helpers are skipped by later method/member rename. |
| `--rename-extended-symbols` | Yes | Optional | Extended semantic flow | Wider scope; depends heavily on complete metadata references. |
| `--skip-symbol-renaming` | Yes | Optional | No | Best for validating project-wide string resource behavior separately. |
| `--BeLeo` | Yes | Optional | Depends on other flags | Changes name provider only. |
| `--string-obfuscation-strategy <STRATEGY>` | Yes | Optional | Depends on other flags | Forces one string codec instead of automatic per-literal selection. |

## Rename Scope Comparison

| Scope | Default mode | `--rename-extended-symbols` | `--skip-symbol-renaming` |
| --- | --- | --- | --- |
| Namespaces | No | Yes | No |
| Classes | Yes | Yes | No |
| Structs/interfaces/enums/delegates | No | Yes | No |
| Methods | Yes | Yes | No |
| Properties/fields/events | No | Yes | No |
| Parameters/locals | No | Yes | No |
| Constructors/accessors/operators | Not renamed directly | Not renamed directly | No |
| Generated/designer files | Skipped | Skipped | Skipped |

## Known Boundary

The string-resource pass is independent from symbol renaming. Large projects can compile successfully with `--skip-symbol-renaming` while still exposing existing rename edge cases in default or extended rename modes. Treat `--skip-symbol-renaming` as a validation and isolation tool, not as a replacement for rename hardening.
