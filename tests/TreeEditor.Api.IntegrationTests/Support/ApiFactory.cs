using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TreeEditor.Api.IntegrationTests.Support;

/// <summary>The real API host wired to a dedicated test database.</summary>
public sealed class ApiFactory(string connectionString, int mutationPermitLimit) : WebApplicationFactory<Program>
{
    /// <summary>Starts the API against a fresh database; the host migrates and seeds it on start.</summary>
    public static async Task<(ApiFactory Factory, Database Database)> CreateAsync(
        PostgresContainer postgres,
        int mutationPermitLimit = 100_000)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var factory = new ApiFactory(connectionString, mutationPermitLimit);
        factory.StartServer();
        return (factory, new Database(connectionString));
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:TreeEditor", connectionString);
        builder.UseSetting("Api:MutationPermitLimit", mutationPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
