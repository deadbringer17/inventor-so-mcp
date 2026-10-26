// OpenCV thread configuration is process-global; production likewise runs one reconstruction worker at a time.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
