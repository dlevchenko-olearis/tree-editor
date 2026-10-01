using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TreeEditor.Api.IntegrationTests.Support;

/// <summary>Thin client for the tree editor API, sending the headers the browser client sends.</summary>
public sealed class ApiClient(HttpClient http)
{
    /// <summary>The underlying client.</summary>
    public HttpClient Http { get; } = http;

    /// <summary>Sends a GET request.</summary>
    public Task<HttpResponseMessage> GetAsync(string path) => Http.GetAsync(path);

    /// <summary>Sends a GET request, asserts success, and returns the JSON body.</summary>
    public async Task<JsonNode> GetJsonAsync(string path)
    {
        using var response = await Http.GetAsync(path);
        return await ReadAsync(response, 200);
    }

    /// <summary>Loads a node and returns its JSON.</summary>
    public Task<JsonNode> NodeAsync(Guid id) => GetJsonAsync($"/api/nodes/{id}");

    /// <summary>Reads the current generation through the roots endpoint.</summary>
    public async Task<long> GenerationAsync() =>
        (await GetJsonAsync("/api/nodes/roots?limit=1"))["generation"]!.GetValue<long>();

    /// <summary>Sends an Apply request with a fresh or the given idempotency key.</summary>
    public Task<HttpResponseMessage> ApplyAsync(object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/changes") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Requested-With", "tree-editor");
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return Http.SendAsync(request);
    }

    /// <summary>Sends a Reset request.</summary>
    public Task<HttpResponseMessage> ResetAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/reset") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-Requested-With", "tree-editor");
        return Http.SendAsync(request);
    }

    /// <summary>Asserts the status code and returns the JSON body.</summary>
    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response, int expectedStatus)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expectedStatus, $"Expected {expectedStatus} but got {(int)response.StatusCode}: {text}");
        return JsonNode.Parse(text) ?? throw new JsonException("Empty body.");
    }
}
