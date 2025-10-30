// File: CPlusPlus.cs
// Goal: Reproduce the structure/output of Benchmark.cpp entirely in C# for apples-to-apples C++ vs C# comparisons.
// Notes:
//  - Uses your Kev/Suffecool-encoded VALUES and PokerLib evaluator (Eval5/Eval5With/Perm7Indices/HandRankJb).
//  - No checksum matching required (we still sum to prevent dead-code elimination).
//  - Generates unique-card hands using a per-thread partial Fisher–Yates; avoids giant hand arrays to keep memory down.
//  - Switch CARD_COUNT below between 5 and 7 to match your C++ compile-time flag.
//
// References to your codebase used here:
//   - Benchmark.cpp flow & formatting (C++ reference).  [see: /mnt/data/Benchmark.cpp]        ← mirroring flow/prints
//   - FinalRiverBench uses Perm7Indices & values-only eval patterns.                            ← API parity
//   - PokerLib tables & helpers: Eval5, Eval5With, Perm7Indices, HandRankJb, Flushes/Unique5.  ← hot path eval
//
// (c) PokerBenchmarks – C# parity runner.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using poker.net.Interfaces;   // StaticDeckService (same as in FinalRiverBench)
using poker.net.Models;       // Card
using poker.net.Services;     // StaticDeckService, PokerLib

namespace PokerBenchmarks
{

    public static class CPlusPlusBench
    {
        // ===== Configuration (match your C++ build) =====
        private static int CARD_COUNT = 7; // set via Run(cardCount)

        private static readonly (long Count, string Label)[] FiveCardConfigs = new[]
        {
            (50_000_000L,  "50M hands"),
            (100_000_000L, "100M hands"),
            (500_000_000L, "500M hands"),
            (1_000_000_000L,"1B hands")
        };

        private static readonly (long Count, string Label)[] SevenCardConfigs = new[]
        {
            (5_000_000L,   "5M hands"),
            (10_000_000L,  "10M hands"),
            (50_000_000L,  "50M hands"),
            (100_000_000L, "100M hands")
        };

        // C++ value_str equivalent (index 1..9 used)
        private static readonly string[] ValueStr =
        {
            "", "Straight Flush","Four of a Kind","Full House",
            "Flush","Straight","Three of a Kind","Two Pair","One Pair","High Card"
        };

        // Deck (Kev encoding VALUES) cached for all threads
        private static int[] _deckValues = Array.Empty<int>();
        private static volatile int _deckReady; // 0/1

        // Tables for fast 5-card eval in 7-card best-of-21
        private static ReadOnlySpan<ushort> _flushes => PokerLib.Flushes;
        private static ReadOnlySpan<ushort> _unique5 => PokerLib.Unique5;
        private static ReadOnlySpan<ushort> _hashes => PokerLib.HashValues;
        private static ReadOnlySpan<byte> _perm7 => PokerLib.Perm7Indices; // flattened 21x5

        // Thread-local RNG (xorshift64*) + scratch buffers
        [ThreadStatic] private static ulong _rngState;
        [ThreadStatic] private static int[] _indices;          // 0..51 indices for partial shuffle
        [ThreadStatic] private static int[] _handBuffer;       // temp VALUES (length 7)
        [ThreadStatic] private static int[] _fiveWork;         // temp 5 for 5-card eval (if needed)

