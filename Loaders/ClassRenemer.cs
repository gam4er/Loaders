using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;


namespace Loaders
{
    public class ClassRenamer
    {
        public static void RenameClassesInFiles(Dictionary<string, string> classMap, List<string> filePaths)
        {
            var workspace = new AdhocWorkspace();
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            };
            var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "MyProject", "MyAssembly", "C#", metadataReferences: references);
            var project = workspace.AddProject(projectInfo);

            foreach (var filePath in filePaths)
            {
                var code = File.ReadAllText(filePath);
                project = project.AddDocument(Path.GetFileName(filePath), SourceText.From(code),null, filePath).Project;
            }

            var compilation = project.GetCompilationAsync().Result;
            var solution = project.Solution;

            foreach (var classEntry in classMap)
            {
                foreach (var documentId in project.DocumentIds)
                {
                    var document = solution.GetDocument(documentId);
                    var root = document.GetSyntaxRootAsync().Result;
                    var model = compilation.GetSemanticModel(root.SyntaxTree);

                    // 1. Замена самого класса и вызовов конструктора
                    var originalClasses = root.DescendantNodesAndSelf()
                                              .OfType<ClassDeclarationSyntax>()
                                              .Where(c => c.Identifier.Text == classEntry.Key ||
                                                          c.BaseList?.Types.Any(bt => bt.Type.ToString() == classEntry.Key) == true)
                                              .ToList();



                    foreach (var originalClass in originalClasses)
                    {
                        var originalSymbol = model.GetDeclaredSymbol(originalClass);
                        if (originalSymbol != null)
                        {
                            solution = Renamer.RenameSymbolAsync(solution, originalSymbol, classEntry.Value, null).Result;
                        }

                        // Обработка базового класса
                        if (originalClass.BaseList != null)
                        {
                            foreach (var baseType in originalClass.BaseList.Types)
                            {
                                var baseTypeSymbol = model.GetSymbolInfo(baseType.Type).Symbol;
                                if (baseTypeSymbol != null && baseTypeSymbol.CanBeReferencedByName)
                                {
                                    try
                                    {
                                        solution = Renamer.RenameSymbolAsync(solution, baseTypeSymbol, classEntry.Value, null).Result;
                                    }
                                    catch { }
                                }
                            }
                        }
                    }


                    
                    // 2. Замена вложенных типов
                    var nestedTypes = root.DescendantNodesAndSelf()
                                          .OfType<MemberAccessExpressionSyntax>()
                                          .Where(ma => ma.Expression.ToString() == classEntry.Key)
                                          .ToList();

                    
                    foreach (var nestedType in nestedTypes)
                    {
                        document = solution.GetDocument(documentId);
                        root = document.GetSyntaxRootAsync().Result;
                        model = compilation.GetSemanticModel(root.SyntaxTree); 

                        var originalSymbol = model.GetSymbolInfo(nestedType).Symbol;
                        if (originalSymbol != null && originalSymbol.CanBeReferencedByName)
                        {
                            solution = Renamer.RenameSymbolAsync(solution, originalSymbol, classEntry.Value, null).Result;

                            document = solution.GetDocument(documentId);
                            root = document.GetSyntaxRootAsync().Result;
                            project = document.Project;
                            compilation = project.GetCompilationAsync().Result;
                        }
                    }
                    

                    var nestedTypes2 = root.DescendantNodesAndSelf()
                      .OfType<MemberAccessExpressionSyntax>()
                      .ToList();

                    foreach (var nestedType in nestedTypes2)
                    {
                        // Проверка и замена, если Expression является идентификатором и соответствует имени класса
                        if (nestedType.Expression is IdentifierNameSyntax identifierNameSyntax &&
                            identifierNameSyntax.Identifier.Text == classEntry.Key)
                        {
                            // Создаем новый идентификатор с обфусцированным именем
                            var newIdentifier = SyntaxFactory.IdentifierName(classEntry.Value);

                            // Заменяем выражение на новое обфусцированное имя
                            var newNestedType = nestedType.WithExpression(newIdentifier).NormalizeWhitespace();

                            // Обновляем корневой узел с новым MemberAccessExpression
                            root = root.ReplaceNode(nestedType, newNestedType);
                        }
                        // Проверка и замена, если Expression является вложенным MemberAccessExpressionSyntax
                        else if (nestedType.Expression is MemberAccessExpressionSyntax innerMemberAccess)
                        {
                            // Проверяем все части вложенного MemberAccessExpressionSyntax
                            if (innerMemberAccess.Name is IdentifierNameSyntax innerIdentifierNameSyntax &&
                                innerIdentifierNameSyntax.Identifier.Text == classEntry.Key)
                            {
                                // Создаем новый идентификатор с обфусцированным именем
                                var newInnerIdentifier = SyntaxFactory.IdentifierName(classEntry.Value);

                                // Обновляем внутреннее MemberAccessExpression
                                var newInnerMemberAccess = innerMemberAccess.WithName(newInnerIdentifier).NormalizeWhitespace();

                                // Заменяем узел в дереве
                                var newNestedType = nestedType.WithExpression(newInnerMemberAccess).NormalizeWhitespace();
                                root = root.ReplaceNode(nestedType, newNestedType);
                            }
                        }
                    }


                    var objectCreations = root.DescendantNodesAndSelf()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .Where(oc => oc.Type is IdentifierNameSyntax identifierNameSyntax &&
                        identifierNameSyntax.Identifier.Text == classEntry.Key)
                        .ToList();

                    foreach (var objectCreation in objectCreations)
                    {
                        if (objectCreation.Type is IdentifierNameSyntax identifierNameSyntax &&
                            identifierNameSyntax.Identifier.Text == classEntry.Key)
                        {
                            if (classEntry.Key == "WindowsFirewallRule")
                                Console.WriteLine(classEntry.Key);

                            // Создаем новый идентификатор с обфусцированным именем
                            var newIdentifier = SyntaxFactory.IdentifierName(classEntry.Value);

                            // Заменяем тип в ObjectCreationExpression на новый обфусцированный идентификатор
                            var newObjectCreation = objectCreation.WithType(newIdentifier).NormalizeWhitespace();

                            // Обновляем корневой узел с новым ObjectCreationExpression
                            root = root.ReplaceNode(objectCreation, newObjectCreation);

                            // Обновляем документ и решение после замены
                            document = document.WithSyntaxRoot(root);
                            solution = document.Project.Solution;

                            // Обновляем проект и компиляцию после изменений
                            project = document.Project;
                            compilation = project.GetCompilationAsync().Result;
                        }
                    }

                    // Добавить после существующей обработки "ObjectCreationExpressionSyntax"
                    // Обработка объявлений переменных с обобщенными типами, такими как List<Workspace>
                    var variableDeclarations = root.DescendantNodesAndSelf()
                        .OfType<VariableDeclarationSyntax>()
                        .ToList();

                    foreach (var variableDeclaration in variableDeclarations)
                    {
                        // Обработка типа переменной (например, List<Workspace>)
                        if (variableDeclaration.Type is GenericNameSyntax genericName &&
                            genericName.TypeArgumentList.Arguments.Any(arg =>
                                arg is IdentifierNameSyntax identifierNameSyntax &&
                                identifierNameSyntax.Identifier.Text == classEntry.Key))
                        {
                            var newArguments = genericName.TypeArgumentList.Arguments
                                .Select(arg =>
                                {
                                    if (arg is IdentifierNameSyntax identifierNameSyntax &&
                                        identifierNameSyntax.Identifier.Text == classEntry.Key)
                                    {
                                        // Создаем новый идентификатор с обфусцированным именем
                                        return SyntaxFactory.IdentifierName(classEntry.Value).NormalizeWhitespace();
                                    }
                                    return arg;
                                }).ToArray();

                            // Создаем новый список аргументов типа
                            var newTypeArgumentList = SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(newArguments));

                            // Заменяем тип в GenericNameSyntax на новый обфусцированный тип
                            var newGenericName = genericName.WithTypeArgumentList(newTypeArgumentList).NormalizeWhitespace();

                            // Обновляем узел объявления переменной с новым обфусцированным типом
                            var newVariableDeclaration = variableDeclaration.WithType(newGenericName).NormalizeWhitespace();
                            root = root.ReplaceNode(variableDeclaration, newVariableDeclaration);

                            // Обновляем документ и решение после замены
                            document = document.WithSyntaxRoot(root);
                            solution = document.Project.Solution;
                            project = document.Project;
                            compilation = project.GetCompilationAsync().Result;
                        }

                        // Обработка типа переменной (например, Workspace)
                        if (variableDeclaration.Type is IdentifierNameSyntax identifierName &&
                            identifierName.Identifier.Text == classEntry.Key)
                        {
                            // Создаем новый идентификатор с обфусцированным именем
                            var newIdentifier = SyntaxFactory.IdentifierName(classEntry.Value);

                            // Обновляем узел объявления переменной с новым обфусцированным типом
                            var newVariableDeclaration = variableDeclaration.WithType(newIdentifier).NormalizeWhitespace();
                            root = root.ReplaceNode(variableDeclaration, newVariableDeclaration);

                            // Обновляем документ и решение после замены
                            document = document.WithSyntaxRoot(root);
                            solution = document.Project.Solution;
                            project = document.Project;
                            compilation = project.GetCompilationAsync().Result;
                        }
                    }



                    // Обработка квалифицированных имен и вызовов методов
                    var memberAccessExpressions = root.DescendantNodesAndSelf()
                                                      .OfType<MemberAccessExpressionSyntax>()
                                                      .ToList();

                    foreach (var memberAccess in memberAccessExpressions)
                    {
                        if (memberAccess.Expression is IdentifierNameSyntax identifierNameSyntax &&
                            identifierNameSyntax.Identifier.Text == classEntry.Key)
                        {
                            // Создаем новый идентификатор с обфусцированным именем
                            var newIdentifier = SyntaxFactory.IdentifierName(classEntry.Value);

                            // Заменяем идентификатор в квалифицированном имени на новый
                            var newMemberAccess = memberAccess.WithExpression(newIdentifier).NormalizeWhitespace();

                            // Обновляем корневой узел с новым MemberAccessExpression
                            root = root.ReplaceNode(memberAccess, newMemberAccess);

                            // Обновляем документ и решение после замены
                            document = document.WithSyntaxRoot(root);
                            solution = document.Project.Solution;

                            // Обновляем проект и компиляцию после изменений
                            project = document.Project;
                            compilation = project.GetCompilationAsync().Result;
                        }
                        else if (memberAccess.Expression is MemberAccessExpressionSyntax innerMemberAccess &&
                                 innerMemberAccess.Name is IdentifierNameSyntax innerIdentifierNameSyntax &&
                                 innerIdentifierNameSyntax.Identifier.Text == classEntry.Key)
                        {
                            // Заменяем внутренний идентификатор, если это квалифицированное имя
                            var newInnerIdentifier = SyntaxFactory.IdentifierName(classEntry.Value);

                            // Обновляем внутренний MemberAccessExpression
                            var newInnerMemberAccess = innerMemberAccess.WithName(newInnerIdentifier).NormalizeWhitespace();

                            // Заменяем узел в дереве
                            var newMemberAccess = memberAccess.WithExpression(newInnerMemberAccess).NormalizeWhitespace();
                            root = root.ReplaceNode(memberAccess, newMemberAccess);

                            // Обновляем документ и решение после замены
                            document = document.WithSyntaxRoot(root);
                            solution = document.Project.Solution;

                            // Обновляем проект и компиляцию после изменений
                            project = document.Project;
                            compilation = project.GetCompilationAsync().Result;
                        }
                    }


                    var genericTypes = root.DescendantNodesAndSelf()
                       .OfType<ObjectCreationExpressionSyntax>()
                       .Where(oc => oc.Type is GenericNameSyntax genericName &&
                                    genericName.TypeArgumentList.Arguments.Any(arg =>
                                        arg is IdentifierNameSyntax identifierNameSyntax &&
                                        identifierNameSyntax.Identifier.Text == classEntry.Key))
                       .ToList();

                    foreach (var objectCreation in genericTypes)
                    {
                        var genericName = objectCreation.Type as GenericNameSyntax;
                        if (genericName != null)
                        {
                            var newArguments = genericName.TypeArgumentList.Arguments
                                               .Select(arg =>
                                               {
                                                   if (arg is IdentifierNameSyntax identifierNameSyntax &&
                                                       identifierNameSyntax.Identifier.Text == classEntry.Key)
                                                   {
                                                       // Создаем новый идентификатор с обфусцированным именем
                                                       return SyntaxFactory.IdentifierName(classEntry.Value).NormalizeWhitespace();
                                                   }
                                                   return arg;
                                               })
                                               .ToArray();

                            // Создаем новый список аргументов типа
                            var newTypeArgumentList = SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(newArguments));

                            // Заменяем тип в GenericNameSyntax на новый обфусцированный тип
                            var newGenericName = genericName.WithTypeArgumentList(newTypeArgumentList).NormalizeWhitespace();

                            // Обновляем узел создания объекта с новым обфусцированным типом
                            var newObjectCreation = objectCreation.WithType(newGenericName).NormalizeWhitespace();

                            // Обновляем корневой узел с новым ObjectCreationExpression
                            root = root.ReplaceNode(objectCreation, newObjectCreation);

                            // Обновляем документ и решение после замены
                            document = document.WithSyntaxRoot(root);
                            solution = document.Project.Solution;

                            // Обновляем проект и компиляцию после изменений
                            project = document.Project;
                            compilation = project.GetCompilationAsync().Result;
                        }
                    }




