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
| **EchoRoundtrip** | **128**          | **False**          | **32.49 μs** | **0.908 μs** | **0.805 μs** |     **581 B** |
| **EchoRoundtrip** | **128**          | **True**           | **33.29 μs** | **1.224 μs** | **1.145 μs** |     **581 B** |
| **EchoRoundtrip** | **4096**         | **False**          | **39.23 μs** | **1.043 μs** | **0.975 μs** |     **580 B** |
| **EchoRoundtrip** | **4096**         | **True**           | **37.76 μs** | **0.910 μs** | **0.807 μs** |     **581 B** |
