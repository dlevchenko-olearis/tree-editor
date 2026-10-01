using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TreeEditor.Api.IntegrationTests.Support;

namespace TreeEditor.Api.IntegrationTests.Api;

public sealed class ResetEndpointTests(PostgresContainer postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private Database _database = null!;
    private ApiClient _api = null!;

    public async ValueTask InitializeAsync()
    {
        (_factory, _database) = await ApiFactory.CreateAsync(postgres);
        _api = new ApiClient(_factory.CreateClient());
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Reset_restores_the_sample_exactly_and_increments_the_generation()
    {
        var sample = await SampleContent();
        using (var applied = await _api.ApplyAsync(new
        {
            generation = 1,
            created = new[] { new { id = Guid.CreateVersion7(), parentId = Sample.Id("Archive"), value = "Added" } },
            updated = new[] { new { id = Sample.Id("Checkout"), value = "Edited", expectedVersion = await VersionOf("Checkout") } },
            deleted = new[] { Sample.Id("Engineering") },
        }))
        {
            await ApiClient.ReadAsync(applied, 200);
        }

        var reset = await Reset();

        Assert.Equal(2, reset["generation"]!.GetValue<long>());
        Assert.Equal(sample, await SampleContent());
        Assert.Equal(2, await _api.GenerationAsync());
        Assert.Equal(0L, await _database.ScalarAsync<long>("SELECT count(*) FROM apply_requests"));
    }

    [Fact]
    public async Task A_stale_edit_after_reset_is_rejected_with_stale_generation()
    {
        var version = await VersionOf("Checkout");
        await Reset();

        using var response = await _api.ApplyAsync(new
        {
            generation = 1,
            updated = new[] { new { id = Sample.Id("Checkout"), value = "Stale", expectedVersion = version } },
        });

        var conflict = Assert.Single((await ApiClient.ReadAsync(response, 409))["conflicts"]!.AsArray());
        Assert.Equal("StaleGeneration", conflict!["kind"]!.GetValue<string>());
        Assert.Null(conflict["id"]);
        Assert.Equal("Checkout", (await _api.NodeAsync(Sample.Id("Checkout")))["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_stale_deletion_after_reset_is_rejected_and_the_node_stays_live()
    {
        await Reset();

        using var response = await _api.ApplyAsync(new { generation = 1, deleted = new[] { Sample.Id("Company") } });

        var body = await ApiClient.ReadAsync(response, 409);
        Assert.Equal(2, body["generation"]!.GetValue<long>());
        Assert.False((await _api.NodeAsync(Sample.Id("Company")))["isDeleted"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Reset_concurrent_with_apply_never_leaves_a_mixed_state()
    {
        var sample = await SampleContent();
        for (var round = 0; round < 5; round++)
        {
            var generation = await _api.GenerationAsync();
            var created = Enumerable.Range(0, 2_000)
                .Select(index => new { id = Guid.CreateVersion7(), parentId = Sample.Id("Archive"), value = $"Concurrent {index}" })
                .ToArray();

            var apply = _api.ApplyAsync(new { generation, created, deleted = new[] { Sample.Id("Company") } });
            var reset = _api.ResetAsync();
            await Task.WhenAll(apply, reset);

            using var applyResponse = apply.Result;
            using var resetResponse = reset.Result;
            Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
            Assert.Contains(applyResponse.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            Assert.Equal(sample, await SampleContent());
        }
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=x")]
    public async Task A_cross_site_form_post_is_rejected_without_effect(string contentType)
    {
        await DeleteCompany();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/reset")
        {
            Content = new StringContent("a=b", Encoding.UTF8),
        };
        request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);

        using var response = await _api.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(1, await _api.GenerationAsync());
        Assert.True((await _api.NodeAsync(Sample.Id("Company")))["isDeleted"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_json_post_without_the_custom_header_is_rejected_without_effect()
    {
        await DeleteCompany();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/reset")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        using var response = await _api.Http.SendAsync(request);

        var problem = await ApiClient.ReadAsync(response, 400);
        Assert.Equal(1, problem["generation"]!.GetValue<long>());
        Assert.Equal(1, await _api.GenerationAsync());
    }

    [Fact]
    public async Task Cross_origin_requests_get_no_cors_permission()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/reset");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "x-requested-with,content-type");

        using var response = await _api.Http.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private async Task DeleteCompany()
    {
        using var response = await _api.ApplyAsync(new { generation = 1, deleted = new[] { Sample.Id("Company") } });
        await ApiClient.ReadAsync(response, 200);
    }

    private async Task<JsonNode> Reset()
    {
        using var response = await _api.ResetAsync();
        return await ApiClient.ReadAsync(response, 200);
    }

    private async Task<long> VersionOf(string value) =>
        (await _api.NodeAsync(Sample.Id(value)))["version"]!.GetValue<long>();

    private Task<List<string>> SampleContent() =>
        _database.QueryAsync(
            "SELECT id::text || '|' || coalesce(parent_id::text, '') || '|' || value || '|' || is_deleted::text FROM nodes ORDER BY id",
            reader => reader.GetString(0));
}
