# Loaders

Loaders is a .NET Framework 4.8 obfuscation pipeline: it copies a C# project to a working directory, applies obfuscation steps, then compiles and runs the obfuscated binary.

## Core process
- **PrepareOutputProject**: copy the source project into an isolated folder.
- **LoadProject**: parse the csproj to collect C# files and assembly references.
- **String obfuscation**: remove comments and rewrite string literals.
- **Overload injection**: add harmless method overloads to increase control-flow noise.
- **Semantic class renaming**: Roslyn symbols safely update all references across files.
- **Syntactic constructor fix-up**: adjust call sites that semantic renaming may miss.
- **Semantic method renaming**: Roslyn symbols, using pre-filtered entries and overload disambiguation.
- **Compile**: build and run the obfuscated assembly in memory.

## Semantic vs syntactic renaming
- **Semantic**: operates on Roslyn `ISymbol`, updates usages (type refs, parameters, object creations) reliably. Requires accurate `MetadataReference`.
- **Syntactic**: operates on syntax trees, faster but unaware of symbol binding; can miss or break edge cases.

## Key components
- **`InMemCompiler`**: orchestrates the pipeline.
- **`ClassRenamer`**: semantic class renamer via Roslyn.
- **`MethodRenamer`**: semantic method renamer driven by `MethodRenameEntry`.
- **`MethodCollectionRewriter`**: collects eligible methods, captures overload info.
- **`SimpleConstructorRenameService`**: focused syntactic constructor pass.
- **`StringLiteralObfuscationService`** and rewriters: string processing and comment removal.
- **`Loaders.GAC`**: resolves assembly references from GAC.

## Best practices
- Run semantic renaming (classes/methods) before syntactic fixes.
- Provide complete `MetadataReference` to the Roslyn workspace (parsed from csproj + common framework libs).
- Centralize exclusion rules (override, interface implementations, extern/DllImport, `Dispose`, serialization callbacks, `Main`, `object` methods) in collection.
- Use structured entries (class, method, parameter count, new name) to avoid collisions and handle overloads.
- Persist changes to disk after successful semantic updates and a clean build.

## Build & run
- Set `SourceFolder`, `OutputFolder`, and `ProjectFileName` in `InMemCompiler`.
- Run the app: it copies, obfuscates, compiles, and executes the binary.