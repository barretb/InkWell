using Xunit;

// Story tests in this assembly each stand up a real SQLCipher database, and opening one costs a
// deliberate PBKDF2 key derivation. Run in parallel, twenty of those compete for the same cores and
// distort wall-clock measurements — which is fatal for the SC-006 assertions in
// Performance/DistractionFreePerformanceTests, where a toggle that takes 40 ms in isolation was
// observed taking 1.2 s under load and failing a one-second budget.
//
// A performance test that fails at random is worse than none: people learn to re-run it. Trading
// roughly twenty seconds of suite time for deterministic timings is the right way round.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