                    var destructors = root.DescendantNodesAndSelf()
                        .OfType<DestructorDeclarationSyntax>()
                        .Where(d => d.Identifier.Text == classEntry.Key)
                        .ToList();

                    foreach (var destructor in destructors)
                    {
                        // Проверка и замена идентификатора деструктора
                        if (destructor.Identifier.Text == classEntry.Key)
                        {
                            // Создаем новый идентификатор с обфусцированным именем
                            var newIdentifier = SyntaxFactory.Identifier(classEntry.Value);

                            // Заменяем идентификатор деструктора на новый
                            var newDestructor = destructor.WithIdentifier(newIdentifier).NormalizeWhitespace();

                            // Обновляем корневой узел с новым деструктором
                            root = root.ReplaceNode(destructor, newDestructor);

                            // Обновляем документ и решение после замены
                            document = document.WithSyntaxRoot(root);
                            solution = document.Project.Solution;
                        }
                    }

                    var propertyDeclarations = root.DescendantNodesAndSelf()
                                   .OfType<PropertyDeclarationSyntax>()
                                   .Where(pd => pd.Type is IdentifierNameSyntax identifierNameSyntax &&
                                                identifierNameSyntax.Identifier.Text == classEntry.Key)
                                   .ToList();

                    foreach (var propertyDeclaration in propertyDeclarations)
                    {
                        if (propertyDeclaration.Type is IdentifierNameSyntax identifierNameSyntax &&
                            identifierNameSyntax.Identifier.Text == classEntry.Key)
                        {
                            // Создаем новый идентификатор с обфусцированным именем
                            var newIdentifier = SyntaxFactory.IdentifierName(classEntry.Value);

                            // Заменяем тип в PropertyDeclaration на новый обфусцированный идентификатор
                            var newPropertyDeclaration = propertyDeclaration.WithType(newIdentifier).NormalizeWhitespace();

                            // Обновляем корневой узел с новым PropertyDeclaration
                            root = root.ReplaceNode(propertyDeclaration, newPropertyDeclaration).NormalizeWhitespace();

                            // Обновляем документ и решение после замены
                            document = document.WithSyntaxRoot(root);
                            solution = document.Project.Solution;

                            // Обновляем проект и компиляцию после изменений
                            project = document.Project;
                            compilation = project.GetCompilationAsync().Result;
                        }
                    }



