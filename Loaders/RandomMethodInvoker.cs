using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using System.Reflection;
using System.Text;

namespace Loaders
{
    /// <summary>
    /// Generates small, harmless blocks of reflective invocation code
    /// that can be injected into obfuscated methods as noise.
    ///
    /// The generated code wraps a random instance method call from a
    /// limited set of safe framework assemblies in nested try/catch and
    /// Task.Run so failures are silently ignored.
    /// </summary>
    internal class RandomMethodInvoker
    {
        private static readonly Random _random = new Random();

        public static string RandomMethod()
        {
            // Pick a starting assembly from a small set of stable framework types.
            var candidateAssemblies = new[]
            {
                typeof(System.IO.Directory).Assembly,
                typeof(System.Text.StringBuilder).Assembly,
                typeof(System.Diagnostics.Process).Assembly
            };

            var assembly = candidateAssemblies[_random.Next(candidateAssemblies.Length)];

            // Получение всех типов в сборке
            var types = assembly.GetTypes();
            Type type = null;

            List<MethodInfo> methods = new List<MethodInfo>();

            do
            {
                // Выбор случайного типа из сборки
                type = types[_random.Next(types.Length)];

                // Пропускаем типы, которые не могут быть использованы
                if (type.IsAbstract ||
                    type.IsInterface ||
                    type.IsNotPublic ||
                    type.IsGenericType ||
                    !type.GetConstructors().Any(c => c.GetParameters().Length == 0) ||
                    IsInfrastructureType(type))
                {
                    continue; // Пропускаем недопустимые типы
                }

                if (!IsValidType(type))
                {
                    continue;
                }

                // Получение всех методов у выбранного типа
                methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                              .Where(IsValidMethod)
                              .ToList();

            } while (methods.Count == 0);

            // Выбор случайного метода
            var method = methods[_random.Next(methods.Count)];
            var methodName = method.Name;

            // Генерация случайных аргументов для метода
            var arguments = GenerateRandomArguments(method.GetParameters());

            // Формирование строки вызова метода в блоке try/catch с Task.Run
            var code = GenerateInvocationCode(type, methodName, arguments);

            // Возвращаем сгенерированный безвредный фрагмент вызова
            return code;
        }

        private static object[] GenerateRandomArguments(ParameterInfo[] parameters)
        {
            var args = new List<object>();

            foreach (var param in parameters)
            {
                var paramType = param.ParameterType;

                if (paramType == typeof(int) || paramType == typeof(int?))
                {
                    args.Add(_random.Next(0, 100));
                }
                else if (paramType == typeof(string))
                {
                    args.Add("\"" + Guid.NewGuid().ToString() + "\"");
                }
                else if (paramType == typeof(bool) || paramType == typeof(bool?))
                {
                    args.Add(_random.Next(0, 2) == 0 ? "false" : "true");
                }
                else if (paramType == typeof(double) || paramType == typeof(double?))
                {
                    args.Add(_random.NextDouble().ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (paramType == typeof(long) || paramType == typeof(long?))
                {
                    args.Add(((long)_random.Next(0, 1000000)).ToString());
                }
                else if (paramType == typeof(float) || paramType == typeof(float?))
                {
                    args.Add(((float)_random.NextDouble()).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "f");
                }
                else if (paramType == typeof(char) || paramType == typeof(char?))
                {
                    args.Add("'" + (char)_random.Next('a', 'z') + "'");
                }
                else if (paramType == typeof(byte) || paramType == typeof(byte?))
                {
                    args.Add((byte)_random.Next(0, 256));
                }
                else if (paramType == typeof(sbyte) || paramType == typeof(sbyte?))
                {
                    args.Add((sbyte)_random.Next(-128, 127));
                }
                else if (paramType == typeof(short) || paramType == typeof(short?))
                {
                    args.Add((short)_random.Next(short.MinValue, short.MaxValue));
                }
                else if (paramType == typeof(ushort) || paramType == typeof(ushort?))
                {
                    args.Add((ushort)_random.Next(0, ushort.MaxValue));
                }
                else if (paramType == typeof(uint) || paramType == typeof(uint?))
                {
                    args.Add((uint)_random.Next(0, int.MaxValue));
                }
                else if (paramType == typeof(ulong) || paramType == typeof(ulong?))
                {
                    args.Add((ulong)(_random.Next(0, int.MaxValue) * 2L));
                }
                else if (paramType == typeof(decimal) || paramType == typeof(decimal?))
                {
                    args.Add(((decimal)_random.NextDouble()).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "m");
                }
                else
                {
                    // Fallback: use default(T) by emitting a cast expression in code.
                    args.Add($"default({GetCSharpTypeName(paramType)})");
                }
            }

            return args.ToArray();
        }

        private static string GenerateInvocationCode(Type type, string methodName, object[] arguments)
        {
            // Получаем полное имя типа и заменяем "+" на "."
            string typeName = GetCSharpTypeName(type);

            // Проверка на обобщенный тип
            if (type.IsGenericType)
            {
                // Форматируем имя типа, убирая символы `1, `2 и т.д., используемые для обозначения обобщенных типов
                typeName = typeName.Split('`')[0];

                // Получение имен аргументов обобщенного типа
                var genericArgs = string.Join(", ", type.GetGenericArguments().Select(t =>
                {
                    // Используем FullName для конкретных типов, иначе используем Name для параметров типа
                    return GetCSharpTypeName(t);
                }));

                // Формирование полного имени типа с обобщенными аргументами
                typeName = $"{typeName}<{genericArgs}>";
            }

            string args = string.Join(", ", arguments);

            return $@"#pragma warning disable CS0618
try
{{
    global::System.Threading.Tasks.Task.Run(() =>
    {{
        try
        {{
            {typeName} instance = new {typeName}();
            instance.{methodName}({args});
        }}
        catch (global::System.Exception)
        {{
        }}
    }}).Start();
}}
catch (global::System.Exception)
{{
}}
#pragma warning restore CS0618";
        }

        private static string GetCSharpTypeName(Type type)
        {
            if (type == null)
            {
                return "object";
            }

            if (type.IsByRef)
            {
                type = type.GetElementType();
            }

            if (type == typeof(void))
            {
                return "void";
            }

            if (type.IsArray)
            {
                return GetCSharpTypeName(type.GetElementType()) + "[]";
            }

            var fullName = type.FullName;
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return type.Name;
            }

            return "global::" + fullName.Replace('+', '.');
        }

