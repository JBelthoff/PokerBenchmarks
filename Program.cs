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
            //CPlusPlusBench.Run();
            
            // Full 9 player table
            //Op9Bench.Run(10_000_000);

        }
    }
}
