using System;
using System.Runtime.InteropServices;

namespace Fixture.Scope
{
    public delegate int FixtureDelegate(int sourceValue);

    public enum FixtureMode
    {
        FirstChoice,
        SecondChoice
    }

    public interface IWorkerContract
    {
        int ContractMethod(int contractValue);
    }

    public struct FixtureStruct
    {
        public int StructField;
    }

    public sealed class Worker : IWorkerContract
    {
        public event EventHandler WorkCompleted;
        public int PublicField;
        public int CountProperty { get; set; }

        public int ContractMethod(int contractValue)
        {
            return contractValue;
        }

        public int Calculate(int inputValue)
        {
            var castedWorker = (Worker)this;
            int staticValue = StaticFactory(inputValue);
            var literalValue = "alpha";
            var createdStruct = new FixtureStruct();
            FixtureMode selectedMode = FixtureMode.FirstChoice;
            return castedWorker.CountProperty + staticValue + literalValue.Length + createdStruct.StructField + (int)selectedMode;
        }

        public override string ToString()
        {
            return CountProperty.ToString();
        }

        public void Dispose()
        {
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private static int StaticFactory(int factoryInput)
        {
            return factoryInput + 1;
        }
    }

    internal static class Program
    {
        private static void Main(string[] args)
        {
            var worker = new Worker();
            worker.CountProperty = 1;
            Console.WriteLine(worker.Calculate(args.Length));
        }
    }
}
