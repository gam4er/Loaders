using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace Loaders
{

    public class ClassesEnum : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _classNameMap;

        public ClassesEnum()
        {
            _classNameMap = new Dictionary<string, string>();
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            string originalName = node.Identifier.Text;
            string obfuscatedName = Stuff.GetObfuscatedName(originalName);

            // Сохраняем оригинальное и обфусцированное имя в словаре
            _classNameMap [originalName] = obfuscatedName;

            return base.VisitClassDeclaration(node);
        }

        public Dictionary<string, string> GetClassMap()
        {
            return _classNameMap;
        }
    }

    public class ClassObfuscatorAndNormalizer : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _classNameMap;
        private static readonly HashSet<string> reservedNamespaces = new HashSet<string>
    {
        "System",
        "Microsoft"
    };

        public ClassObfuscatorAndNormalizer(Dictionary<string, string> classNameMap)
        {
            _classNameMap = classNameMap;
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (_classNameMap.TryGetValue(node.Identifier.Text, out var newName))
            {
                node = node.WithIdentifier(SyntaxFactory.Identifier(newName)).NormalizeWhitespace();
            }

            // Нормализация членов класса, чтобы добавить переносы строк между ними
            var members = node.Members;
            var updatedMembers = new SyntaxList<MemberDeclarationSyntax>();

            foreach (var member in members)
            {
                var normalizedMember = (MemberDeclarationSyntax)Visit(member);
                updatedMembers = updatedMembers.Add(normalizedMember);

                // Добавляем перенос строки между членами класса
                if (member != members.Last())
                {
                    // Добавляем пустую строку через Trivia
                    var newlineTrivia = SyntaxFactory.CarriageReturnLineFeed;
                    updatedMembers = updatedMembers.Add(
                        SyntaxFactory.IncompleteMember().WithLeadingTrivia(newlineTrivia));
                }
            }

            return node.WithMembers(updatedMembers).NormalizeWhitespace();
        }

        public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (reservedNamespaces.Any(ns => node.Name.ToString().StartsWith(ns)))
            {
                return node;
            }
            return base.VisitUsingDirective(node);
        }

        public override SyntaxNode VisitGenericName(GenericNameSyntax node)
        {
            // Проверяем, есть ли имя в словаре обфусцированных имен
            if (_classNameMap.TryGetValue(node.Identifier.Text, out var newName))
            {
                if (node.Identifier.Text == "SeatbeltOptions")                  
                    Console.WriteLine("SeatbeltOptions");
                

                // Заменяем имя обобщенного типа
                var newIdentifier = SyntaxFactory.Identifier(newName);

                // Заменяем типы внутри обобщения, если они также обфусцированы
                var typeArguments = node.TypeArgumentList.Arguments.Select(typeArg =>
                {
                    if (typeArg is IdentifierNameSyntax identifierNameSyntax && _classNameMap.TryGetValue(identifierNameSyntax.Identifier.Text, out var typeName))
                    {
                        return SyntaxFactory.IdentifierName(typeName).NormalizeWhitespace();
                    }
                    return typeArg;
                }).ToArray();

                // Создаем новый обобщенный тип с обфусцированными именами
                var newTypeArgumentList = SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(typeArguments));
                return node.WithIdentifier(newIdentifier).WithTypeArgumentList(newTypeArgumentList).NormalizeWhitespace();
            }

            return base.VisitGenericName(node);
        }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            var parent = node.Parent;

            // Если это не обфусцируемый контекст
            if (IsInReservedNamespace(node) ||
                parent is AssignmentExpressionSyntax ||
                parent is ArgumentSyntax ||
                parent is PropertyDeclarationSyntax ||
                parent is FieldDeclarationSyntax ||
                parent is VariableDeclaratorSyntax)
            {
                return node;
            }

            // Обфусцируем имя, если оно найдено в словаре
            if (_classNameMap.TryGetValue(node.Identifier.Text, out var newClassName))
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(newClassName)).NormalizeWhitespace();
            }

            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode VisitCastExpression(CastExpressionSyntax node)
        {
            // Обрабатываем тип в явном приведении
            var typeSyntax = (TypeSyntax)Visit(node.Type);

            if (typeSyntax is IdentifierNameSyntax identifierNameSyntax &&
                _classNameMap.TryGetValue(identifierNameSyntax.Identifier.Text, out var newName))
            {
                var newType = SyntaxFactory.IdentifierName(newName).NormalizeWhitespace();
                return node.WithType(newType).NormalizeWhitespace();
            }

            // Обрабатываем случай с квалифицированным именем
            if (typeSyntax is QualifiedNameSyntax qualifiedNameSyntax)
            {
                var right = (SimpleNameSyntax)Visit(qualifiedNameSyntax.Right);

                if (_classNameMap.TryGetValue(right.Identifier.Text, out var qualifiedNewName))
                {
                    var newQualifiedName = qualifiedNameSyntax.WithRight(SyntaxFactory.IdentifierName(qualifiedNewName));
                    return node.WithType(newQualifiedName).NormalizeWhitespace();
                }
            }

            if (node.Type is GenericNameSyntax Nam)
            {
                // Проверка: есть ли скобки и обфусцированное имя
                if (_classNameMap.TryGetValue(Nam.Identifier.Text, out var nName))
                {
                    var newType = SyntaxFactory.IdentifierName(nName).NormalizeWhitespace();
                    return node.WithType(newType).NormalizeWhitespace();
                }
            }

            return base.VisitCastExpression(node);
        }

        public override SyntaxNode VisitAttribute(AttributeSyntax node)
        {
            var nameSyntax = node.Name;

            if (nameSyntax is IdentifierNameSyntax identifierNameSyntax &&
                _classNameMap.TryGetValue(identifierNameSyntax.Identifier.Text, out var newName))
            {
                var newIdentifier = SyntaxFactory.IdentifierName(newName);
                node = node.WithName(newIdentifier).NormalizeWhitespace();
            }

            var argumentList = node.ArgumentList;
            if (argumentList != null)
            {
                var newArguments = argumentList.Arguments.Select(arg =>
                {
                    if (arg.Expression is TypeOfExpressionSyntax typeOfExpression)
                    {
                        var typeSyntax = typeOfExpression.Type;
                        var visitedTypeSyntax = (TypeSyntax)Visit(typeSyntax);
                        return arg.WithExpression(SyntaxFactory.TypeOfExpression(visitedTypeSyntax));
                    }
                    return arg;
                }).ToArray();

                node = node.WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(newArguments)));
            }

            // Обрабатываем случай с квалифицированным именем
            if (nameSyntax is QualifiedNameSyntax qualifiedNameSyntax)
            {
                var left = (NameSyntax)Visit(qualifiedNameSyntax.Left);
                var right = (SimpleNameSyntax)Visit(qualifiedNameSyntax.Right);

                if (_classNameMap.TryGetValue(right.Identifier.Text, out var newQualifiedName))
                {
                    right = SyntaxFactory.IdentifierName(newQualifiedName);
                    node = node.WithName(SyntaxFactory.QualifiedName(left, right).NormalizeWhitespace());
                }
            }

            return base.VisitAttribute(node);
        }

        public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        {
            if (IsInReservedNamespace(node.Left))
            {
                // Если левая часть зарезервирована, оставляем имя без изменений
                return node;
            }

            if (IsInReservedNamespace(node))
            {
                return node;
            }

            var left = (NameSyntax)Visit(node.Left);
            var right = (SimpleNameSyntax)Visit(node.Right);

            if (_classNameMap.TryGetValue(right.Identifier.Text, out var newName))
            {
                right = SyntaxFactory.IdentifierName(newName);
            }

            //return SyntaxFactory.QualifiedName(left, right).NormalizeWhitespace();
            return SyntaxFactory.QualifiedName((NameSyntax)Visit(node.Left), right).NormalizeWhitespace();
        }

        public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            // Обработка типа, используемого в выражении создания объекта
            var type = (TypeSyntax)Visit(node.Type);

            if (type is IdentifierNameSyntax identifierName &&
                _classNameMap.TryGetValue(identifierName.Identifier.Text, out var newName))
            {
                // Замена имени типа на обфусцированное имя
                var newType = SyntaxFactory.IdentifierName(newName).NormalizeWhitespace();
                return node.WithType(newType).NormalizeWhitespace();
            }

            // Обработка обобщенного типа (например, List<Workspace>)
            if (type is GenericNameSyntax genericName)
            {
                var newIdentifier = genericName.Identifier;
                if (_classNameMap.TryGetValue(newIdentifier.Text, out var newName1))
                {
                    newIdentifier = SyntaxFactory.Identifier(newName1);
                }

                var typeArguments = genericName.TypeArgumentList.Arguments.Select(typeArg =>
                {
                    if (typeArg is IdentifierNameSyntax identifierNameSyntax && _classNameMap.TryGetValue(identifierNameSyntax.Identifier.Text, out var typeName))
                    {
                        return SyntaxFactory.IdentifierName(typeName).NormalizeWhitespace();
                    }
                    return typeArg;
                }).ToArray();

                var newTypeArgumentList = SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(typeArguments));
                type = genericName.WithIdentifier(newIdentifier).WithTypeArgumentList(newTypeArgumentList);
            }

            return node.WithType(type).NormalizeWhitespace();
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
            var typeSyntax = node.Type;

            if (typeSyntax != null && _classNameMap.TryGetValue(typeSyntax.ToString(), out var newName))
            {
                var trailingTrivia = typeSyntax.GetTrailingTrivia();

                // Обеспечиваем наличие пробела между типом и идентификатором параметра
                if (!trailingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)))
                {
                    trailingTrivia = trailingTrivia.Add(SyntaxFactory.Whitespace(" "));
                    typeSyntax = typeSyntax.WithTrailingTrivia(trailingTrivia);
                }
                // Обфусцируем тип параметра
                var newType = SyntaxFactory.IdentifierName(newName).WithTrailingTrivia(SyntaxFactory.Whitespace(" "));
                return node.WithType(newType).NormalizeWhitespace();
            }

            return base.VisitParameter(node).NormalizeWhitespace();
        }

        public override SyntaxNode Visit(SyntaxNode node)
        { 
            if (node != null)
            {
                // Нормализуем узел, добавляя переносы строк для region и атрибутов
                var normalizedNode = base.Visit(node).NormalizeWhitespace();
                
                if (node is ParenthesizedExpressionSyntax parenthesizedExpression)
                {
                    var expression = parenthesizedExpression.Expression;
                    if (expression is CastExpressionSyntax castExpression)
                    {
                        return VisitCastExpression(castExpression);
                    }
                }


                if (node is RegionDirectiveTriviaSyntax || node is EndRegionDirectiveTriviaSyntax || node is AttributeListSyntax)
                {
                    return normalizedNode.WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed)
                                          .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
                }

                return normalizedNode;
            }

            return node;
        }

        public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var typeSyntax = (TypeSyntax)Visit(node.Type);

            if (typeSyntax != null && _classNameMap.TryGetValue(typeSyntax.ToString(), out var newName))
            {
                var newType = SyntaxFactory.IdentifierName(newName).NormalizeWhitespace();
                return node.WithType(newType).NormalizeWhitespace();
            }

            return node.WithType(typeSyntax).NormalizeWhitespace();
        }

        public override SyntaxNode VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
            // Visit the expression inside the parentheses
            // Проверяем, есть ли внутри скобок приведение типа
            if (node.Expression is CastExpressionSyntax castExpression)
            {
                var typeSyntax = castExpression.Type;

                // Если тип присутствует в словаре, заменяем его
                if (_classNameMap.TryGetValue(typeSyntax.ToString(), out var newName))
                {
                    Console.WriteLine(typeSyntax.ToString() + "  " + newName);
                    var newType = SyntaxFactory.IdentifierName(newName).NormalizeWhitespace();
                    return SyntaxFactory.ParenthesizedExpression(SyntaxFactory.CastExpression(newType, castExpression.Expression));
                }
            }

            // Продолжаем нормализацию и обход узла
            return base.VisitParenthesizedExpression(node).NormalizeWhitespace();
        }

        public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            // Проверяем, если в выражении присутствует строковая конкатенация
            if (node.OperatorToken.IsKind(SyntaxKind.PlusToken))
            {
                // Если одна из частей - строка, обфусцируем её
                var left = this.Visit(node.Left);
                var right = this.Visit(node.Right);

                // Собираем новое выражение с учётом скобок
                return node.WithLeft((ExpressionSyntax)left).WithRight((ExpressionSyntax)right);
            }
            return base.VisitBinaryExpression(node);
        }

        private bool IsInReservedNamespace(SyntaxNode node)
        {
            while (node != null)
            {
                if (node is NamespaceDeclarationSyntax namespaceDeclaration)
                {
                    // Проверяем, начинается ли имя пространства имен с зарезервированных префиксов
                    return reservedNamespaces.Any(ns => namespaceDeclaration.Name.ToString().StartsWith(ns));
                }
                else if (node is QualifiedNameSyntax qualifiedName)
                {
                    // Проверяем каждую часть квалифицированного имени
                    return reservedNamespaces.Any(ns => qualifiedName.ToString().StartsWith(ns));
                }
                else if (node is IdentifierNameSyntax identifierName)
                {
                    // Проверяем, начинается ли идентификатор с зарезервированного пространства имен
                    return reservedNamespaces.Any(ns => identifierName.Identifier.Text.StartsWith(ns));
                }

                node = node.Parent;
            }
            return false;
        }

    }
}
