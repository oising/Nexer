// Application.Current is process-wide; UI tests run sequentially.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
