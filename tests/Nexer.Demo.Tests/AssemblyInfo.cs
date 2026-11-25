// Application.Current and the demo's static state are process-wide; run the end-to-end tests serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
