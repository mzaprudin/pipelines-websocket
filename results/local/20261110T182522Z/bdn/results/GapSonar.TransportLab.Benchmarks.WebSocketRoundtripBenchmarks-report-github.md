```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X 0.55GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-KOVDBU : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=15  IterationTime=500ms  LaunchCount=1  
WarmupCount=5  

```
| Method        | PayloadBytes | StandardClient | Mean     | Error    | StdDev   | Allocated |
|-------------- |------------- |--------------- |---------:|---------:|---------:|----------:|
| **EchoRoundtrip** | **128**          | **False**          | **31.61 μs** | **0.857 μs** | **0.759 μs** |     **575 B** |
| **EchoRoundtrip** | **128**          | **True**           | **32.61 μs** | **0.950 μs** | **0.889 μs** |     **579 B** |
| **EchoRoundtrip** | **4096**         | **False**          | **37.68 μs** | **0.687 μs** | **0.537 μs** |     **580 B** |
| **EchoRoundtrip** | **4096**         | **True**           | **38.80 μs** | **1.639 μs** | **1.369 μs** |     **581 B** |
