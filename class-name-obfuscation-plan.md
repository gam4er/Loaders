# Class name obfuscation gaps (ChromiumBookmarksCommand)

## Observed issue
The obfuscated output in `ChromiumBookmarksCommand_obf.cs` shows that usages of the `Bookmark` and `ChromiumBookmarksDTO` types inside type arguments, object creations, and casts remain unobfuscated (e.g., `new List<Bookmark>()`, `new Bookmark(...)`, `(ChromiumBookmarksDTO)result`), even though the class declarations themselves were renamed to `O_1BAC4007` and `O_9EBCBFCE`.

## Goal I inferred
Ensure type usages are consistently rewritten to their obfuscated identifiers wherever the types appear, not just at class declarations or constructors. The pipeline should not leave any references (generic arguments, object creation types, casts, attributes, or inferred `var` initializers) using the original class names once obfuscation finishes.

## Context in the codebase
- Class mapping is built in `ClassCollectionRewriter` and consumed by `ClassObfuscationRewriter` during `ApplyClassObfuscation` in `InMemCompiler`.【F:Loaders/InMemCompiler.cs†L48-L89】【F:Loaders/Obfuscation/Rewriters/ClassCollectionRewriter.cs†L13-L24】
- `ClassObfuscationRewriter` currently renames class declarations and a variety of syntax nodes, relying on `MapType` to rewrite type syntax. Reserved-context checks short-circuit replacements in some parent nodes (arguments, assignments, properties, fields, and variable declarators).【F:Loaders/Obfuscation/Rewriters/ClassObfuscationRewriter.cs†L16-L119】【F:Loaders/Obfuscation/Rewriters/ClassObfuscationRewriter.cs†L189-L230】

## Decomposition of the fix
1. **Reproduce and trace the missing replacements**
   - Run `ClassObfuscationRewriter` on a minimal snippet containing the failing patterns (`List<Bookmark>`, `new Bookmark(...)`, `(ChromiumBookmarksDTO)result`) with the existing map to confirm which syntax nodes are skipped. Instrument `MapType`/`VisitObjectCreationExpression` logging for the specific identifiers to see why they are not rewritten.

2. **Audit reserved-context filtering**
   - Evaluate whether `IsInReservedContext` incorrectly treats type usages (e.g., type arguments inside `var` initializers or casts) as reserved because their parent is a `VariableDeclarator` or `ArgumentSyntax`. Consider narrowing or removing these guards so identifiers within type syntax still get renamed.

3. **Extend type mapping coverage**
   - Ensure `MapType` handles all type forms that can contain class identifiers, including `AliasQualifiedNameSyntax`, `NullableTypeSyntax`, `ArrayTypeSyntax`, `PointerTypeSyntax`, and constraint clauses. Add unit-style regression snippets to validate transformations for generics, object creations, casts, and attribute type references.

4. **Verify rename after the rewrite**
   - Confirm that the subsequent Roslyn-based `ClassRenamer.RenameClasses` stage still finds the symbols after the source-level rewrite. If the earlier pass already renamed declarations, consider either skipping the `Renamer` phase or feeding it the pre-rewrite syntax trees so it does not miss symbols by looking for the original names.

5. **Retest obfuscation output**
   - Run the full pipeline and check that `ChromiumBookmarksCommand_obf.cs` now replaces all `Bookmark`/`ChromiumBookmarksDTO` usages with their obfuscated counterparts in generics, object creations, casts, and return types. Update `obf.list` logging to capture the before/after evidence for these identifiers.

## What to ask for if more data is needed
- Current `obf.list`/`errors.list` snippets around `Bookmark`/`ChromiumBookmarksDTO` to verify which visitor logs fired.
- The exact Roslyn tree (dump of the relevant syntax nodes) before obfuscation for the failing file to match against the reserved-context logic.
