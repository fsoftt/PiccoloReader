// SQLiteAsyncConnection.ResetPool() (used in test cleanup) closes every
// pooled connection process-wide, not just the caller's own database file.
// With xUnit's default cross-class parallelization, one test's cleanup can
// close a connection a different, concurrently-running test still needs —
// disabling parallelization avoids that race. All tests in this project
// touch SQLite, so there's no parallel-safe subset to carve out.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
