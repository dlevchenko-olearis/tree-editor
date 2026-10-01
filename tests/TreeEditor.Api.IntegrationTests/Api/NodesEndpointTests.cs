using System.Net;
using System.Text.Json.Nodes;
using TreeEditor.Api.IntegrationTests.Support;

namespace TreeEditor.Api.IntegrationTests.Api;

public sealed class NodesEndpointTests(PostgresContainer postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private ApiClient _api = null!;

    public async ValueTask InitializeAsync()
    {
        (_factory, _) = await ApiFactory.CreateAsync(postgres);
        _api = new ApiClient(_factory.CreateClient());
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Roots_are_the_sample_roots_with_the_generation()
    {
        var page = await _api.GetJsonAsync("/api/nodes/roots");

        Assert.Equal(1, page["generation"]!.GetValue<long>());
        Assert.Equal(["Company", "Library", "Archive"], Values(page));
        Assert.Null(page["nextAfter"]);
    }

    [Theory]
    [InlineData(10, new[] { 10, 10, 5 })]
    [InlineData(5, new[] { 5, 5, 5, 5, 5 })]
    [InlineData(25, new[] { 25 })]
    public async Task Children_are_paged_without_duplicates_or_an_empty_trailing_page(int limit, int[] pageSizes)
    {
        var catalog = Sample.Id("Catalog");
        var sizes = new List<int>();
        var values = new List<string>();
        string? after = null;
        do
        {
            var query = after is null ? $"?limit={limit}" : $"?limit={limit}&after={after}";
            var page = await _api.GetJsonAsync($"/api/nodes/{catalog}/children{query}");
            sizes.Add(page["items"]!.AsArray().Count);
            values.AddRange(Values(page));
            after = page["nextAfter"]?.GetValue<string>();
        }
        while (after is not null);

        Assert.Equal(pageSizes, sizes);
        Assert.Equal(Enumerable.Range(1, 25).Select(book => $"Book {book:00}"), values);
    }

    [Fact]
    public async Task Leaves_report_no_children()
    {
        var page = await _api.GetJsonAsync($"/api/nodes/{Sample.Id("Replication")}/children");

        Assert.All(page["items"]!.AsArray(), item => Assert.False(item!["hasChildren"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task Descendants_that_were_never_loaded_are_shown_as_deleted()
    {
        var generation = await _api.GenerationAsync();
        using var deleted = await _api.ApplyAsync(new { generation, deleted = new[] { Sample.Id("Engineering") } });
        await ApiClient.ReadAsync(deleted, 200);

        var platform = await _api.GetJsonAsync($"/api/nodes/{Sample.Id("Platform")}/children");
        var replication = await _api.GetJsonAsync($"/api/nodes/{Sample.Id("Replication")}/children");
        var leaf = await _api.NodeAsync(Sample.Id("Raft log compaction"));
        var operations = await _api.GetJsonAsync($"/api/nodes/{Sample.Id("Operations")}/children");

        Assert.All(platform["items"]!.AsArray(), item => Assert.True(item!["isDeleted"]!.GetValue<bool>()));
        Assert.All(replication["items"]!.AsArray(), item => Assert.True(item!["isDeleted"]!.GetValue<bool>()));
        Assert.True(leaf["isDeleted"]!.GetValue<bool>());
        Assert.All(operations["items"]!.AsArray(), item => Assert.False(item!["isDeleted"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task A_node_is_returned_with_its_ancestor_chain()
    {
        var node = await _api.NodeAsync(Sample.Id("Raft log compaction"));

        Assert.Equal(
            new[] { "Replication", "Storage", "Platform", "Engineering", "Company" }.Select(Sample.Id),
            node["ancestorIds"]!.AsArray().Select(id => Guid.Parse(id!.GetValue<string>())));
        Assert.Equal(Sample.Id("Replication"), Guid.Parse(node["parentId"]!.GetValue<string>()));
        Assert.Equal("Raft log compaction", node["value"]!.GetValue<string>());
        Assert.True(node["version"]!.GetValue<long>() > 0);
        Assert.False(node["isDeleted"]!.GetValue<bool>());
        Assert.Equal(1, node["generation"]!.GetValue<long>());
    }

    [Theory]
    [InlineData("/api/nodes/{0}")]
    [InlineData("/api/nodes/{0}/children")]
    public async Task Unknown_nodes_are_not_found_with_the_generation(string template)
    {
        using var response = await _api.GetAsync(string.Format(template, Guid.CreateVersion7()));

        var problem = await ApiClient.ReadAsync(response, 404);
        Assert.Equal(1, problem["generation"]!.GetValue<long>());
    }

    [Theory]
    [InlineData("/api/nodes/not-a-uuid")]
    [InlineData("/api/nodes/3f2504e0-4f89-41d3-9a0c-0305e82c3301")]
    [InlineData("/api/nodes/00000000-0000-0000-0000-000000000000/children")]
    [InlineData("/api/nodes/roots?limit=0")]
    [InlineData("/api/nodes/roots?limit=201")]
    [InlineData("/api/nodes/roots?after=3f2504e0-4f89-41d3-9a0c-0305e82c3301")]
    [InlineData("/api/nodes/roots?limit=abc")]
    public async Task Invalid_parameters_are_rejected_with_the_generation(string path)
    {
        using var response = await _api.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ApiClient.ReadAsync(response, 400);
        Assert.Equal(1, problem["generation"]!.GetValue<long>());
    }

    [Fact]
    public async Task The_largest_page_is_accepted()
    {
        var page = await _api.GetJsonAsync("/api/nodes/roots?limit=200");

        Assert.Equal(3, page["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task Health_and_openapi_are_served()
    {
        using var health = await _api.GetAsync("/health");
        using var openApi = await _api.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
    }

    private static List<string> Values(JsonNode page) =>
        page["items"]!.AsArray().Select(item => item!["value"]!.GetValue<string>()).ToList();
}