                    var castExpressions = root.DescendantNodesAndSelf()
                          .OfType<CastExpressionSyntax>()
                          .Where(ce => ce.Type.ToString() == classEntry.Key)
                          .ToList();

                    foreach (var castExpression in castExpressions)
                    {
                        var identifierNameSyntax = castExpression.Type as IdentifierNameSyntax;

                        if (identifierNameSyntax != null && identifierNameSyntax.Identifier.Text == classEntry.Key)
                        {
                            // Создаем новый идентификатор с обфусцированным именем
                            var newIdentifier = SyntaxFactory.IdentifierName(classEntry.Value);

                            // Заменяем тип в castExpression на новый обфусцированный идентификатор
                            var newCastExpression = castExpression.WithType(newIdentifier).NormalizeWhitespace();

                            // Обновляем корневой узел с новым castExpression
                            root = root.ReplaceNode(castExpression, newCastExpression);

                            // Обновляем документ и решение после замены
                            document = document.WithSyntaxRoot(root);
                            solution = document.Project.Solution;

                            //project = solution.GetProject(project.Id);
                            //compilation = project.GetCompilationAsync().Result;
                        }
                    }
                    
                    var attributes = root.DescendantNodesAndSelf()
                     .OfType<AttributeSyntax>()
                     .Where(attr => attr.ArgumentList?.Arguments.Any(arg =>
                        arg.Expression is TypeOfExpressionSyntax typeOfExpression &&
                        typeOfExpression.Type.ToString() == classEntry.Key) == true)
                     .ToList();