        public static void Run(int cardCount)
        {
            CARD_COUNT = cardCount;
            EnsureDeck();

            Console.WriteLine($"=== {CARD_COUNT}-Card Poker Hand Evaluator Benchmark ===\n");

            // Warm-up (10k)
            Console.WriteLine("Warming up...");
            Warmup(10_000);

            // Single-thread 10M
            Console.WriteLine($"\n=== SINGLE-THREADED {CARD_COUNT}-Card Test ===");
            SingleThread(10_000_000);

            // Parallel configs
            var configs = CARD_COUNT == 5 ? FiveCardConfigs : SevenCardConfigs;
            foreach (var (count, label) in configs)
            {
                Console.WriteLine($"\n--- {label} ---");
                Console.WriteLine($"Generating {count:n0} random {CARD_COUNT}-card hands...");
                Console.WriteLine("Evaluating...");
                ParallelEvaluate(count);
            }

            // Distribution (100k)
            Console.WriteLine("\n\n=== Hand Distribution Check (100K hands) ===");
            Distribution(100_000);
            if (CARD_COUNT == 5)
            {
                Console.WriteLine("\nExpected 5-card probabilities (for reference):");
                Console.WriteLine("  Straight Flush: ~0.0015%");
                Console.WriteLine("  Four of a Kind: ~0.024%");
                Console.WriteLine("  Full House:     ~0.144%");
                Console.WriteLine("  Flush:          ~0.197%");
                Console.WriteLine("  Straight:       ~0.392%");
                Console.WriteLine("  Three of a Kind:~2.11%");
                Console.WriteLine("  Two Pair:       ~4.75%");
                Console.WriteLine("  One Pair:       ~42.3%");
                Console.WriteLine("  High Card:      ~50.1%");
            }
            else
            {
                Console.WriteLine("\nExpected 7-card probabilities (for reference):");
                Console.WriteLine("  Straight Flush: ~0.03%");
                Console.WriteLine("  Four of a Kind: ~0.17%");
                Console.WriteLine("  Full House:     ~2.60%");
                Console.WriteLine("  Flush:          ~3.03%");
                Console.WriteLine("  Straight:       ~4.62%");
                Console.WriteLine("  Three of a Kind:~4.83%");
                Console.WriteLine("  Two Pair:       ~23.5%");
                Console.WriteLine("  One Pair:       ~43.8%");
                Console.WriteLine("  High Card:      ~17.4%");
            }
        }

        // ---------- Core flows ----------

        private static void Warmup(int count)
        {
            ulong total = 0;
            for (int i = 0; i < count; i++)
            {
                var val = EvaluateOneRandomHand();
                total += val;
            }
            GC.KeepAlive(total);
        }

        private static void SingleThread(int count)
        {
            var sw = Stopwatch.StartNew();
            ulong total = 0;
            for (int i = 0; i < count; i++)
            {
                total += EvaluateOneRandomHand();
            }
            sw.Stop();

            var secs = sw.Elapsed.TotalSeconds;
            var mhps = count / secs / 1_000_000.0;

            Console.WriteLine($"Single-thread: {secs:0.0000}s, {mhps:0.00}M hands/sec");
            Console.WriteLine($"Checksum: {total}");
        }

        private static void ParallelEvaluate(long count)
        {
            var sw = Stopwatch.StartNew();
            long chunk = 1_000_000; // reasonable granularity
            int procs = Environment.ProcessorCount;
            long parts = Math.Max(procs * 16, 1);
            long perPart = Math.Max(count / parts, 1);
            long remaining = count;

            ulong grandTotal = 0;

            Parallel.For(0, (int)parts, new ParallelOptions { MaxDegreeOfParallelism = procs }, _ =>
            {
                InitThread();
                ulong local = 0;
                long take = Math.Min(perPart, InterlockedAddClamp(ref remaining, -perPart, count));
                while (take > 0)
                {
                    long todo = Math.Min(take, chunk);
                    for (long i = 0; i < todo; i++)
                        local += EvaluateOneRandomHand();
                    take -= todo;
                }
                InterlockedAdd(ref grandTotal, local);
            });

            sw.Stop();
            double sec = sw.Elapsed.TotalSeconds;
            double hps = count / sec;
            double nsPer = (sw.Elapsed.TotalMilliseconds * 1_000_000.0) / count;

            Console.WriteLine("\nResults:");
            Console.WriteLine($"  Total hands evaluated: {count:n0}");
            Console.WriteLine($"  Elapsed time: {sec:0.0000} seconds");
            Console.WriteLine($"  Hands per second: {hps:0}");
            Console.WriteLine($"  Million hands/sec: {hps / 1_000_000.0:0.00}M");
            Console.WriteLine($"  Nanoseconds per hand: {nsPer:0.00} ns");
            Console.WriteLine($"  Microseconds per hand: {nsPer / 1000.0:0.00} us");
            Console.WriteLine($"  Checksum (prevent optimization): {grandTotal}");
        }

        private static void Distribution(int count)
        {
            int[] freq = new int[10];
            for (int i = 0; i < count; i++)
            {
                ushort v = EvaluateOneRandomHand();
                int rank = PokerLib.HandRankJb(v);
                if ((uint)rank < freq.Length) freq[rank]++;
            }

            Console.WriteLine("\nHand type distribution:");
            for (int i = 1; i <= 9; i++)
            {
                double pct = freq[i] * 100.0 / count;
                Console.WriteLine($"  {ValueStr[i],15}: {freq[i],6} ({pct,5:0.00}%)");
            }
        }

