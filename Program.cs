using BenchmarkDotNet.Running; 

namespace PokerBenchmarks
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // BenchmarkDotNet
            BenchmarkRunner.Run<FinalRiverBench>();

            // C++  Compare
            //CPlusPlusBench.Run(5);
            //Console.WriteLine("\n\n==============================================\n");
            //CPlusPlusBench.Run(7);

        }
    }
}