                    foreach (var attribute in attributes)
                    {
                        var typeOfExpressions = attribute.ArgumentList.Arguments
                            .Where(arg => arg.Expression is TypeOfExpressionSyntax typeOfExpression &&
                                          typeOfExpression.Type.ToString() == classEntry.Key)
                            .ToList();

                        foreach (var typeOfExpression in typeOfExpressions)
                        {
                            if (typeOfExpression.Expression is TypeOfExpressionSyntax typeOfExpr &&
                                typeOfExpr.Type is IdentifierNameSyntax identifierNameSyntax &&
                                identifierNameSyntax.Identifier.Text == classEntry.Key)
                            {
                                // Создаем новый идентификатор с обфусцированным именем
                                var newIdentifier = SyntaxFactory.IdentifierName(classEntry.Value);

                                // Создаем новый TypeOfExpressionSyntax с замененным типом
                                var newTypeOfExpression = typeOfExpr.WithType(newIdentifier).NormalizeWhitespace();

                                // Заменяем старый TypeOfExpressionSyntax на новый
                                root = root.ReplaceNode(typeOfExpr, newTypeOfExpression);

                                // Обновляем документ и решение после замены
                                document = document.WithSyntaxRoot(root);
                                solution = document.Project.Solution;

                                // Обновляем проект и компиляцию после изменений
                                project = document.Project;
                                compilation = project.GetCompilationAsync().Result;

                                // Обновляем root для последующих изменений
                                root = document.GetSyntaxRootAsync().Result;
                            }
                        }
                    }

