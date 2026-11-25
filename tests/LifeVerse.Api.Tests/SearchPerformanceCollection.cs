using Xunit;

namespace LifeVerse.Api.Tests;

// Wall-clock assertions (search performance and Lua time budgets) must not compete
// with unrelated load tests. Production limits remain unchanged.
[CollectionDefinition("Search performance", DisableParallelization = true)]
public sealed class SearchPerformanceCollection { }