        // ---------- Per-hand evaluation ----------

        // Evaluates one random unique-card hand (5 or 7 cards, depending on CARD_COUNT).
        // Uses thread-local buffers and partial Fisher–Yates to avoid allocations.
        private static ushort EvaluateOneRandomHand()
        {
            InitThread();

            // Draw N unique cards via partial shuffle against thread-local indices[]
            // Fill the thread-local hand buffer with VALUES for eval.
            int n = CARD_COUNT;
            EnsureHandBuffers(n);
            DrawUnique(n, _handBuffer);

            if (n == 5)
            {
                // Fast 5-card path
                return PokerLib.Eval5(_handBuffer.AsSpan(0, 5));
            }
            else
            {
                // 7-card best-of-21 path using flattened perm7 & tables (values-only, no allocs)
                ushort best = ushort.MaxValue;
                var vals = _handBuffer; // length 7
                var perm = _perm7;      // 21 rows x 5 cols (flattened)

                for (int row = 0; row < 21; row++)
                {
                    int j = row * 5;

                    int v0 = vals[perm[j + 0]];
                    int v1 = vals[perm[j + 1]];
                    int v2 = vals[perm[j + 2]];
                    int v3 = vals[perm[j + 3]];
                    int v4 = vals[perm[j + 4]];

                    ushort score = PokerLib.Eval5With(_flushes, _unique5, _hashes, v0, v1, v2, v3, v4);
                    if (score < best) best = score;
                }
                return best;
            }
        }

        // ---------- Thread/Deck helpers ----------

        private static void EnsureDeck()
        {
            if (Interlocked.CompareExchange(ref _deckReady, 1, 0) == 0)
            {
                // Load Kev-encoded deck VALUES using same deck service pattern as FinalRiverBench
                var svc = new StaticDeckService(); // synchronous bridge for this harness
                var deck = svc.RawDeckAsync().GetAwaiter().GetResult(); // returns IEnumerable<Card>
                var vals = new int[52];
                int i = 0;
                foreach (var c in deck) vals[i++] = c.Value;
                _deckValues = vals;
            }
        }

        private static void InitThread()
        {
            if (_rngState == 0)
            {
                // Seed from managed Thread ID & high-res timestamp
                ulong s = (ulong)Environment.TickCount64 ^ (ulong)Thread.CurrentThread.ManagedThreadId;
                _rngState = (s == 0) ? 0x9E3779B97F4A7C15UL : s;
            }
            _indices ??= CreateIndices();
        }

        private static int[] CreateIndices()
        {
            var a = new int[52];
            for (int i = 0; i < 52; i++) a[i] = i;
            return a;
        }

        private static void EnsureHandBuffers(int n)
        {
            _handBuffer ??= new int[7];
            _fiveWork ??= new int[5];
        }

        // Draw N unique cards via partial Fisher–Yates; write VALUES into dst[0..N-1]
        private static void DrawUnique(int n, int[] dst)
        {
            // reset thread-local indices array to 0..51
            for (int i = 0; i < 52; i++) _indices[i] = i;

            for (int j = 0; j < n; j++)
            {
                int k = j + (int)(NextUInt() % (uint)(52 - j)); // pick in [j..51]
                int swap = _indices[k];
                _indices[k] = _indices[j];
                _indices[j] = swap;
                dst[j] = _deckValues[_indices[j]];
            }
        }

        // xorshift64* RNG
        private static ulong NextULong()
        {
            ulong x = _rngState;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _rngState = x;
            return x * 0x2545F4914F6CDD1Dul;
        }

        private static uint NextUInt() => (uint)(NextULong() >> 32);

        // Interlocked helpers
        private static void InterlockedAdd(ref ulong target, ulong value)
        {
            ulong initial, computed;
            do
            {
                initial = Volatile.Read(ref target);
                computed = initial + value;
            } while (Interlocked.CompareExchange(ref target, computed, initial) != initial);
        }

        private static long InterlockedAddClamp(ref long target, long delta, long floorZeroForReturn)
        {
            // Reserve 'delta' work; if target already 0, return 0.
            long initial, updated;
            do
            {
                initial = Volatile.Read(ref target);
                if (initial <= 0) return 0;
                long take = Math.Min(initial, -delta); // reserve positive amount
                updated = initial - take;
            } while (Interlocked.CompareExchange(ref target, updated, initial) != initial);
            return initial - updated; // actual reserved
        }
    }
}
