# Конвейер Протектора И Флаги

[English version](protector-pipeline.md)

Этот документ раскрывает короткую диаграмму из README. Он описывает, как поведение протектора меняется в зависимости от CLI-флагов.

## Общий Flow

```mermaid
flowchart TD
    A["CLI: Loaders.exe"] --> B["Нормализовать пути"]
    B --> C{"Source valid?\nРовно один .csproj?"}
    C -- "нет" --> X["Ошибка path/project validation"]
    C -- "да" --> D["Resolve output folder"]
    D --> E{"Requested output exists?"}
    E -- "да" --> F["Использовать conflict-safe suffix"]
    E -- "нет" --> G["Использовать requested output"]
    F --> H["Скопировать source tree"]
    G --> H
    H --> I["Загрузить project metadata"]
    I --> J["Project-wide string resource pass"]
    J --> K["Перезагрузить patched project"]
    K --> L["Optional assignment rewrite"]
    L --> M["Optional symbol renaming"]
    M --> N["Direct Roslyn Emit"]
    N --> O{"Console executable?"}
    O -- "да" --> P["Smoke-run --help"]
    O -- "нет" --> Q["Finish"]
    P --> Q
```

## Флаг Assignment Rewrite

`--out-assignment-methods` выполняется до symbol renaming. Если флаг активен, последующий member/method renaming пропускает generated out-assignment helpers.

```mermaid
flowchart LR
    A["После string resource pass"] --> B{"--out-assignment-methods?"}
    B -- "нет" --> C["Оставить local assignments без изменений"]
    B -- "да" --> D["Найти safe local initializers"]
    D --> E["Сгенерировать helper method"]
    E --> F["Переписать declaration/call через out parameter"]
    C --> G["Перейти к symbol rename decision"]
    F --> G
```

## Режимы Symbol Rename

```mermaid
flowchart TD
    A["После string/resource и optional assignment rewrite"] --> B{"--skip-symbol-renaming?"}
    B -- "да" --> C["Пропустить overload injection, class rename,\nmethod rename, extended rename"]
    C --> Z["Compile"]
    B -- "нет" --> D["Inject method overloads"]
    D --> E{"--rename-extended-symbols?"}
    E -- "нет" --> F["Collect class map"]
    F --> G["Rename classes"]
    G --> H["Fix constructors"]
    H --> I["Collect method entries"]
    I --> J["Rename methods"]
    J --> Z
    E -- "да" --> K["Semantic namespace/type rename"]
    K --> L["Fix constructors"]
    L --> M["Semantic member/parameter/local rename"]
    M --> Z
```

## Поведение Output Folder

Протектор не удаляет предыдущие output roots. Если requested output уже существует, он добавляет numeric suffix и сообщает фактический путь.

```mermaid
flowchart LR
    A["Requested output path"] --> B{"Exists?"}
    B -- "нет" --> C["Использовать path as-is"]
    B -- "да" --> D["Try _01"]
    D --> E{"Exists?"}
    E -- "да" --> F["Try next suffix"]
    F --> E
    E -- "нет" --> G["Use suffixed path"]
```

## Матрица Флагов

| Флаги | String resource | Assignment rewrite | Symbol rename | Notes |
| --- | --- | --- | --- | --- |
| нет | Да | Нет | Default class/method flow | Исходный широкий obfuscation path, но symbol rename может зависеть от особенностей проекта. |
| `--out-assignment-methods` | Да | Да | Default class/method flow | Generated helpers пропускаются последующим method/member rename. |
| `--rename-extended-symbols` | Да | Optional | Extended semantic flow | Более широкий scope; сильно зависит от полных metadata references. |
| `--skip-symbol-renaming` | Да | Optional | Нет | Лучший режим для отдельной проверки project-wide string resource behavior. |
| `--BeLeo` | Да | Optional | Зависит от других флагов | Меняет только name provider. |
| `--string-obfuscation-strategy <STRATEGY>` | Да | Optional | Зависит от других флагов | Принудительно выбирает один string codec вместо automatic per-literal selection. |

## Сравнение Rename Scope

| Scope | Default mode | `--rename-extended-symbols` | `--skip-symbol-renaming` |
| --- | --- | --- | --- |
| Namespaces | Нет | Да | Нет |
| Classes | Да | Да | Нет |
| Structs/interfaces/enums/delegates | Нет | Да | Нет |
| Methods | Да | Да | Нет |
| Properties/fields/events | Нет | Да | Нет |
| Parameters/locals | Нет | Да | Нет |
| Constructors/accessors/operators | Не переименуются напрямую | Не переименуются напрямую | Нет |
| Generated/designer files | Пропускаются | Пропускаются | Пропускаются |

## Известная Граница

String-resource pass независим от symbol renaming. Большие проекты могут успешно компилироваться с `--skip-symbol-renaming`, но при этом выявлять уже существующие edge cases в default или extended rename modes. Используйте `--skip-symbol-renaming` как validation/isolation tool, а не как замену hardening для rename passes.
