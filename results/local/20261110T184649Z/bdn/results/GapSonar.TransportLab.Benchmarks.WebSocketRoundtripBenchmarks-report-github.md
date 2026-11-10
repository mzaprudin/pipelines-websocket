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
| **EchoRoundtrip** | **128**          | **False**          | **33.88 μs** | **1.895 μs** | **1.773 μs** |     **582 B** |
| **EchoRoundtrip** | **128**          | **True**           | **32.65 μs** | **0.857 μs** | **0.760 μs** |     **580 B** |
| **EchoRoundtrip** | **4096**         | **False**          | **38.71 μs** | **1.007 μs** | **0.893 μs** |     **581 B** |
| **EchoRoundtrip** | **4096**         | **True**           | **38.48 μs** | **1.341 μs** | **1.255 μs** |     **580 B** |
