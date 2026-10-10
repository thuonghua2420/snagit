using Xunit;

namespace SnapGit.Tests;

/// <summary>Real encodes and timing guard-rails: one at a time, alone on the machine, or CPU contention makes frame-rate checks flaky.</summary>
[CollectionDefinition("Heavy", DisableParallelization = true)]
public class HeavyCollection;