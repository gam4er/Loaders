using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loaders.Obfuscation.Rewriters
{
    /// <summary>
    /// Adds random overloads to methods to complicate control flow analysis.
    ///
    /// Overloads are only generated for suitable methods (no expression-bodied
    /// members, no abstract/DllImport/default-parameter methods) to avoid
    /// breaking signatures that are relied upon by external callers.
    /// </summary>
    public sealed class MethodOverloadRewriter : CSharpSyntaxRewriter
    {
        private const int RandomIdentifierLength = 8;
        private readonly Random _random = new();

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var candidateMethods = node.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.ExpressionBody == null)
                .Where(method => !method.Modifiers.Any(SyntaxKind.AbstractKeyword))
                .Where(method => !HasDllImportAttribute(method))
                .Where(method => !method.ParameterList.Parameters.Any(parameter => parameter.Default != null))
                .ToList();

            var overloads = candidateMethods
                .Select(CreateOverloadedMethod)
                .Where(overload => overload != null && !MethodWithSameSignatureExists(node, overload))
                .Cast<MethodDeclarationSyntax>()
                .ToArray();

            var updatedClass = node.AddMembers(overloads);
            return base.VisitClassDeclaration(updatedClass);
        }

        private static bool HasDllImportAttribute(MethodDeclarationSyntax method)
        {
            return method.AttributeLists
                .SelectMany(attrList => attrList.Attributes)
                .Any(attr => attr.Name.ToString().Contains("DllImport"));
        }

        private bool MethodWithSameSignatureExists(ClassDeclarationSyntax classDeclaration, MethodDeclarationSyntax method)
        {
            string methodName = method.Identifier.Text;
            string[] parameterTypes = method.ParameterList.Parameters.Select(p => p.Type?.ToString() ?? string.Empty).ToArray();

            return classDeclaration.Members
                .OfType<MethodDeclarationSyntax>()
                .Any(existing =>
                    existing.Identifier.Text == methodName &&
                    existing.ParameterList.Parameters.Count == parameterTypes.Length &&
                    existing.ParameterList.Parameters
                        .Select(p => p.Type?.ToString() ?? string.Empty)
                        .SequenceEqual(parameterTypes));
        }

        private MethodDeclarationSyntax? CreateOverloadedMethod(MethodDeclarationSyntax method)
        {
            string randomParameterName = GenerateRandomIdentifier();
            var newParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(randomParameterName))
                .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)));

            var newParameterList = method.ParameterList.AddParameters(newParameter);
            string randomInvocationCode = RandomMethodInvoker.RandomMethod();
            var randomInvocationStatement = SyntaxFactory.ParseStatement(randomInvocationCode);

            var originalBody = method.Body ?? SyntaxFactory.Block();
            var newBody = SyntaxFactory.Block(randomInvocationStatement).AddStatements(originalBody.Statements.ToArray());

            var sanitizedModifiers = method.Modifiers.Where(modifier => !modifier.IsKind(SyntaxKind.OverrideKeyword));

            return method.WithParameterList(newParameterList)
                .WithBody(newBody)
                .WithModifiers(SyntaxFactory.TokenList(sanitizedModifiers))
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
        }

        private string GenerateRandomIdentifier()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            return new string(Enumerable
                .Repeat(chars, RandomIdentifierLength)
                .Select(set => set[_random.Next(set.Length)])
                .ToArray());
        }
    }
}
