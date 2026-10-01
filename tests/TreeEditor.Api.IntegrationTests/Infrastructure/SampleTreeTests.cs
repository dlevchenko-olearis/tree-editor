using TreeEditor.Api.IntegrationTests.Support;

namespace TreeEditor.Api.IntegrationTests.Infrastructure;

public sealed class SampleTreeTests(PostgresContainer postgres)
{
    [Fact]
    public async Task A_fresh_database_is_seeded_with_a_deep_and_wide_sample_tree()
    {
        await using var harness = await InfrastructureHarness.CreateAsync(postgres);

        var depth = await harness.Database.ScalarAsync<int>(
            """
            WITH RECURSIVE walk (id, depth) AS (
                SELECT id, 1 FROM nodes WHERE parent_id IS NULL
                UNION ALL
                SELECT n.id, w.depth + 1 FROM nodes AS n JOIN walk AS w ON n.parent_id = w.id
            )
            SELECT max(depth) FROM walk
            """);
        var childCounts = await harness.Database.QueryAsync(
            "SELECT count(*) FROM nodes WHERE parent_id IS NOT NULL GROUP BY parent_id",
            reader => reader.GetInt64(0));
        var roots = await harness.Database.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE parent_id IS NULL");

        Assert.True(roots >= 1);
        Assert.True(depth >= 5, $"The sample tree is only {depth} levels deep.");
        Assert.True(childCounts.Count(count => count > 1) >= 3);
        Assert.Contains(childCounts, count => count > 20);
        Assert.Equal(0L, await harness.Database.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE is_deleted"));
    }

    [Fact]
    public async Task Ids_and_values_are_identical_across_seedings()
    {
        await using var first = await InfrastructureHarness.CreateAsync(postgres);
        await using var second = await InfrastructureHarness.CreateAsync(postgres);

        Assert.Equal(await ReadTree(first), await ReadTree(second));
    }

    [Fact]
    public async Task Sample_ids_are_version_7_uuids_in_declaration_order()
    {
        await using var harness = await InfrastructureHarness.CreateAsync(postgres);

        var versions = await harness.Database.QueryAsync(
            "SELECT id FROM nodes ORDER BY version",
            reader => reader.GetGuid(0));
        var byId = await harness.Database.QueryAsync(
            "SELECT id FROM nodes ORDER BY id",
            reader => reader.GetGuid(0));

        Assert.All(versions, id => Assert.Equal(7, id.Version));
        Assert.Equal(versions, byId);
    }

    private static Task<List<string>> ReadTree(InfrastructureHarness harness) =>
        harness.Database.QueryAsync(
            "SELECT id::text || '|' || coalesce(parent_id::text, '') || '|' || value FROM nodes ORDER BY id",
            reader => reader.GetString(0));
}
