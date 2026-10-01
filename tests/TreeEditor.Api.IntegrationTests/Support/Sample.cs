using TreeEditor.Infrastructure.Sample;

namespace TreeEditor.Api.IntegrationTests.Support;

/// <summary>Ids of the sample tree nodes, looked up by value.</summary>
public static class Sample
{
    private static readonly Dictionary<string, Guid> Ids = new FixedSampleTreeSource()
        .GetNodes()
        .ToDictionary(node => node.Value.Value, node => node.Id.Value);

    /// <summary>Number of nodes in the sample tree.</summary>
    public static int Count => Ids.Count;

    /// <summary>The id of the sample node with <paramref name="value"/>.</summary>
    public static Guid Id(string value) => Ids[value];
}
