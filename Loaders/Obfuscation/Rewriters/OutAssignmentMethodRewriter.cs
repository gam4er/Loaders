using System;
using System.Collections.Generic;
using System.Linq;
using Loaders.Obfuscation.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loaders.Obfuscation.Rewriters
{
    internal sealed class OutAssignmentMethodRewriter : CSharpSyntaxRewriter
    {
        private static readonly SymbolDisplayFormat TypeDisplayFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        private readonly SemanticModel _semanticModel;
        private readonly IDictionary<string, HashSet<string>> _reservedMemberNames;
        private readonly Stack<ClassRewriteContext> _classContexts = new Stack<ClassRewriteContext>();

        public OutAssignmentMethodRewriter(
            SemanticModel semanticModel,
            IDictionary<string, HashSet<string>> reservedMemberNames)
        {
            _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            _reservedMemberNames = reservedMemberNames ?? throw new ArgumentNullException(nameof(reservedMemberNames));
        }

        public bool Changed { get; private set; }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var classSymbol = _semanticModel.GetDeclaredSymbol(node);
            if (classSymbol == null)
            {
                return base.VisitClassDeclaration(node);
            }

            var typeKey = GetTypeKey(classSymbol);
            if (!_reservedMemberNames.TryGetValue(typeKey, out var reservedNames))
            {
                reservedNames = new HashSet<string>(StringComparer.Ordinal);
                _reservedMemberNames[typeKey] = reservedNames;
            }

            var context = new ClassRewriteContext(classSymbol, reservedNames);
            _classContexts.Push(context);

            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
            _classContexts.Pop();

            if (context.Helpers.Count == 0)
            {
                return visited;
            }

            Changed = true;
            return visited.AddMembers(context.Helpers.ToArray());
        }

        public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            if (_classContexts.Count == 0 ||
                !TryCreateReplacement(node, _classContexts.Peek(), out var replacement, out var helper))
            {
                return base.VisitLocalDeclarationStatement(node);
            }

            _classContexts.Peek().Helpers.Add(helper);
            Changed = true;
            return replacement
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());
        }

        public static string GetTypeKey(INamedTypeSymbol typeSymbol)
        {
            return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private bool TryCreateReplacement(
            LocalDeclarationStatementSyntax node,
            ClassRewriteContext context,
            out StatementSyntax replacement,
            out MethodDeclarationSyntax helper)
        {
            replacement = null;
            helper = null;

            if (ShouldSkipDeclarationShape(node))
            {
                return false;
            }

            var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method == null || method.TypeParameterList != null)
            {
                return false;
            }

            if (node.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().Any() ||
                node.Ancestors().OfType<LocalFunctionStatementSyntax>().Any())
            {
                return false;
            }

            var declarator = node.Declaration.Variables[0];
            var initializer = declarator.Initializer?.Value;
            if (initializer == null ||
                initializer is InitializerExpressionSyntax ||
                HasUnsupportedSyntax(initializer))
            {
                return false;
            }

            var localSymbol = _semanticModel.GetDeclaredSymbol(declarator) as ILocalSymbol;
            if (localSymbol == null ||
                !TryCreateTypeSyntax(localSymbol.Type, out var targetType))
            {
                return false;
            }

            if (!TryCollectDependencies(initializer, localSymbol, out var dependencies))
            {
                return false;
            }

            if (!IsLiftableExpression(initializer))
            {
                return false;
            }

            var helperName = ReserveHelperName(context, localSymbol.Type);
            var targetName = declarator.Identifier.Text;
            replacement = CreateInvocationStatement(helperName, dependencies, targetName);
            helper = CreateHelperMethod(helperName, dependencies, targetName, targetType, initializer);
            return true;
        }

        private static bool ShouldSkipDeclarationShape(LocalDeclarationStatementSyntax node)
        {
            return node.Modifiers.Any(SyntaxKind.ConstKeyword) ||
                   node.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) ||
                   node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) ||
                   node.Declaration.Variables.Count != 1 ||
                   node.Declaration.Variables[0].Initializer == null;
        }

        private static bool HasUnsupportedSyntax(ExpressionSyntax expression)
        {
            return expression.DescendantNodesAndSelf().Any(node =>
                node is AnonymousFunctionExpressionSyntax ||
                node is QueryExpressionSyntax ||
                node is DeclarationExpressionSyntax ||
                node is DeclarationPatternSyntax ||
                node is RefExpressionSyntax ||
                node is ArgumentSyntax argument && IsRefLikeArgument(argument) ||
                node.IsKind(SyntaxKind.AwaitExpression) ||
                node.IsKind(SyntaxKind.StackAllocArrayCreationExpression) ||
                node.IsKind(SyntaxKind.ImplicitStackAllocArrayCreationExpression) ||
                node.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                node.IsKind(SyntaxKind.AddAssignmentExpression) ||
                node.IsKind(SyntaxKind.SubtractAssignmentExpression) ||
                node.IsKind(SyntaxKind.MultiplyAssignmentExpression) ||
                node.IsKind(SyntaxKind.DivideAssignmentExpression) ||
                node.IsKind(SyntaxKind.ModuloAssignmentExpression) ||
                node.IsKind(SyntaxKind.AndAssignmentExpression) ||
                node.IsKind(SyntaxKind.ExclusiveOrAssignmentExpression) ||
                node.IsKind(SyntaxKind.OrAssignmentExpression) ||
                node.IsKind(SyntaxKind.LeftShiftAssignmentExpression) ||
                node.IsKind(SyntaxKind.RightShiftAssignmentExpression) ||
                node.IsKind(SyntaxKind.CoalesceAssignmentExpression) ||
                node.IsKind(SyntaxKind.PreIncrementExpression) ||
                node.IsKind(SyntaxKind.PreDecrementExpression) ||
                node.IsKind(SyntaxKind.PostIncrementExpression) ||
                node.IsKind(SyntaxKind.PostDecrementExpression) ||
                node.IsKind(SyntaxKind.ThisExpression) ||
                node.IsKind(SyntaxKind.BaseExpression));
        }

        private static bool IsRefLikeArgument(ArgumentSyntax argument)
        {
            return argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) ||
                   argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword) ||
                   argument.RefOrOutKeyword.IsKind(SyntaxKind.InKeyword);
        }

        private bool TryCollectDependencies(
            ExpressionSyntax expression,
            ILocalSymbol targetSymbol,
            out IReadOnlyList<OutAssignmentDependency> dependencies)
        {
            dependencies = Array.Empty<OutAssignmentDependency>();
            var orderedDependencies = new List<OutAssignmentDependency>();
            var seenSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                var symbol = _semanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol == null)
                {
                    var candidates = _semanticModel.GetSymbolInfo(identifier).CandidateSymbols;
                    if (candidates.Length == 1)
                    {
                        symbol = candidates[0];
                    }
                    else if (candidates.Length > 1)
                    {
                        return false;
                    }
                }

                if (symbol == null)
                {
                    continue;
                }

                if (symbol is ILocalSymbol local)
                {
                    if (SymbolEqualityComparer.Default.Equals(local, targetSymbol))
                    {
                        return false;
                    }

                    if (!TryAddDependency(local, identifier, orderedDependencies, seenSymbols))
                    {
                        return false;
                    }

                    continue;
                }

                if (symbol is IParameterSymbol parameter)
                {
                    if (!TryAddDependency(parameter, identifier, orderedDependencies, seenSymbols))
                    {
                        return false;
                    }

                    continue;
                }

                if (!IsAllowedNonDependencySymbol(symbol, identifier))
                {
                    return false;
                }
            }

            dependencies = orderedDependencies;
            return true;
        }

        private static bool TryAddDependency(
            ILocalSymbol local,
            IdentifierNameSyntax identifier,
            ICollection<OutAssignmentDependency> dependencies,
            ISet<ISymbol> seenSymbols)
        {
            if (!seenSymbols.Add(local))
            {
                return true;
            }

            if (!TryCreateTypeSyntax(local.Type, out var typeSyntax))
            {
                return false;
            }

            dependencies.Add(new OutAssignmentDependency(local, identifier.Identifier.Text, typeSyntax));
            return true;
        }

        private static bool TryAddDependency(
            IParameterSymbol parameter,
            IdentifierNameSyntax identifier,
            ICollection<OutAssignmentDependency> dependencies,
            ISet<ISymbol> seenSymbols)
        {
            if (!seenSymbols.Add(parameter))
            {
                return true;
            }

            if (!TryCreateTypeSyntax(parameter.Type, out var typeSyntax))
            {
                return false;
            }

            dependencies.Add(new OutAssignmentDependency(parameter, identifier.Identifier.Text, typeSyntax));
            return true;
        }

        private bool IsLiftableExpression(ExpressionSyntax expression)
        {
            foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                var symbol = _semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol == null ||
                    symbol.MethodKind == MethodKind.LocalFunction ||
                    UsesMethodTypeParameter(symbol))
                {
                    return false;
                }

                if (!symbol.IsStatic &&
                    !symbol.IsExtensionMethod &&
                    !InvocationHasExplicitReceiver(invocation))
                {
                    return false;
                }
            }

            foreach (var memberAccess in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
            {
                var symbol = _semanticModel.GetSymbolInfo(memberAccess.Name).Symbol;
                if (symbol is IFieldSymbol field && !field.IsStatic && IsImplicitInstanceReceiver(memberAccess.Expression))
                {
                    return false;
                }

                if (symbol is IPropertySymbol property && !property.IsStatic && IsImplicitInstanceReceiver(memberAccess.Expression))
                {
                    return false;
                }

                if (symbol is IEventSymbol eventSymbol && !eventSymbol.IsStatic && IsImplicitInstanceReceiver(memberAccess.Expression))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool InvocationHasExplicitReceiver(InvocationExpressionSyntax invocation)
        {
            return invocation.Expression is MemberAccessExpressionSyntax ||
                   invocation.Expression is MemberBindingExpressionSyntax;
        }

        private static bool IsImplicitInstanceReceiver(ExpressionSyntax expression)
        {
            return expression is ThisExpressionSyntax ||
                   expression is BaseExpressionSyntax;
        }

        private static bool IsAllowedNonDependencySymbol(ISymbol symbol, IdentifierNameSyntax identifier)
        {
            switch (symbol)
            {
                case INamespaceSymbol _:
                case INamedTypeSymbol _:
                case ITypeParameterSymbol _:
                case IAliasSymbol _:
                    return true;
                case IFieldSymbol field:
                    return field.IsStatic || IsQualifiedMemberName(identifier);
                case IPropertySymbol property:
                    return property.IsStatic || IsQualifiedMemberName(identifier);
                case IEventSymbol eventSymbol:
                    return eventSymbol.IsStatic || IsQualifiedMemberName(identifier);
                case IMethodSymbol method:
                    return method.IsStatic || method.IsExtensionMethod || IsQualifiedMemberName(identifier);
                default:
                    return false;
            }
        }

        private static bool IsQualifiedMemberName(IdentifierNameSyntax identifier)
        {
            return identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
                   ReferenceEquals(memberAccess.Name, identifier) ||
                   identifier.Parent is MemberBindingExpressionSyntax;
        }

        private static bool UsesMethodTypeParameter(IMethodSymbol method)
        {
            return method.TypeArguments.Any(type => ContainsMethodTypeParameter(type)) ||
                   method.Parameters.Any(parameter => ContainsMethodTypeParameter(parameter.Type)) ||
                   ContainsMethodTypeParameter(method.ReturnType);
        }

        private static bool ContainsMethodTypeParameter(ITypeSymbol type)
        {
            if (type == null)
            {
                return false;
            }

            if (type is ITypeParameterSymbol typeParameter &&
                typeParameter.DeclaringMethod != null)
            {
                return true;
            }

            if (type is IArrayTypeSymbol arrayType)
            {
                return ContainsMethodTypeParameter(arrayType.ElementType);
            }

            if (type is INamedTypeSymbol namedType)
            {
                return namedType.TypeArguments.Any(ContainsMethodTypeParameter);
            }

            return false;
        }

        private static bool TryCreateTypeSyntax(ITypeSymbol type, out TypeSyntax typeSyntax)
        {
            typeSyntax = null;
            if (ContainsUnsupportedType(type))
            {
                return false;
            }

            var typeName = type.ToDisplayString(TypeDisplayFormat);
            if (string.IsNullOrWhiteSpace(typeName) ||
                typeName.Contains("<anonymous type>") ||
                typeName.IndexOf("dynamic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            var parsedType = SyntaxFactory.ParseTypeName(typeName);
            if (parsedType.ContainsDiagnostics)
            {
                return false;
            }

            typeSyntax = parsedType;
            return true;
        }

        private static bool ContainsUnsupportedType(ITypeSymbol type)
        {
            if (type == null ||
                type.TypeKind == TypeKind.Dynamic ||
                type.TypeKind == TypeKind.Pointer ||
                type.IsAnonymousType ||
                type.TypeKind.ToString().IndexOf("FunctionPointer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (type is IArrayTypeSymbol arrayType)
            {
                return ContainsUnsupportedType(arrayType.ElementType);
            }

            if (type is INamedTypeSymbol namedType)
            {
                return namedType.IsTupleType ||
                       namedType.IsRefLikeType ||
                       namedType.TypeArguments.Any(ContainsUnsupportedType);
            }

            return false;
        }

        private static StatementSyntax CreateInvocationStatement(
            string helperName,
            IEnumerable<OutAssignmentDependency> dependencies,
            string targetName)
        {
            var arguments = dependencies
                .Select(dependency => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(dependency.ParameterName)))
                .Concat(new[]
                {
                    SyntaxFactory.Argument(
                            SyntaxFactory.DeclarationExpression(
                                SyntaxFactory.IdentifierName("var"),
                                SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(targetName))))
                        .WithRefOrOutKeyword(SyntaxFactory.Token(SyntaxKind.OutKeyword))
                });

            return SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(helperName))
                    .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments))));
        }

        private static MethodDeclarationSyntax CreateHelperMethod(
            string helperName,
            IEnumerable<OutAssignmentDependency> dependencies,
            string targetName,
            TypeSyntax targetType,
            ExpressionSyntax initializer)
        {
            var parameters = dependencies
                .Select(dependency => SyntaxFactory.Parameter(SyntaxFactory.Identifier(dependency.ParameterName))
                    .WithType(dependency.Type))
                .Concat(new[]
                {
                    SyntaxFactory.Parameter(SyntaxFactory.Identifier(targetName))
                        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.OutKeyword)))
                        .WithType(targetType)
                });

            var assignment = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(targetName),
                    initializer.WithoutTrivia()));

            return SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                    SyntaxFactory.Identifier(helperName))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)))
                .WithBody(SyntaxFactory.Block(assignment));
        }

        private static string ReserveHelperName(ClassRewriteContext context, ITypeSymbol targetType)
        {
            var baseName = ObfuscatedNameGenerator.Generate(
                "OutAssignmentHelper:" + targetType.ToDisplayString(TypeDisplayFormat));
            var helperName = baseName;
            var suffix = 1;
            while (!context.ReservedMemberNames.Add(helperName))
            {
                helperName = baseName + suffix.ToString();
                suffix++;
            }

            return helperName;
        }

        private sealed class ClassRewriteContext
        {
            public ClassRewriteContext(INamedTypeSymbol classSymbol, HashSet<string> reservedMemberNames)
            {
                ClassSymbol = classSymbol;
                ReservedMemberNames = reservedMemberNames;
            }

            public INamedTypeSymbol ClassSymbol { get; }
            public HashSet<string> ReservedMemberNames { get; }
            public List<MethodDeclarationSyntax> Helpers { get; } = new List<MethodDeclarationSyntax>();
        }

        private sealed class OutAssignmentDependency
        {
            public OutAssignmentDependency(ISymbol symbol, string parameterName, TypeSyntax type)
            {
                Symbol = symbol;
                ParameterName = parameterName;
                Type = type;
            }

            public ISymbol Symbol { get; }
            public string ParameterName { get; }
            public TypeSyntax Type { get; }
        }
    }
}
