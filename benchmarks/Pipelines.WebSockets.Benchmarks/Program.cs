using BenchmarkDotNet.Running;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args).ToArray();
return summaries.Length == 0 || summaries.Any(static summary =>
    summary.HasCriticalValidationErrors || summary.Reports.Any(static report => !report.Success)) ? 1 : 0;
