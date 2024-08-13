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

            if (parent is AssignmentExpressionSyntax assignment && assignment.Left == node ||
                parent is ArgumentSyntax argument && argument.Expression == node ||
                parent is PropertyDeclarationSyntax ||
                parent is FieldDeclarationSyntax ||
                parent is VariableDeclaratorSyntax)
            {
                // Не обфусцируем, если это переменная, свойство, поле или аргумент метода
                return node;
            }

            // Обфусцируем вызовы методов и доступ к членам классов
            if (parent is MemberAccessExpressionSyntax memberAccess)
            {
                // Если это доступ к члену класса через точку (например, RegistryUtil.GetBinaryValue)
                if (_classNameMap.TryGetValue(node.Identifier.Text, out var newName))
                {
                    return node.WithIdentifier(SyntaxFactory.Identifier(newName)).NormalizeWhitespace();
                }
            }

            // Обфускация имени класса в других случаях
            if (_classNameMap.TryGetValue(node.Identifier.Text, out var newClassName))
            {
                return node.WithIdentifier(SyntaxFactory.Identifier(newClassName)).NormalizeWhitespace();
            }

            /*
            // Проверка на случай, когда переменная присваивается сама себе или передается в конструктор
            bool isAssignmentOrConstructorParameter =
                parent is AssignmentExpressionSyntax assignment && assignment.Left == node ||
                parent is ArgumentSyntax argument && argument.Expression == node;

            // Исключаем замену имен переменных, если они присваиваются или передаются в конструктор
            if (!isAssignmentOrConstructorParameter && _classNameMap.TryGetValue(node.Identifier.Text, out var newName))
            {
                if (IsInReservedNamespace(node))
                {
                    return node;
                }
                // Добавляем пробел после идентификатора, чтобы предотвратить слияние с переменной
                var newIdentifier = SyntaxFactory.Identifier(newName).WithTrailingTrivia(SyntaxFactory.Space);
                // Заменяем только имена классов в типах, а не переменные
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)).NormalizeWhitespace();
            }
            */

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
            // Обфусцируем тип в атрибуте, если он присутствует в словаре
            var nameSyntax = node.Name;

            if (nameSyntax is IdentifierNameSyntax identifierNameSyntax &&
                _classNameMap.TryGetValue(identifierNameSyntax.Identifier.Text, out var newName))
            {
                var newIdentifier = SyntaxFactory.IdentifierName(newName);
                return node.WithName(newIdentifier).NormalizeWhitespace();
            }

            // Обрабатываем случай с квалифицированным именем
            if (nameSyntax is QualifiedNameSyntax qualifiedNameSyntax)
            {
                var right = (SimpleNameSyntax)Visit(qualifiedNameSyntax.Right);

                if (_classNameMap.TryGetValue(right.Identifier.Text, out var qualifiedNewName))
                {
                    var newQualifiedName = qualifiedNameSyntax.WithRight(SyntaxFactory.IdentifierName(qualifiedNewName));
                    return node.WithName(newQualifiedName).NormalizeWhitespace();
                }
            }

            return base.VisitAttribute(node);
        }

        public override SyntaxNode VisitQualifiedName(QualifiedNameSyntax node)
        {
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

            return SyntaxFactory.QualifiedName(left, right).NormalizeWhitespace();
        }

        public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            // Обрабатываем тип, используемый в выражении создания объекта
            var type = (TypeSyntax)Visit(node.Type);

            // Заменяем аргументы типа в случае использования обобщенного типа
            if (type is GenericNameSyntax genericName)
            {
                var newIdentifier = genericName.Identifier;
                if (_classNameMap.TryGetValue(newIdentifier.Text, out var newName))
                {
                    newIdentifier = SyntaxFactory.Identifier(newName);
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

        private bool IsInReservedNamespace(SyntaxNode node)
        {
            var parent = node.Parent;
            while (parent != null)
            {
                if (parent is NamespaceDeclarationSyntax namespaceDeclaration)
                {
                    return reservedNamespaces.Any(ns => namespaceDeclaration.Name.ToString().StartsWith(ns));
                }
                parent = parent.Parent;
            }
            return false;
        }

        public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var typeSyntax = (TypeSyntax)Visit(node.Type);

            return node.WithType(typeSyntax).NormalizeWhitespace();
        }
    }
}
