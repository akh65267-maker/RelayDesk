using RelayDesk.IntegrationTests.Infrastructure;

namespace RelayDesk.Evals;

/// <summary>xUnit only honours collection fixtures defined in the test assembly itself.</summary>
[CollectionDefinition(ApiCollection.Name)]
public sealed class EvalCollection : ICollectionFixture<RelayApiFactory>;
