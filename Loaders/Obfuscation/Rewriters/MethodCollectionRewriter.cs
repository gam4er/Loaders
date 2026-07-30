using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Loaders.Obfuscation.Utilities;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Collects method declarations for each class and builds a mapping between
    /// original method identifiers and obfuscated names. Constructors, operators,
    /// property/event accessors and overrides are skipped.
    /// Additionally skips methods of derived classes, extern/PInvoke, serialization callbacks,
    /// common System.Object contract methods, and Dispose implementations to avoid breaking contracts.
    /// Also skips entry-point `Main` methods.
    /// </summary>
    public sealed class MethodCollectionRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _methodNameMap = new();
        private readonly List<MethodRenameEntry> _entries = new();
        private readonly bool _includeDerivedPrivateStaticMethods;
        private readonly bool _skipOutAssignmentHelpers;

        public MethodCollectionRewriter(
            bool includeDerivedPrivateStaticMethods = false,
            bool skipOutAssignmentHelpers = false)
        {
            _includeDerivedPrivateStaticMethods = includeDerivedPrivateStaticMethods;
            _skipOutAssignmentHelpers = skipOutAssignmentHelpers;
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var hasBaseType = node.BaseList != null && node.BaseList.Types.Count > 0;

            // Skip entire class if it derives from another type to avoid inheritance contract issues
            if (hasBaseType && !_includeDerivedPrivateStaticMethods)
            {
                return base.VisitClassDeclaration(node);
            }


            // Walk methods in this class and collect rename map
            foreach (var method in node.Members.OfType<MethodDeclarationSyntax>())
            {
                if (hasBaseType && !IsPrivateStaticMethod(method))
                {
                    continue;
                }

                if (_skipOutAssignmentHelpers && IsOutAssignmentHelper(method))
                {
                    continue;
                }

                if (ShouldSkip(method))
                {
                    continue;
                }

                var originalName = method.Identifier.Text;
                var obfuscatedName = ObfuscatedNameGenerator.Generate(originalName);

                // Key by class-qualified name to avoid cross-type clashes
                var className = node.Identifier.Text;
                var mapKey = $"{className}.{originalName}";
                _methodNameMap[mapKey] = obfuscatedName;

                // Also capture structured entry with parameter count for overload disambiguation
                var parameterCount = method.ParameterList?.Parameters.Count ?? 0;
                _entries.Add(new MethodRenameEntry(className, originalName, parameterCount, obfuscatedName));
            }

            return base.VisitClassDeclaration(node);
        }

        private static bool ShouldSkip(MethodDeclarationSyntax method)
        {
            // Skip overrides, extern, operators and special-name accessors
            if (//method.Modifiers.Any(SyntaxKind.OverrideKeyword) ||
                method.Modifiers.Any(SyntaxKind.ExternKeyword))
            {
                return true;
            }

            var name = method.Identifier.Text;
            if (name.StartsWith("get_") || name.StartsWith("set_") ||
                name.StartsWith("add_") || name.StartsWith("remove_") ||
                name.StartsWith("op_"))
            {
                return true;
            }

            // Skip entry point Main method (any signature)
            if (name.Contains("Main") )
            {
                return true;
            }

            // Skip common System.Object contract methods
            if (name == nameof(object.ToString) ||
                name == nameof(object.GetHashCode) ||
                (name == nameof(object.Equals) && method.ParameterList?.Parameters.Count == 1))
            {
                return true;
            }

            // Skip Dispose() without parameters to avoid IDisposable implementations
            if (name == nameof(System.IDisposable.Dispose) && (method.ParameterList?.Parameters.Count ?? 0) == 0)
            {
                return true;
            }

            // Skip P/Invoke methods marked with [DllImport]
            /*
            if (method.AttributeLists.SelectMany(a => a.Attributes)
                    .Any(a => a.Name.ToString().EndsWith("DllImport") || a.Name.ToString().EndsWith("DllImportAttribute")))
            {
                return true;
            }
            */

            // Skip serialization callbacks
            var serializationAttrNames = new[]
            {
                "OnSerializing", "OnSerialized", "OnDeserializing", "OnDeserialized",
                "OnSerializingAttribute", "OnSerializedAttribute", "OnDeserializingAttribute", "OnDeserializedAttribute"
            };
            if (method.AttributeLists.SelectMany(a => a.Attributes)
                    .Any(a => serializationAttrNames.Contains(a.Name.ToString())))
            {
                return true;
            }

            return false;
        }

        private static bool IsPrivateStaticMethod(MethodDeclarationSyntax method)
        {
            return method.Modifiers.Any(SyntaxKind.PrivateKeyword) &&
                   method.Modifiers.Any(SyntaxKind.StaticKeyword);
        }

        private static bool IsOutAssignmentHelper(MethodDeclarationSyntax method)
        {
            if (!IsPrivateStaticMethod(method) ||
                !(method.ReturnType is PredefinedTypeSyntax returnType) ||
                !returnType.Keyword.IsKind(SyntaxKind.VoidKeyword) ||
                method.ParameterList.Parameters.Count == 0 ||
                method.Body?.Statements.Count != 1)
            {
                return false;
            }

            var lastParameter = method.ParameterList.Parameters.Last();
            if (!lastParameter.Modifiers.Any(SyntaxKind.OutKeyword) ||
                !(method.Body.Statements[0] is ExpressionStatementSyntax expressionStatement) ||
                !(expressionStatement.Expression is AssignmentExpressionSyntax assignment))
            {
                return false;
            }

            return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                   assignment.Left is IdentifierNameSyntax left &&
                   left.Identifier.Text == lastParameter.Identifier.Text;
        }

        public IReadOnlyDictionary<string, string> GetMethodMap() => _methodNameMap;
        public IReadOnlyList<MethodRenameEntry> GetEntries() => _entries;
    }

    public readonly struct MethodRenameEntry
    {
        public string ClassName { get; }
        public string MethodName { get; }
        public int ParameterCount { get; }
        public string NewName { get; }

        public MethodRenameEntry(string className, string methodName, int parameterCount, string newName)
        {
            ClassName = className;
            MethodName = methodName;
            ParameterCount = parameterCount;
            NewName = newName;
        }
    }
}
