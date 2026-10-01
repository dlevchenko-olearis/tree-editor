using System.Net;
using System.Text.Json.Nodes;
using Npgsql;
using TreeEditor.Api.IntegrationTests.Support;

namespace TreeEditor.Api.IntegrationTests.Api;

public sealed class ChangesEndpointTests(PostgresContainer postgres) : IAsyncLifetime
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
    public async Task Applies_creations_edits_and_deletions_together()
    {
        var mobile = await VersionOf("Mobile app");
        var created = Guid.CreateVersion7();

        var result = await ApplyOk(new
        {
            generation = 1,
            created = new[] { new { id = created, parentId = Sample.Id("Web app"), value = "Account page" } },
            updated = new[] { new { id = Sample.Id("Mobile app"), value = "Mobile apps", expectedVersion = mobile } },
            deleted = new[] { Sample.Id("Facilities") },
        });

        Assert.Equal(created, IdAt(result["created"]!, 0));
        Assert.True(result["updated"]![0]!["version"]!.GetValue<long>() > mobile);
        Assert.Equal(Sample.Id("Facilities"), Guid.Parse(result["deleted"]![0]!.GetValue<string>()));
        Assert.Equal("Account page", (await _api.NodeAsync(created))["value"]!.GetValue<string>());
        Assert.Equal("Mobile apps", (await Node("Mobile app"))["value"]!.GetValue<string>());
        Assert.True((await Node("Facilities"))["isDeleted"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Persists_nothing_when_any_change_conflicts()
    {
        var before = await Snapshot();

        var conflicts = await ApplyConflict(new
        {
            generation = 1,
            created = new[] { new { id = Guid.CreateVersion7(), parentId = Sample.Id("Web app"), value = "x" } },
            updated = new[]
            {
                new { id = Sample.Id("Mobile app"), value = "fine", expectedVersion = await VersionOf("Mobile app") },
                new { id = Sample.Id("Checkout"), value = "stale", expectedVersion = 1L },
            },
            deleted = new[] { Sample.Id("Facilities") },
        });

        Assert.Equal([("VersionMismatch", Sample.Id("Checkout"))], Kinds(conflicts));
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Creates_a_chain_of_new_nodes()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        await ApplyOk(new
        {
            generation = 1,
            created = new[]
            {
                new { id = first, parentId = Sample.Id("Archive"), value = "N1" },
                new { id = second, parentId = first, value = "N2" },
            },
        });

        var node = await _api.NodeAsync(second);
        Assert.Equal(
            [first, Sample.Id("Archive")],
            node["ancestorIds"]!.AsArray().Select(id => Guid.Parse(id!.GetValue<string>())));
    }

    [Fact]
    public async Task Rejects_a_forward_reference_and_persists_nothing()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var before = await Snapshot();

        var problem = await ApplyStatus(
            new
            {
                generation = 1,
                created = new[]
                {
                    new { id = second, parentId = first, value = "N2" },
                    new { id = first, parentId = Sample.Id("Archive"), value = "N1" },
                },
            },
            400);

        Assert.NotNull(problem["errors"]!["created[0].parentId"]);
        Assert.Equal(1, problem["generation"]!.GetValue<long>());
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Rejects_an_attempt_to_create_a_root()
    {
        var before = await Snapshot();

        var problem = await ApplyStatus(
            new { generation = 1, created = new[] { new { id = Guid.CreateVersion7(), parentId = (Guid?)null, value = "root" } } },
            400);

        Assert.NotNull(problem["errors"]!["created[0].parentId"]);
        Assert.Equal(before, await Snapshot());
    }

    [Theory]
    [InlineData(201, "abc")]
    [InlineData(0, " abc")]
    [InlineData(0, "abc ")]
    [InlineData(0, "")]
    public async Task Rejects_invalid_values_and_persists_nothing(int length, string text)
    {
        var value = length > 0 ? new string('a', length) : text;
        var before = await Snapshot();

        await ApplyStatus(
            new { generation = 1, created = new[] { new { id = Guid.CreateVersion7(), parentId = Sample.Id("Archive"), value } } },
            400);

        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Rejects_a_value_with_a_nul_character_and_persists_nothing()
    {
        var before = await Snapshot();
        var value = "a" + (char)0 + "b";

        var problem = await ApplyStatus(
            new { generation = 1, created = new[] { new { id = Guid.CreateVersion7(), parentId = Sample.Id("Archive"), value } } },
            400);

        Assert.NotNull(problem["errors"]!["created[0].value"]);
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Rejects_a_value_with_an_unpaired_surrogate_as_a_bad_request()
    {
        var before = await Snapshot();
        var unpaired = @"\" + "ud800";
        var json = $$"""{"generation":1,"created":[{"id":"{{Guid.CreateVersion7()}}","parentId":"{{Sample.Id("Archive")}}","value":"a{{unpaired}}b"}]}""";

        using var response = await ApplyJson(json);

        await ApiClient.ReadAsync(response, 400);
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Rejects_null_items_as_a_validation_error()
    {
        using var response = await ApplyJson("""{"generation":1,"created":[null],"updated":[null]}""");

        var problem = await ApiClient.ReadAsync(response, 400);
        Assert.NotNull(problem["errors"]!["created[0]"]);
        Assert.NotNull(problem["errors"]!["updated[0]"]);
    }

    [Fact]
    public async Task Stores_a_valid_value_exactly_as_received()
    {
        var id = Guid.CreateVersion7();
        var value = "a  b" + (char)0x0085 + "c";

        await ApplyOk(new { generation = 1, created = new[] { new { id, parentId = Sample.Id("Archive"), value } } });

        Assert.Equal(value, (await _api.NodeAsync(id))["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Rejects_ids_that_are_not_version_7()
    {
        var problem = await ApplyStatus(new { generation = 1, deleted = new[] { Guid.NewGuid() } }, 400);

        Assert.NotNull(problem["errors"]!["deleted[0]"]);
    }

    [Fact]
    public async Task Lists_every_version_conflict()
    {
        var conflicts = await ApplyConflict(new
        {
            generation = 1,
            updated = new[]
            {
                new { id = Sample.Id("Checkout"), value = "a", expectedVersion = 1L },
                new { id = Sample.Id("Search page"), value = "b", expectedVersion = 1L },
            },
        });

        Assert.Equal(
            [("VersionMismatch", Sample.Id("Checkout")), ("VersionMismatch", Sample.Id("Search page"))],
            Kinds(conflicts));
        Assert.Equal(await VersionOf("Checkout"), conflicts[0]!["currentVersion"]!.GetValue<long>());
    }

    [Fact]
    public async Task Reports_a_missing_parent()
    {
        var child = Guid.CreateVersion7();
        var parent = Guid.CreateVersion7();

        var conflicts = await ApplyConflict(new { generation = 1, created = new[] { new { id = child, parentId = parent, value = "x" } } });

        Assert.Equal([("ParentNotFound", child)], Kinds(conflicts));
        Assert.Equal(parent, Guid.Parse(conflicts[0]!["parentId"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Deletes_a_subtree_including_descendants_that_were_never_loaded()
    {
        await ApplyOk(new { generation = 1, deleted = new[] { Sample.Id("Platform") } });

        var subtree = new[] { "Platform", "Storage", "Replication", "Raft log compaction", "Snapshot transfer", "Backups", "Networking", "Service mesh" };
        foreach (var value in subtree)
        {
            Assert.True((await Node(value))["isDeleted"]!.GetValue<bool>(), value);
        }

        var inspected = await _database.QueryAsync(
            "SELECT value, is_deleted_effective FROM node_state WHERE value = ANY($1)",
            reader => (reader.GetString(0), reader.GetBoolean(1)),
            (object)subtree);
        Assert.Equal(subtree.Length, inspected.Count);
        Assert.All(inspected, row => Assert.True(row.Item2, row.Item1));
        Assert.Equal((long)Sample.Count, await _database.ScalarAsync<long>("SELECT count(*) FROM nodes"));
        Assert.Equal(1L, await _database.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE is_deleted"));
        Assert.False((await Node("Product"))["isDeleted"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Own_edits_and_additions_inside_a_branch_deleted_by_the_same_apply_do_not_conflict()
    {
        var added = Guid.CreateVersion7();

        await ApplyOk(new
        {
            generation = 1,
            created = new[] { new { id = added, parentId = Sample.Id("Backups"), value = "Weekly" } },
            updated = new[] { new { id = Sample.Id("Nightly full backup"), value = "Nightly", expectedVersion = await VersionOf("Nightly full backup") } },
            deleted = new[] { Sample.Id("Storage") },
        });

        Assert.True((await Node("Nightly full backup"))["isDeleted"]!.GetValue<bool>());
        Assert.True((await _api.NodeAsync(added))["isDeleted"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_second_of_two_sessions_editing_the_same_node_gets_a_version_conflict()
    {
        var version = await VersionOf("Checkout");
        var first = new { generation = 1, updated = new[] { new { id = Sample.Id("Checkout"), value = "Session 1", expectedVersion = version } } };
        var second = new
        {
            generation = 1,
            created = new[] { new { id = Guid.CreateVersion7(), parentId = Sample.Id("Archive"), value = "Session 2 node" } },
            updated = new[] { new { id = Sample.Id("Checkout"), value = "Session 2", expectedVersion = version } },
        };

        await ApplyOk(first);
        var conflicts = await ApplyConflict(second);

        Assert.Equal([("VersionMismatch", Sample.Id("Checkout"))], Kinds(conflicts));
        Assert.Equal("Session 1", (await Node("Checkout"))["value"]!.GetValue<string>());
        Assert.Equal(0L, await _database.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE value = 'Session 2 node'"));
    }

    [Fact]
    public async Task Concurrent_edits_in_opposite_order_end_in_success_or_conflict_but_never_in_a_server_error()
    {
        for (var round = 0; round < 15; round++)
        {
            var x = await VersionOf("Tier 1");
            var y = await VersionOf("Tier 2");
            var forward = new
            {
                generation = 1,
                updated = new[]
                {
                    new { id = Sample.Id("Tier 1"), value = $"A{round}", expectedVersion = x },
                    new { id = Sample.Id("Tier 2"), value = $"A{round}", expectedVersion = y },
                },
            };
            var backward = new
            {
                generation = 1,
                updated = new[]
                {
                    new { id = Sample.Id("Tier 2"), value = $"B{round}", expectedVersion = y },
                    new { id = Sample.Id("Tier 1"), value = $"B{round}", expectedVersion = x },
                },
            };

            var responses = await Task.WhenAll(_api.ApplyAsync(forward), _api.ApplyAsync(backward));

            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Conflict],
                responses.Select(response => response.StatusCode).Order());
            var tier1 = (await Node("Tier 1"))["value"]!.GetValue<string>();
            Assert.Equal(tier1, (await Node("Tier 2"))["value"]!.GetValue<string>());
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Deleting_a_node_that_another_session_edited_succeeds()
    {
        await ApplyOk(new { generation = 1, updated = new[] { new { id = Sample.Id("France"), value = "France (FR)", expectedVersion = await VersionOf("France") } } });

        await ApplyOk(new { generation = 1, deleted = new[] { Sample.Id("France") } });

        Assert.True((await Node("France"))["isDeleted"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Editing_under_a_node_another_session_deleted_conflicts_as_deleted()
    {
        var version = await VersionOf("Berlin office");
        await ApplyOk(new { generation = 1, deleted = new[] { Sample.Id("Europe") } });

        var conflicts = await ApplyConflict(new
        {
            generation = 1,
            created = new[] { new { id = Guid.CreateVersion7(), parentId = Sample.Id("Germany"), value = "Munich office" } },
            updated = new[] { new { id = Sample.Id("Berlin office"), value = "Berlin", expectedVersion = version } },
        });

        Assert.Equal(
            [("ParentDeleted", conflicts[0]!["id"]!.GetValue<Guid>()), ("Deleted", Sample.Id("Berlin office"))],
            Kinds(conflicts));
    }

    [Fact]
    public async Task Deleting_inside_an_already_deleted_branch_succeeds_without_effect()
    {
        await ApplyOk(new { generation = 1, deleted = new[] { Sample.Id("Sales") } });
        var before = await Snapshot();

        var result = await ApplyOk(new { generation = 1, deleted = new[] { Sample.Id("Germany") } });

        Assert.Equal(Sample.Id("Germany"), Guid.Parse(result["deleted"]![0]!.GetValue<string>()));
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task A_repeated_request_with_the_same_key_is_applied_once_and_returns_the_same_result()
    {
        var key = Guid.NewGuid();
        var body = new { generation = 1, created = new[] { new { id = Guid.CreateVersion7(), parentId = Sample.Id("Archive"), value = "Once" } } };

        var first = await ApplyOk(body, key);
        var second = await ApplyOk(body, key);

        Assert.Equal(first.ToJsonString(), second.ToJsonString());
        Assert.Equal(1L, await _database.ScalarAsync<long>("SELECT count(*) FROM nodes WHERE value = 'Once'"));
    }

    [Fact]
    public async Task A_key_reused_with_a_different_change_set_is_rejected()
    {
        var key = Guid.NewGuid();
        await ApplyOk(new { generation = 1, deleted = new[] { Sample.Id("Asia") } }, key);
        var before = await Snapshot();

        var problem = await ApplyStatus(new { generation = 1, deleted = new[] { Sample.Id("France") } }, 422, key);

        Assert.Equal(1, problem["generation"]!.GetValue<long>());
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task A_request_that_conflicted_is_evaluated_again_when_retried_with_the_same_key()
    {
        var key = Guid.NewGuid();
        var original = await VersionOf("Tier 1");
        await ApplyOk(new { generation = 1, updated = new[] { new { id = Sample.Id("Tier 1"), value = "Other", expectedVersion = original } } });
        var body = new { generation = 1, updated = new[] { new { id = Sample.Id("Tier 1"), value = "Mine", expectedVersion = original } } };
        await ApplyConflict(body, key);

        await _database.ExecuteAsync("UPDATE nodes SET version = $1 WHERE id = $2", original, Sample.Id("Tier 1"));
        await ApplyOk(body, key);

        Assert.Equal("Mine", (await Node("Tier 1"))["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task Creating_an_id_that_already_exists_reports_its_parent_and_version()
    {
        var id = Guid.CreateVersion7();
        var body = new { generation = 1, created = new[] { new { id, parentId = Sample.Id("Archive"), value = "Twice" } } };
        var created = await ApplyOk(body);

        var conflicts = await ApplyConflict(body);

        Assert.Equal([("AlreadyExists", id)], Kinds(conflicts));
        Assert.Equal(Sample.Id("Archive"), Guid.Parse(conflicts[0]!["parentId"]!.GetValue<string>()));
        Assert.Equal(created["created"]![0]!["version"]!.GetValue<long>(), conflicts[0]!["currentVersion"]!.GetValue<long>());
    }

    [Fact]
    public async Task Accepts_ten_thousand_operations_and_rejects_ten_thousand_and_one()
    {
        var parent = Sample.Id("Archive");
        var largest = Enumerable.Range(0, 10_000)
            .Select(_ => new { id = Guid.CreateVersion7(), parentId = parent, value = new string('v', 200) })
            .ToArray();
        var tooLarge = largest.Append(new { id = Guid.CreateVersion7(), parentId = parent, value = "one more" }).ToArray();

        var rejected = await ApplyStatus(new { generation = 1, created = tooLarge }, 413);
        Assert.Equal((long)Sample.Count, await _database.ScalarAsync<long>("SELECT count(*) FROM nodes"));
        Assert.Equal(1, rejected["generation"]!.GetValue<long>());

        var accepted = await ApplyOk(new { generation = 1, created = largest });
        Assert.Equal(10_000, accepted["created"]!.AsArray().Count);
    }

    [Fact]
    public async Task Requires_a_uuid_idempotency_key()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/changes")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { generation = 1, deleted = new[] { Sample.Id("Asia") } }),
        };
        request.Headers.Add("X-Requested-With", "tree-editor");

        using var response = await _api.Http.SendAsync(request);

        var problem = await ApiClient.ReadAsync(response, 400);
        Assert.NotNull(problem["errors"]!["Idempotency-Key"]);
        Assert.False((await Node("Asia"))["isDeleted"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_lock_timeout_is_reported_as_retryable_and_persists_nothing()
    {
        var version = await VersionOf("Checkout");
        await using var connection = await _database.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("SELECT 1 FROM nodes WHERE id = $1 FOR UPDATE", connection, transaction))
        {
            hold.Parameters.Add(new NpgsqlParameter { Value = Sample.Id("Checkout") });
            await hold.ExecuteNonQueryAsync();
        }

        using var response = await _api.ApplyAsync(new
        {
            generation = 1,
            updated = new[] { new { id = Sample.Id("Checkout"), value = "Blocked", expectedVersion = version } },
        });

        await ApiClient.ReadAsync(response, 503);
        Assert.NotNull(response.Headers.RetryAfter);
        await transaction.RollbackAsync();
        Assert.Equal("Checkout", (await Node("Checkout"))["value"]!.GetValue<string>());
    }

    private async Task<JsonNode> ApplyOk(object body, Guid? key = null) => await ApplyStatus(body, 200, key);

    private async Task<JsonArray> ApplyConflict(object body, Guid? key = null) =>
        (await ApplyStatus(body, 409, key))["conflicts"]!.AsArray();

    private async Task<JsonNode> ApplyStatus(object body, int status, Guid? key = null)
    {
        using var response = await _api.ApplyAsync(body, key);
        return await ApiClient.ReadAsync(response, status);
    }

    private Task<HttpResponseMessage> ApplyJson(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/changes")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Requested-With", "tree-editor");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return _api.Http.SendAsync(request);
    }

    private Task<JsonNode> Node(string value) => _api.NodeAsync(Sample.Id(value));

    private async Task<long> VersionOf(string value) => (await Node(value))["version"]!.GetValue<long>();

    private Task<List<string>> Snapshot() =>
        _database.QueryAsync(
            "SELECT id::text || '|' || value || '|' || is_deleted::text || '|' || version::text FROM nodes ORDER BY id",
            reader => reader.GetString(0));

    private static List<(string Kind, Guid Id)> Kinds(JsonArray conflicts) =>
        conflicts
            .Select(conflict => (conflict!["kind"]!.GetValue<string>(), Guid.Parse(conflict["id"]!.GetValue<string>())))
            .ToList();

    private static Guid IdAt(JsonNode array, int index) => Guid.Parse(array[index]!["id"]!.GetValue<string>());
}
