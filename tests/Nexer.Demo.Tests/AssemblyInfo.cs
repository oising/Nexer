// Application.Current and the demo's static state are process-wide; run the end-to-end tests serially.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
