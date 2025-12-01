using System;
using System.Collections.Generic;
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

        public ClassObfuscationRewriter(IReadOnlyDictionary<string, string> classNameMap)
        {
            _classNameMap = classNameMap ?? throw new ArgumentNullException(nameof(classNameMap));
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var updatedNode = node;
            if (_classNameMap.TryGetValue(node.Identifier.Text, out var newName))
            {
                updatedNode = updatedNode.WithIdentifier(SyntaxFactory.Identifier(newName));
            }

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
            if (IsInReservedContext(node) || !_classNameMap.TryGetValue(node.Identifier.Text, out var newClassName))
            {
                return base.VisitIdentifierName(node);
            }

            return SyntaxFactory.IdentifierName(newClassName).WithTriviaFrom(node);
        }

        public override SyntaxNode VisitGenericName(GenericNameSyntax node)
        {
            var identifier = node.Identifier.Text;
            var updatedArguments = node.TypeArgumentList.Arguments.Select(arg => (TypeSyntax)Visit(arg));
            var updatedList = SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(updatedArguments));

            if (_classNameMap.TryGetValue(identifier, out var newName))
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)).WithTypeArgumentList(updatedList);
            }

            return node.WithTypeArgumentList(updatedList);
        }

        public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        {
            if (IsReservedNamespace(node.Left) || IsReservedNamespace(node))
            {
                return node;
            }

            var left = (NameSyntax)Visit(node.Left);
            var right = (SimpleNameSyntax)Visit(node.Right);

            if (_classNameMap.TryGetValue(node.Right.Identifier.Text, out var newName))
            {
                right = SyntaxFactory.IdentifierName(newName);
            }

            return SyntaxFactory.QualifiedName(left, right).WithTriviaFrom(node);
        }

        public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var updatedType = (TypeSyntax)Visit(node.Type);

            if (updatedType is GenericNameSyntax genericType &&
                string.Equals(genericType.Identifier.Text, /*nameof(List)*/ "List", StringComparison.Ordinal))
            {
                var updatedArguments = genericType.TypeArgumentList.Arguments
                    .Select(argument => (TypeSyntax)Visit(argument));
                var updatedTypeArgumentList = SyntaxFactory.TypeArgumentList(
                    SyntaxFactory.SeparatedList(updatedArguments));

                updatedType = genericType.WithTypeArgumentList(updatedTypeArgumentList);
            }

            return node.WithType(updatedType).WithTriviaFrom(node);
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
            var visitedType = (TypeSyntax)Visit(node.Type);
            return node.WithType(visitedType).WithTriviaFrom(node);
        }

        public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var visitedType = (TypeSyntax)Visit(node.Type);
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
            var visitedType = (TypeSyntax)Visit(node.Type);
            return node.WithType(visitedType).WithTriviaFrom(node);
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
            return IsReservedNamespace(node) ||
                   parent is AssignmentExpressionSyntax ||
                   parent is ArgumentSyntax ||
                   parent is PropertyDeclarationSyntax ||
                   parent is FieldDeclarationSyntax ||
                   parent is VariableDeclaratorSyntax;
        }
    }
}
