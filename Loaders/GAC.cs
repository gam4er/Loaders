using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Loaders
{
    internal class GAC
    {
        public static IEnumerable<UsingDirectiveSyntax> ExtractUsings(SyntaxNode node)
        {
            var usings = new List<UsingDirectiveSyntax>();

            // Рекурсивно обходим узлы и извлекаем директивы using
            foreach (var child in node.ChildNodes())
            {
                if (child is UsingDirectiveSyntax usingDirective)
                {
                    usings.Add(usingDirective);
                }
                else
                {
                    usings.AddRange(ExtractUsings(child));
                }
            }

            return usings;
        }

        public static List<Assembly> FindAssemblyForNamespace(string namespaceName)
        {
            // Пытаемся найти сборку по неймспейсу в текущем домене
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetTypes().Any(t => t.Namespace == namespaceName))
                    {
                        return new List<Assembly> { assembly };
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                }
                catch (FileLoadException)
                {
                }
            }

            // Если сборка не найдена в текущем домене, ищем в GAC
            return FindAssemblyInGAC(namespaceName);
        }

        public static List<Assembly> FindAssemblyInGAC(string namespaceName)
        {
            var gacAssemblies = GetGACAssemblies().Where(n => !n.ToLower().Contains("resources") &&
                                                                !n.Contains("Version=2.0.0.0") &&
                                                                !n.Contains("Version=3.5.0.0") &&
                                                                !n.Contains("Version=3.0.0.0")).OrderByDescending(n => n);

            var a = gacAssemblies.Where(n => n.Contains(namespaceName)).ToList();
            if (a != null)
                try { return a.Select(ass => Assembly.ReflectionOnlyLoad(ass)).ToList(); }
                catch { }
            //return Assembly.ReflectionOnlyLoad(a.FirstOrDefault()) ;        

            List<Assembly> result = new List<Assembly>();

            foreach (var assemblyName in gacAssemblies)
            {
                try
                {
                    if (AssemblyContainsNamespace(assemblyName, namespaceName))
                    {
                        var assembly = Assembly.ReflectionOnlyLoad(assemblyName);
                        if (assembly.GetTypes().Any(t => t.Namespace == namespaceName))
                        {
                            result.Add(assembly);
                        }
                    }

                    /*
                    if (namespaceName.Split('.').All(n => assemblyName.Contains(n)))
                    {
                        var assembly = Assembly.ReflectionOnlyLoad(assemblyName);
                        if (assembly.GetTypes().Any(t => t.Namespace == namespaceName))
                        {
                            result.Add(assembly);
                        }
                    }
                    */
                }
                catch
                {
                    // Игнорируем ошибки загрузки сборки
                }
            }

            // Если сборка не найдена, возвращаем null
            return null;
        }

        public static IEnumerable<string> GetGACAssemblies()
        {
            List<string> assemblies = new List<string>();
            IAssemblyEnum pAssemblyEnum = null;
            IAssemblyName pAssemblyName = null;
            HRESULT hr = HRESULT.E_FAIL;

            hr = CreateAssemblyEnum(out pAssemblyEnum, IntPtr.Zero, null, ASM_CACHE_FLAGS.ASM_CACHE_GAC, IntPtr.Zero);
            if (hr == HRESULT.S_OK)
            {
                while (pAssemblyEnum.GetNextAssembly(IntPtr.Zero, out pAssemblyName, 0) == HRESULT.S_OK && pAssemblyName != null)
                {
                    int nSize = 260;
                    StringBuilder sbDisplayName = new StringBuilder(nSize);
                    hr = pAssemblyName.GetDisplayName(sbDisplayName, ref nSize, ASM_DISPLAY_FLAGS.ASM_DISPLAYF_FULL);
                    if (hr == HRESULT.S_OK)
                    {
                        assemblies.Add(sbDisplayName.ToString());
                    }
                }
                Marshal.ReleaseComObject(pAssemblyEnum);
            }

            return assemblies;


        }

        private static string GetAssemblyPath(string assemblyDisplayName)
        {
            IAssemblyName pAssemblyName = null;
            HRESULT hr = CreateAssemblyNameObject(out pAssemblyName, assemblyDisplayName, CREATE_ASM_NAME_OBJ_FLAGS.CANOF_PARSE_DISPLAY_NAME, IntPtr.Zero);
            if (hr != HRESULT.S_OK)
            {
                return null;
            }

            IAssemblyCache pAssemblyCache = null;
            hr = CreateAssemblyCache(out pAssemblyCache, 0);
            if (hr != HRESULT.S_OK)
            {
                return null;
            }

            int nPathLength = 260;
            StringBuilder sbPath = new StringBuilder(nPathLength);
            hr = pAssemblyCache.QueryAssemblyInfo(0, assemblyDisplayName, sbPath, ref nPathLength);
            if (hr != HRESULT.S_OK)
            {
                return null;
            }

            return sbPath.ToString();
        }

        private static bool AssemblyContainsNamespace(string assemblyPath, string targetNamespace)
        {
            try
            {
                var assemblyDefinition = Mono.Cecil.AssemblyDefinition.ReadAssembly(assemblyPath);
                foreach (var module in assemblyDefinition.Modules)
                {
                    foreach (var type in module.Types)
                    {
                        if (type.Namespace == targetNamespace)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading assembly '{assemblyPath}': {ex.Message}");
            }
            return false;
        }

        public enum HRESULT : int
        {
            S_OK = 0,
            S_FALSE = 1,
            E_NOINTERFACE = unchecked((int)0x80004002),
            E_NOTIMPL = unchecked((int)0x80004001),
            E_FAIL = unchecked((int)0x80004005),
        }

        [Guid("21b8916c-f28e-11d2-a473-00c04f8ef448")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAssemblyEnum
        {
            HRESULT GetNextAssembly(IntPtr pvReserved, out IAssemblyName ppName, int dwFlags);
            HRESULT Reset();
            HRESULT Clone(out IAssemblyEnum ppEnum);
        }

        public enum ASM_CACHE_FLAGS
        {
            ASM_CACHE_ZAP = 0x01,
            ASM_CACHE_GAC = 0x02,
            ASM_CACHE_DOWNLOAD = 0x04,
            ASM_CACHE_ROOT = 0x08,
            ASM_CACHE_ROOT_EX = 0x80
        }

        [DllImport("Fusion.dll", SetLastError = true)]
        public static extern HRESULT CreateAssemblyEnum(out IAssemblyEnum pEnum, IntPtr pUnkReserved, IAssemblyName pName, ASM_CACHE_FLAGS dwFlags, IntPtr pvReserved);

        [Guid("CD193BC0-B4BC-11d2-9833-00C04FC31D2E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAssemblyName
        {
            HRESULT SetProperty(int PropertyId, IntPtr pvProperty, int cbProperty);
            HRESULT GetProperty(int PropertyId, out IntPtr pvProperty, ref int pcbProperty);
            HRESULT Finalize();
            HRESULT GetDisplayName(StringBuilder szDisplayName, ref int pccDisplayName, ASM_DISPLAY_FLAGS dwDisplayFlags);
            HRESULT Reserved([In, MarshalAs(UnmanagedType.LPStruct)] Guid refIID, IntPtr pUnkReserved1, IntPtr pUnkReserved2, string szReserved, UInt64 llReserved, IntPtr pvReserved, int cbReserved, out IntPtr ppReserved);
            HRESULT GetName(ref int lpcwBuffer, StringBuilder pwzName);
            HRESULT GetVersion(out int pdwVersionHi, out int pdwVersionLow);
            HRESULT IsEqual(IAssemblyName pName, int dwCmpFlags);
            HRESULT Clone(out IAssemblyName pName);
        }

        public enum ASM_DISPLAY_FLAGS
        {
            ASM_DISPLAYF_VERSION = 0x1,
            ASM_DISPLAYF_CULTURE = 0x2,
            ASM_DISPLAYF_PUBLIC_KEY_TOKEN = 0x4,
            ASM_DISPLAYF_PUBLIC_KEY = 0x8,
            ASM_DISPLAYF_CUSTOM = 0x10,
            ASM_DISPLAYF_PROCESSORARCHITECTURE = 0x20,
            ASM_DISPLAYF_LANGUAGEID = 0x40,
            ASM_DISPLAYF_RETARGET = 0x80,
            ASM_DISPLAYF_CONFIG_MASK = 0x100,
            ASM_DISPLAYF_MVID = 0x200,
            ASM_DISPLAYF_CONTENT_TYPE = 0x400,
            ASM_DISPLAYF_FULL = (((((ASM_DISPLAYF_VERSION | ASM_DISPLAYF_CULTURE) | ASM_DISPLAYF_PUBLIC_KEY_TOKEN) | ASM_DISPLAYF_RETARGET) | ASM_DISPLAYF_PROCESSORARCHITECTURE) | ASM_DISPLAYF_CONTENT_TYPE)
        }

        [DllImport("Fusion.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern HRESULT CreateAssemblyNameObject(out IAssemblyName ppAssemblyNameObj, [MarshalAs(UnmanagedType.LPWStr)] string szAssemblyName, CREATE_ASM_NAME_OBJ_FLAGS flags, IntPtr pvReserved);

        public enum CREATE_ASM_NAME_OBJ_FLAGS
        {
            CANOF_PARSE_DISPLAY_NAME = 0x1,
            CANOF_SET_DEFAULT_VALUES = 0x2,
            CANOF_VERIFY_FRIEND_ASSEMBLYNAME = 0x4,
            CANOF_PARSE_FRIEND_DISPLAY_NAME = (CANOF_PARSE_DISPLAY_NAME | CANOF_VERIFY_FRIEND_ASSEMBLYNAME)
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("7c23ff90-33af-11d3-95da-00a024a85b51")]
        public interface IApplicationContext { }

        [DllImport("Fusion.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern HRESULT CreateAssemblyNameObject(out IAssemblyName ppAssemblyNameObj, [MarshalAs(UnmanagedType.LPWStr)] string szAssemblyName, int flags, IntPtr pvReserved);

        [DllImport("Fusion.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern HRESULT CreateAssemblyCache(out IAssemblyCache ppAsmCache, int dwReserved);

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("E707DCDE-D1CD-11D2-BAB9-00C04F8ECEAE")]
        public interface IAssemblyCache
        {
            HRESULT UninstallAssembly(int dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pszAssemblyName, IntPtr pRefData, IntPtr pulDisposition);
            HRESULT QueryAssemblyInfo(int dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pszAssemblyName, StringBuilder pAsmInfo, ref int pccBuffer);
            HRESULT CreateAssemblyCacheItem(int dwFlags, IntPtr pvReserved, out IntPtr ppAsmItem, [MarshalAs(UnmanagedType.LPWStr)] string pszAssemblyName);
            HRESULT CreateAssemblyScavenger(out object ppAsmScavenger);
            HRESULT InstallAssembly(int dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pszManifestFilePath, IntPtr pvReserved);
        }
    }
}