        private static bool IsValidType(Type type)
        {
            // Skip generic and non-public types to reduce the chance of
            // hitting unexpected or security-sensitive APIs.
            return !type.IsGenericType && type.IsPublic;
        }

        private static bool IsInfrastructureType(Type type)
        {
            var fullName = type.FullName ?? string.Empty;

            // Heuristically exclude infrastructure types that are known to be
            // backed by obsolete or internal-only APIs, in particular some
            // System.Net HTTP types.
            if (fullName.StartsWith("System.Net.HttpWebRequest", StringComparison.Ordinal) ||
                fullName.StartsWith("System.Net.HttpWebResponse", StringComparison.Ordinal) ||
                fullName.StartsWith("System.Net.HttpListener", StringComparison.Ordinal) ||
                fullName.StartsWith("System.Configuration.", StringComparison.Ordinal) ||
                fullName.IndexOf(".Configuration.", StringComparison.Ordinal) >= 0 ||
                fullName.StartsWith("System.Net.Configuration.", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static bool IsValidMethod(MethodInfo method)
        {
            // We only want instance methods with parameters we can safely
            // generate literals for (primitives, nullable primitives, string).
            if (!method.IsPublic || method.IsStatic || method.IsConstructor)
            {
                return false;
            }

            if (method.ContainsGenericParameters || method.IsGenericMethod)
            {
                return false;
            }

            if (method.Name.StartsWith("get_", StringComparison.Ordinal) ||
                method.Name.StartsWith("set_", StringComparison.Ordinal) ||
                method.Name.StartsWith("add_", StringComparison.Ordinal) ||
                method.Name.StartsWith("remove_", StringComparison.Ordinal) ||
                method.Name.StartsWith("op_", StringComparison.Ordinal) ||
                method.Name.StartsWith("get_Item", StringComparison.Ordinal) ||
                method.Name.StartsWith("set_Item", StringComparison.Ordinal))
            {
                return false;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return false;
            }

            // Reject whole overload families where any overload uses string/char
            // parameters, because our simple literal generation can be ambiguous
            // across those overloads.
            var overloads = method.DeclaringType?.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Where(m => m.Name == method.Name) ?? Enumerable.Empty<MethodInfo>();

            foreach (var overload in overloads)
            {
                foreach (var p in overload.GetParameters())
                {
                    var ot = p.ParameterType;
                    if (ot == typeof(string) || ot == typeof(char))
                    {
                        return false;
                    }
                }
            }

            // Now ensure all parameters on the chosen method are from the set
            // we know how to generate consistent literals for.
            foreach (var p in parameters)
            {
                var t = p.ParameterType;

                if (t == typeof(int) || t == typeof(int?) ||
                    t == typeof(string) ||
                    t == typeof(bool) || t == typeof(bool?) ||
                    t == typeof(double) || t == typeof(double?) ||
                    t == typeof(long) || t == typeof(long?) ||
                    t == typeof(float) || t == typeof(float?) ||
                    t == typeof(char) || t == typeof(char?) ||
                    t == typeof(byte) || t == typeof(byte?) ||
                    t == typeof(sbyte) || t == typeof(sbyte?) ||
                    t == typeof(short) || t == typeof(short?) ||
                    t == typeof(ushort) || t == typeof(ushort?) ||
                    t == typeof(uint) || t == typeof(uint?) ||
                    t == typeof(ulong) || t == typeof(ulong?) ||
                    t == typeof(decimal) || t == typeof(decimal?))
                {
                    continue;
                }

                // Reject everything else (complex, generic or framework-internal types).
                return false;
            }

            return true;
        }
    }
}


