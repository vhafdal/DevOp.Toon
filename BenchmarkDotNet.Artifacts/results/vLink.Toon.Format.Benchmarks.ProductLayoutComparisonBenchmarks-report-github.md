```

BenchmarkDotNet v0.15.8, Linux Zorin OS 18.1
11th Gen Intel Core i7-11850H 2.50GHz (Max: 0.80GHz), 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.105
  [Host] : .NET 10.0.5 (10.0.5, 10.0.526.15411), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  Categories=Decode  

```
| Method           | ProductCount | Mean | Error | Ratio | RatioSD | Alloc Ratio |
|----------------- |------------- |-----:|------:|------:|--------:|------------:|
| DecodeToonNative | 100          |   NA |    NA |     ? |       ? |           ? |

Benchmarks with issues:
  ProductLayoutComparisonBenchmarks.DecodeToonNative: ShortRun(IterationCount=3, LaunchCount=1, WarmupCount=3) [ProductCount=100]
