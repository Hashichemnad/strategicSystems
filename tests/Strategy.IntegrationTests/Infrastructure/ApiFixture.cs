namespace Strategy.IntegrationTests.Infrastructure;

/// <summary>One host + fresh database shared by the tests of a class (tests use unique data, never global counts).</summary>
public sealed class ApiFixture(PostgresFixture postgres) : IAsyncLifetime
{
    public ApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Factory = await ApiFactory.CreateAsync(postgres);

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}
