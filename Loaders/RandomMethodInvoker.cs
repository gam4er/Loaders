using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Loaders
{
    internal class RandomMethodInvoker
    {

        public static string RandomMethod()
        {
            // Загрузка сборки System.DirectoryServices
            var assembly = typeof(System.IO.Directory).Assembly;

            // Получение всех типов в сборке
            var types = assembly.GetTypes();
            Type type = null;

            List<MethodInfo> methods = new List<MethodInfo>();
            var random = new Random();

            do {
                // Выбор случайного типа из сборки
                type = types [random.Next(types.Length)];

                // Пропускаем типы, которые не могут быть использованы
                if (type.IsAbstract ||
                    type.IsInterface ||
                    type.IsNotPublic ||
                    type.IsGenericType || // Исключаем обобщенные типы
                    !type.GetConstructors().Any(c => c.GetParameters().Length == 0))
                {
                    continue; // Пропускаем недопустимые типы
                }

                if (!IsValidType(type))
                    continue;

                // Получение всех методов у выбранного типа
                methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                              .Where(m => m.GetParameters().All(p => p.ParameterType.IsValueType || p.ParameterType.IsGenericType)
                                    && m.GetParameters().Length > 0)
                              .Where(IsValidMethod)
                              .Where(m => m.IsPublic) // Фильтрация только публичных методов
                              .Where(m => !m.IsStatic) // Исключаем статические методы
                              .Where(m => !m.Name.StartsWith("set_Item") && !m.Name.StartsWith("get_Item")) // Исключаем индексаторы
                              .Where(m => !m.Name.StartsWith("op_")) // Исключаем операторы
                              .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")) // Исключаем методы доступа к полям
                              .Where(m => !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_")) // Исключаем методы событий
                              .Where(m => !m.IsConstructor) // Исключаем конструкторы
                              .Where(m => !m.GetParameters().Any(p => // Исключаем методы с типами аргументов, связанными с сборками
                                    p.ParameterType == typeof(System.Type) ||
                                    p.ParameterType == typeof(System.Reflection.Assembly) ||
                                    p.ParameterType == typeof(System.Reflection.Module) ||
                                    p.ParameterType == typeof(System.Reflection.MemberInfo) ||
                                    p.ParameterType == typeof(System.Delegate) ||
                                    p.ParameterType.FullName?.StartsWith("System.Func`") == true ||
                                    p.ParameterType.FullName?.StartsWith("System.Action`") == true ||
                                    p.ParameterType.FullName?.Contains("`") == true ||
                                    p.ParameterType.FullName?.Contains("+") == true ||
                                    p.ParameterType.FullName?.Contains("Func") == true || // Исключаем методы с типами, содержащими Func<>
                                    p.ParameterType.FullName?.Contains("Action") == true)) // Исключаем методы с типами, содержащими Action<>
                              .ToList();

            } while (methods.Count == 0);

            // Выбор случайного метода
            var method = methods [random.Next(methods.Count)];
            var methodName = method.Name;

            // Генерация случайных аргументов для метода
            var arguments = GenerateRandomArguments(method.GetParameters());

            // Формирование строки вызова метода в блоке try/catch с Task.Run
            var code = GenerateInvocationCode(type, methodName, arguments);

            // Вывод кода на экран
            return code;
        }

        private static object [] GenerateRandomArguments(ParameterInfo [] parameters)
        {
            var random = new Random();
            var args = new List<object>();

            foreach (var param in parameters)
            {
                if (param.ParameterType == typeof(int))
                {
                    args.Add(random.Next(0, 100));
                }
                else if (param.ParameterType == typeof(string))
                {
                    args.Add("\"" + Guid.NewGuid().ToString() + "\""); // Случайная строка
                }
                else if (param.ParameterType == typeof(bool))
                {
                    args.Add(random.Next(0, 2) == 0 ? "false" : "true");
                }
                else if (param.ParameterType == typeof(double))
                {
                    args.Add(random.NextDouble().ToString("F2"));
                }
                else if (param.ParameterType == typeof(long))
                {
                    args.Add(((long)random.Next(0, 1000000)).ToString());
                }
                else if (param.ParameterType == typeof(float))
                {
                    args.Add(((float)random.NextDouble()).ToString("F2"));
                }
                else if (param.ParameterType == typeof(char))
                {
                    args.Add("'" + (char)random.Next('a', 'z') + "'");
                }
                else if (param.ParameterType == typeof(byte))
                {
                    args.Add((byte)random.Next(0, 256));
                }
                else if (param.ParameterType == typeof(sbyte))
                {
                    args.Add((sbyte)random.Next(-128, 127));
                }
                else if (param.ParameterType == typeof(short))
                {
                    args.Add((short)random.Next(-32768, 32767));
                }
                else if (param.ParameterType == typeof(ushort))
                {
                    args.Add((ushort)random.Next(0, 65535));
                }
                else if (param.ParameterType == typeof(uint))
                {
                    args.Add((uint)random.Next(0, int.MaxValue));
                }
                else if (param.ParameterType == typeof(ulong))
                {
                    args.Add((ulong)(random.Next(0, int.MaxValue) * 2L));
                }
                else if (param.ParameterType == typeof(decimal))
                {
                    args.Add(((decimal)random.NextDouble()).ToString("F2"));
                }
                else
                {
                    // Создание значения по умолчанию для других типов
                    args.Add($"new {param.ParameterType.FullName}()");
                }
            }

            return args.ToArray();
        }

        private static string GenerateInvocationCode(Type type, string methodName, object [] arguments)
        {
            // Получаем полное имя типа и заменяем "+" на "."
            string typeName = type.FullName.Replace('+', '.');

            // Проверка на обобщенный тип
            if (type.IsGenericType)
            {
                // Форматируем имя типа, убирая символы `1, `2 и т.д., используемые для обозначения обобщенных типов
                typeName = typeName.Split('`') [0];

                // Получение имен аргументов обобщенного типа
                var genericArgs = string.Join(", ", type.GetGenericArguments().Select(t =>
                {
                    // Используем FullName для конкретных типов, иначе используем Name для параметров типа
                    return t.FullName != null ? t.FullName.Replace('+', '.') : t.Name;
                }));

                // Формирование полного имени типа с обобщенными аргументами
                typeName = $"{typeName}<{genericArgs}>";
            }

            string args = string.Join(", ", arguments);

            return $@"
try
{{
    Task.Run(() =>
    {{
        try
        {{
            {typeName} instance = new {typeName}();
            instance.{methodName}({args});
        }}
        catch (Exception)
        {{
        }}
    }}).Start();
}}
catch (Exception)
{{
}}";
        }

        private static bool IsValidType(Type type)
        {
            // Пропускаем типы с типовыми параметрами (generics) и недоступные типы
            return !type.IsGenericType && type.IsPublic;
        }
        private static bool IsValidMethod(MethodInfo method)
        {
            // Пропускаем методы с типовыми параметрами или недоступные методы
            return method.IsPublic &&
                   !method.ContainsGenericParameters && // Исключаем методы с типовыми параметрами
                   !method.IsGenericMethod && // Исключаем обобщенные методы
                   !method.GetParameters().Any(p => // Пропускаем методы с недопустимыми типами параметров
                       p.ParameterType.IsGenericType || // Исключаем типы с типовыми параметрами
                       p.ParameterType.IsNotPublic || // Исключаем непубличные типы
                       p.ParameterType.FullName?.Contains("`") == true || // Исключаем обобщенные типы
                       p.ParameterType.FullName?.Contains("+") == true); // Исключаем вложенные типы
        }

    }
}


