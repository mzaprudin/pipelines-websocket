```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.3 LTS (Noble Numbat)
AMD Ryzen 9 5950X 0.55GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  Job-CKHFMU : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

IterationCount=15  IterationTime=500ms  LaunchCount=2  
WarmupCount=5  

```
| Method        | PayloadBytes | StandardClient | Mean     | Error    | StdDev   | Allocated |
|-------------- |------------- |--------------- |---------:|---------:|---------:|----------:|
| **EchoRoundtrip** | **128**          | **False**          | **31.99 μs** | **0.413 μs** | **0.605 μs** |     **582 B** |
| **EchoRoundtrip** | **128**          | **True**           | **32.53 μs** | **0.817 μs** | **1.172 μs** |     **581 B** |
| **EchoRoundtrip** | **4096**         | **False**          | **38.58 μs** | **0.600 μs** | **0.898 μs** |     **581 B** |
| **EchoRoundtrip** | **4096**         | **True**           | **37.59 μs** | **0.807 μs** | **1.183 μs** |     **582 B** |
