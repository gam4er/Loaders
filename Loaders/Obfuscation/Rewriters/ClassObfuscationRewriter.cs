using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Renames class identifiers throughout the syntax tree using a provided name map.
    /// </summary>
    public sealed class ClassObfuscationRewriter : CSharpSyntaxRewriter
    {
        private static readonly HashSet<string> ReservedNamespaces = new(StringComparer.Ordinal)
        {
            "System",
            "Microsoft",
        };

        private readonly IReadOnlyDictionary<string, string> _classNameMap;

        private static readonly object LogLock = new();
        private static readonly string LogFilePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "obf.log"));

        private static void LogDebug(string message)
        {
            var formatted = $"[ClassObfuscationRewriter] {DateTime.UtcNow:O} {message}{Environment.NewLine}";

            lock (LogLock)
            {
                var logDirectory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                File.AppendAllText(LogFilePath, formatted);
            }
        }

        public ClassObfuscationRewriter(IReadOnlyDictionary<string, string> classNameMap)
        {
            _classNameMap = classNameMap ?? throw new ArgumentNullException(nameof(classNameMap));
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            string newName = "";
            var updatedNode = node;

            if (_classNameMap.TryGetValue(node.Identifier.Text, out newName))
            {
                updatedNode = updatedNode.WithIdentifier(SyntaxFactory.Identifier(newName));
            }
#if DEBUG
            if (newName == "Bookmark")
            {
                Console.WriteLine("Bookmark");
            }
#endif

            var updatedAttributes = node.AttributeLists
                .Select(attributeList => (AttributeListSyntax)Visit(attributeList));
            var updatedTypeParameters = (TypeParameterListSyntax?)Visit(node.TypeParameterList);
            var updatedBaseList = (BaseListSyntax?)Visit(node.BaseList);
            var updatedConstraints = node.ConstraintClauses
                .Select(constraint => (TypeParameterConstraintClauseSyntax)Visit(constraint));
            var rewrittenMembers = updatedNode.Members.Select(member => (MemberDeclarationSyntax)Visit(member));

            return updatedNode
                .WithAttributeLists(SyntaxFactory.List(updatedAttributes))
                .WithTypeParameterList(updatedTypeParameters)
                .WithBaseList(updatedBaseList)
                .WithConstraintClauses(SyntaxFactory.List(updatedConstraints))
                .WithMembers(SyntaxFactory.List(rewrittenMembers))
                .NormalizeWhitespace();
        }

        public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node)
        {
            return IsReservedNamespace(node.Name) ? node : base.VisitUsingDirective(node);
        }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            var identifier = node.Identifier.Text;

            if (!IsTypeContext(node) && IsInReservedContext(node))
            {
                if (_classNameMap.ContainsKey(identifier))
                {
                    LogDebug($"Skipping identifier '{identifier}' because it is in a reserved context ({node.Parent?.Kind()}).");
                }

                return base.VisitIdentifierName(node);
            }

            if (!_classNameMap.TryGetValue(identifier, out var newClassName))
            {
                return base.VisitIdentifierName(node);
            }

            LogDebug($"Renaming identifier '{identifier}' to '{newClassName}'.");

            return SyntaxFactory.IdentifierName(newClassName).WithTriviaFrom(node);
        }

        public override SyntaxNode VisitGenericName(GenericNameSyntax node)
        {
            var identifier = node.Identifier.Text;

            var updatedArguments = node.TypeArgumentList.Arguments.Select(arg => (TypeSyntax)Visit(arg));
            var updatedList = SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(updatedArguments));

            if (_classNameMap.TryGetValue(identifier, out var newName))
            {
                LogDebug($"Renaming generic identifier '{identifier}' to '{newName}'.");
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)).WithTypeArgumentList(updatedList);
            }

            if (node.TypeArgumentList.Arguments.Any(arg => ContainsMappedIdentifier(arg)))
            {
                LogDebug($"Visited generic '{identifier}' with updated type arguments: {updatedList.ToFullString()}.");
            }

            return node.WithTypeArgumentList(updatedList);
        }

        public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        {
            var left = (NameSyntax)Visit(node.Left);
            var right = (SimpleNameSyntax)Visit(node.Right);

            if (IsReservedNamespace(node.Left) || IsReservedNamespace(node))
            {
                LogDebug($"Encountered reserved namespace '{node}'. Left visited as '{left}', right visited as '{right}'.");
                return SyntaxFactory.QualifiedName(left, right).WithTriviaFrom(node);
            }

            if (_classNameMap.TryGetValue(node.Right.Identifier.Text, out var newName))
            {
                LogDebug($"Renaming qualified name '{node}' to '{left}.{newName}'.");
                right = SyntaxFactory.IdentifierName(newName);
            }

            return SyntaxFactory.QualifiedName(left, right).WithTriviaFrom(node);
        }

        public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var updatedType = MapType(node.Type);
            var updatedArgumentList = (ArgumentListSyntax?)Visit(node.ArgumentList);
            var updatedInitializer = (InitializerExpressionSyntax?)Visit(node.Initializer);

            LogDebug($"Visiting object creation for type '{node.Type}' => '{updatedType}'. Arguments visited: {updatedArgumentList != null}.");

            if (updatedType is GenericNameSyntax genericType)
            {
                var updatedArguments = genericType.TypeArgumentList.Arguments
                    .Select(argument => MapType(argument));
                var updatedTypeArgumentList = SyntaxFactory.TypeArgumentList(
                    SyntaxFactory.SeparatedList(updatedArguments));

                updatedType = genericType.WithTypeArgumentList(updatedTypeArgumentList);

                LogDebug($"Updated generic object creation type arguments: {updatedTypeArgumentList.ToFullString()} for '{genericType}'.");
            }

            return node
                .WithType(updatedType)
                .WithArgumentList(updatedArgumentList)
                .WithInitializer(updatedInitializer)
                .WithTriviaFrom(node);
        }

        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            if (_classNameMap.TryGetValue(node.Identifier.Text, out var newName))
            {
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName));
            }

            return base.VisitConstructorDeclaration(node).NormalizeWhitespace();
        }

        public override SyntaxNode VisitParameter(ParameterSyntax node)
        {
            var visitedType = MapType(node.Type);
            return node.WithType(visitedType).WithTriviaFrom(node);
        }

        public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var visitedType = MapType(node.Type);
            return node.WithType(visitedType).WithTriviaFrom(node);
        }

        public override SyntaxNode VisitAttribute(AttributeSyntax node)
        {
            var visitedName = (NameSyntax)Visit(node.Name);
            var visitedArguments = node.ArgumentList != null
                ? node.ArgumentList.Arguments.Select(arg => (AttributeArgumentSyntax)Visit(arg))
                : Enumerable.Empty<AttributeArgumentSyntax>();

            var updatedAttribute = node.WithName(visitedName);
            if (node.ArgumentList != null)
            {
                updatedAttribute = updatedAttribute.WithArgumentList(
                    SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(visitedArguments)));
            }

            return updatedAttribute.WithTriviaFrom(node);
        }

        public override SyntaxNode VisitCastExpression(CastExpressionSyntax node)
        {
            var visitedType = MapType(node.Type);
            var visitedExpression = (ExpressionSyntax)Visit(node.Expression);
            return node.WithType(visitedType).WithExpression(visitedExpression).WithTriviaFrom(node);
        }

        private TypeSyntax MapType(TypeSyntax typeSyntax)
        {
            switch (typeSyntax)
            {
                case IdentifierNameSyntax identifier when _classNameMap.TryGetValue(identifier.Identifier.Text, out var newName):
                    return SyntaxFactory.IdentifierName(newName).WithTriviaFrom(identifier);

                case AliasQualifiedNameSyntax aliasQualifiedName:
                    var visitedAliasLeft = MapType(aliasQualifiedName.Alias);
                    var visitedAliasRight = MapType(aliasQualifiedName.Name) as SimpleNameSyntax ?? aliasQualifiedName.Name;
                    return SyntaxFactory.AliasQualifiedName((IdentifierNameSyntax)visitedAliasLeft, visitedAliasRight)
                        .WithTriviaFrom(aliasQualifiedName);

                case GenericNameSyntax generic:
                    var updatedArguments = generic.TypeArgumentList.Arguments
                        .Select(MapType);
                    var updatedTypeArgumentList = SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SeparatedList(updatedArguments));

                    var updatedGeneric = generic.WithTypeArgumentList(updatedTypeArgumentList);
                    if (_classNameMap.TryGetValue(generic.Identifier.Text, out var newGenericName))
                    {
                        updatedGeneric = updatedGeneric.WithIdentifier(SyntaxFactory.Identifier(newGenericName));
                    }

                    return updatedGeneric.WithTriviaFrom(generic);

                case QualifiedNameSyntax qualified:
                    var updatedLeft = MapType(qualified.Left);
                    var updatedRight = MapType(qualified.Right) as SimpleNameSyntax ?? qualified.Right;
                    return SyntaxFactory.QualifiedName((NameSyntax)updatedLeft, updatedRight).WithTriviaFrom(qualified);

                case NullableTypeSyntax nullableType:
                    var visitedUnderlying = MapType(nullableType.ElementType);
                    return SyntaxFactory.NullableType(visitedUnderlying).WithTriviaFrom(nullableType);

                case ArrayTypeSyntax arrayType:
                    var visitedArrayElement = MapType(arrayType.ElementType);
                    var visitedRanks = arrayType.RankSpecifiers.Select(rank => (ArrayRankSpecifierSyntax)Visit(rank));
                    return SyntaxFactory.ArrayType(visitedArrayElement, SyntaxFactory.List(visitedRanks)).WithTriviaFrom(arrayType);

                case PointerTypeSyntax pointerType:
                    var visitedPointerElement = MapType(pointerType.ElementType);
                    return SyntaxFactory.PointerType(visitedPointerElement).WithTriviaFrom(pointerType);

                case TupleTypeSyntax tupleType:
                    var visitedElements = tupleType.Elements.Select(element =>
                    {
                        var visitedElementType = MapType(element.Type);
                        return element.WithType(visitedElementType);
                    });

                    return tupleType.WithElements(SyntaxFactory.SeparatedList(visitedElements)).WithTriviaFrom(tupleType);

                default:
                    return (TypeSyntax)Visit(typeSyntax);
            }
        }

        private bool IsReservedNamespace(SyntaxNode node)
        {
            string? namespaceName = node switch
            {
                NamespaceDeclarationSyntax ns => ns.Name.ToString(),
                QualifiedNameSyntax qn => qn.Left.ToString(),
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => null,
            };

            return namespaceName != null && ReservedNamespaces.Any(ns => namespaceName.StartsWith(ns, StringComparison.Ordinal));
        }

        private bool IsInReservedContext(IdentifierNameSyntax node)
        {
            var parent = node.Parent;

            // Keep namespaces and attribute-related identifiers safe, but allow most other contexts
            // so class/type identifiers used in expressions, variables, arguments, etc. can be renamed.
            return IsReservedNamespace(node) ||
                   parent is AttributeSyntax ||
                   parent is AttributeArgumentSyntax;
        }

        private static bool IsTypeContext(SyntaxNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (current is TypeSyntax)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ContainsMappedIdentifier(TypeSyntax typeSyntax)
        {
            if (typeSyntax is IdentifierNameSyntax identifier)
            {
                return _classNameMap.ContainsKey(identifier.Identifier.Text);
            }

            if (typeSyntax is GenericNameSyntax genericName)
            {
                return _classNameMap.ContainsKey(genericName.Identifier.Text) ||
                       genericName.TypeArgumentList.Arguments.Any(ContainsMappedIdentifier);
            }

            if (typeSyntax is QualifiedNameSyntax qualifiedName)
            {
                return ContainsMappedIdentifier(qualifiedName.Left) || ContainsMappedIdentifier(qualifiedName.Right);
            }

            return false;
        }
    }
}
