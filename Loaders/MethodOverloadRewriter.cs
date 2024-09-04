using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace Loaders
{
    class MethodOverloadRewriter : CSharpSyntaxRewriter
    {
        private readonly Random _random = new Random();
        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            // Получение всех методов в классе
            var methods = node.Members.OfType<MethodDeclarationSyntax>()
                                      .Where(m => m.ExpressionBody == null) // Исключаем методы с ExpressionBody
                                      .Where(m => !m.Modifiers.Any(SyntaxKind.AbstractKeyword)) // Исключаем абстрактные методы
                                      .Where(m => !HasDllImportAttribute(m)) // Исключаем методы с атрибутом DllImport
                                      .Where(m => !m.ParameterList.Parameters.Any(p => p.Default != null)) // Исключаем методы с параметрами по умолчанию
                                      .ToList();


            // Генерация перегруженных методов
            var overloadedMethods = new List<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                var overloadedMethod = CreateOverloadedMethod(method);

                // Если метод уже существует, создать новый с уникальным именем для строкового аргумента
                while (overloadedMethod != null && MethodWithSameSignatureExists(overloadedMethod))
                {
                    overloadedMethod = CreateOverloadedMethod(method);
                }

                if (overloadedMethod != null)
                {
                    overloadedMethods.Add(overloadedMethod);
                }
            }

            // Добавление новых методов в класс
            var newClass = node.AddMembers(overloadedMethods.ToArray());

            return base.VisitClassDeclaration(newClass);
        }

        private bool MethodWithSameSignatureExists(MethodDeclarationSyntax method)
        {
            // Получаем имя метода и типы параметров
            var methodName = method.Identifier.Text;
            var parameterTypes = method.ParameterList.Parameters.Select(p => p.Type.ToString()).ToArray();

            var classDeclaration = method.FirstAncestorOrSelf<ClassDeclarationSyntax>();

            if (classDeclaration == null)
            {
                return false; // Если метод не принадлежит классу, пропускаем проверку
            }

            // Поиск методов в классе с таким же именем и количеством параметров
            var existingMethods = classDeclaration.Members
                                                  .OfType<MethodDeclarationSyntax>()
                                                  .Where(m => m.Identifier.Text == methodName &&
                                                              m.ParameterList.Parameters.Count == parameterTypes.Length);

            foreach (var existingMethod in existingMethods)
            {
                var existingParameterTypes = existingMethod.ParameterList.Parameters.Select(p => p.Type.ToString()).ToArray();
                if (parameterTypes.SequenceEqual(existingParameterTypes))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasDllImportAttribute(MethodDeclarationSyntax method)
        {
            // Проверка наличия атрибута DllImport у метода
            return method.AttributeLists
                         .SelectMany(attrList => attrList.Attributes)
                         .Any(attr => attr.Name.ToString().Contains("DllImport"));
        }
        private MethodDeclarationSyntax CreateOverloadedMethod(MethodDeclarationSyntax method)
        {
            // Генерация случайного имени для строкового параметра
            string randomParameterName = GenerateRandomStringIdentifier();

            // Добавление нового параметра типа string с случайным именем
            var newParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(randomParameterName))
                                            .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)));

            // Создание нового списка параметров с добавленным параметром
            var newParameterList = method.ParameterList.AddParameters(newParameter);

            // Создание нового тела метода, используя оригинальное тело
            var originalBody = method.Body ?? SyntaxFactory.Block();

            // Генерация случайного вызова
            string randomInvocationCode = RandomMethodInvoker.RandomMethod();
            var randomInvocationStatement = SyntaxFactory.ParseStatement(randomInvocationCode);

            // Создание нового тела метода с добавленным случайным вызовом в начало
            var newBody = SyntaxFactory.Block(randomInvocationStatement).AddStatements(originalBody.Statements.ToArray());

            // Удаление модификатора override
            var newModifiers = method.Modifiers.Where(m => !m.IsKind(SyntaxKind.OverrideKeyword));

            // Создание нового метода с обновленным списком параметров, телом и модификаторами
            var newMethod = method.WithParameterList(newParameterList)
                                  .WithBody(newBody)
                                  .WithModifiers(SyntaxFactory.TokenList(newModifiers))
                                  .WithExpressionBody(null) // Убираем ExpressionBody
                                  .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None)); // Убираем точку с запятой после метода

            return newMethod;
        }

        private string GenerateRandomStringIdentifier()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s [_random.Next(s.Length)]).ToArray());
        }

    }
}
