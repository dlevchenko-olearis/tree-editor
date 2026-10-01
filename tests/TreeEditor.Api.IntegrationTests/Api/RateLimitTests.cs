using System.Net;
using System.Text;
using TreeEditor.Api.IntegrationTests.Support;

namespace TreeEditor.Api.IntegrationTests.Api;

public sealed class RateLimitTests(PostgresContainer postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private ApiClient _api = null!;

    public async ValueTask InitializeAsync()
    {
        (_factory, _) = await ApiFactory.CreateAsync(postgres, mutationPermitLimit: 3);
        _api = new ApiClient(_factory.CreateClient());
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Mutations_beyond_the_limit_are_rejected_with_retry_after_while_reads_continue()
    {
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var response = await _api.ApplyAsync(new { generation = 1 });
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.NotNull(response.Headers.RetryAfter);
            }
        }

        using var read = await _api.GetAsync("/api/nodes/roots");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task Requests_rejected_by_the_mutation_guard_do_not_use_up_the_limit()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var forged = await _api.Http.PostAsync("/api/changes", new StringContent("a=b", Encoding.UTF8, "text/plain"));
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, forged.StatusCode);
        }

        using var response = await _api.ApplyAsync(new { generation = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
