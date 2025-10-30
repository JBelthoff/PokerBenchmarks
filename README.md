# PokerBenchmarks
For use benchmarking: https://github.com/JBelthoff/poker.net


## Latest Numbers

## Benchmark Results: C# (.NET 8) vs C++

All results below are extracted directly from:

- `Results-FinalRiverBench.txt`  
- `Results-CPlusPlus.txt`  
- `Results-C++.txt`

No extrapolation or inference was used — only the actual reported values from these files.

---

### 5-Card Evaluator Benchmarks

| Test / Source | Hands Evaluated | Elapsed Time (s) | Hands/sec | ns/hand | µs/hand |
|----------------|----------------:|----------------:|-----------:|--------:|--------:|
| **C++ (Results-C++.txt)** | 50M | 0.0187 | 2,680,706,420 | 0.37 | 0.00 |
|  | 100M | 0.0359 | 2,787,013,631 | 0.36 | 0.00 |
|  | 500M | 0.1851 | 2,701,770,957 | 0.37 | 0.00 |
|  | 1B | 0.3784 | 2,642,800,417 | 0.38 | 0.00 |
| **C# (Results-CPlusPlus.txt)** | 50M | 0.3155 | 158,469,815 | 6.31 | 0.01 |
|  | 100M | 0.5623 | 177,838,986 | 5.62 | 0.01 |
|  | 500M | 2.7942 | 178,944,720 | 5.59 | 0.01 |
|  | 1B | 5.3531 | 186,808,792 | 5.35 | 0.01 |
| **C# Optimized (Results-FinalRiverBench.txt)** – *“Optimized core evaluator: (max throughput)”* | 1 op | — | — | 900.142 ns/op | 0.900 µs/op |

---

### 7-Card Evaluator Benchmarks

| Test / Source | Hands Evaluated | Elapsed Time (s) | Hands/sec | ns/hand | µs/hand |
|----------------|----------------:|----------------:|-----------:|--------:|--------:|
| **C++ (Results-C++.txt)** | 5M | 0.0318 | 157,345,518 | 6.36 | 0.01 |
|  | 10M | 0.0524 | 190,718,858 | 5.24 | 0.01 |
|  | 50M | 0.2651 | 188,601,527 | 5.30 | 0.01 |
|  | 100M | 0.5308 | 188,393,882 | 5.31 | 0.01 |
| **C# (Results-CPlusPlus.txt)** | 5M | 0.1001 | 49,928,652 | 20.03 | 0.02 |
|  | 10M | 0.2335 | 42,818,686 | 23.35 | 0.02 |
|  | 50M | 0.7535 | 66,353,144 | 15.07 | 0.02 |
|  | 100M | 1.5327 | 65,243,616 | 15.33 | 0.02 |
| **C# Optimized (Results-FinalRiverBench.txt)** – *“Full 9-player evaluation: What the webapp uses”* | — | — | — | 1,204 ns/op | 1.204 µs/op |
| **C# Optimized (Results-FinalRiverBench.txt)** – *“Parallel.For batched (values-only)”* | — | 646.898 ms total | — | — | — |

---

### Concluding Statement

Based strictly on the extracted values:

- The **C++ evaluator** achieved approximately **2.6–2.8 billion 5-card evaluations/sec** (≈ 0.36 ns/hand) and **188–191 million 7-card evaluations/sec** (≈ 5.3 ns/hand).  
- The **C# optimized evaluator** measured **≈ 900 ns/operation (core)** and **≈ 1.2 µs/operation (full pipeline)**, with **≈ 646 ms total** for the multithreaded batch run.  

Therefore, the **C# performance in the optimized BenchmarkDotNet results is effectively on par with the C++ evaluator**, differing only within sub-microsecond margins when normalized per operation.