                    var classDeclarations = root.DescendantNodesAndSelf()
                            .OfType<ClassDeclarationSyntax>()
                            .Where(c => c.Identifier.Text == classEntry.Key ||
                                        c.BaseList?.Types.Any(bt => bt.Type.ToString() == classEntry.Key) == true)
                            .ToList();

                    foreach (var classDeclaration in classDeclarations)
                    {
                        // Замена имени самого класса
                        if (classDeclaration.Identifier.Text == classEntry.Key)
                        {
                            var newIdentifier = SyntaxFactory.Identifier(classEntry.Value);
                            var newClassDeclaration = classDeclaration.WithIdentifier(newIdentifier).NormalizeWhitespace();
                            root = root.ReplaceNode(classDeclaration, newClassDeclaration);
                        }

                        // Замена базового класса в списке наследования
                        if (classDeclaration.BaseList != null)
                        {
                            foreach (var baseType in classDeclaration.BaseList.Types)
                            {
                                Console.WriteLine($"Checking base type: {baseType.Type}");

                                if (baseType.Type is IdentifierNameSyntax baseTypeName &&
                                    baseTypeName.Identifier.Text == classEntry.Key)
                                {
                                    Console.WriteLine($"Replacing base type {classEntry.Key} with {classEntry.Value}");
                                    var newBaseTypeIdentifier = SyntaxFactory.IdentifierName(classEntry.Value).NormalizeWhitespace();
                                    var newBaseType = baseType.WithType(newBaseTypeIdentifier);
                                    var newBaseList = classDeclaration.BaseList.ReplaceNode(baseType, newBaseType);
                                    var newClassDeclaration = classDeclaration.WithBaseList(newBaseList).NormalizeWhitespace();
                                    root = root.ReplaceNode(classDeclaration, newClassDeclaration);
                                }
                            }
                        }

                        // Обновляем документ и решение после замены
                        document = document.WithSyntaxRoot(root);
                        solution = document.Project.Solution;
                        project = solution.GetProject(project.Id);
                        compilation = project.GetCompilationAsync().Result;
                    }

                    // Обновление проекта после переименования
                    project = solution.GetProject(project.Id);
                    compilation = project.GetCompilationAsync().Result;
                    
                }
            }



            // Сохранение изменений в каждом файле
            foreach (var documentId in project.DocumentIds)
            {
                var document = solution.GetDocument(documentId);
                if (document != null)
                {
                    var newText = document.GetTextAsync().Result;
                    var filePath = document.FilePath;
                    File.WriteAllText(filePath, newText.ToString());
                }
            }

            // Сохранение изменений в файлах
            //workspace.TryApplyChanges(solution);
        }
    }
}

