using System.Buffers.Binary;
using TreeEditor.Application.Ports;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Infrastructure.Sample;

/// <summary>
/// A fixed sample tree: six levels deep, several branching nodes, and one node with 25 children, more than one
/// default page. Ids are UUIDv7 values with a constant timestamp and an increasing counter, so they are the same on
/// every start and sort in the order the nodes are declared.
/// </summary>
internal sealed class FixedSampleTreeSource : ISampleTreeSource
{
    private const long TimestampMilliseconds = 1_704_067_200_000;

    private static readonly IReadOnlyList<SeedNode> Nodes = Build();

    /// <inheritdoc />
    public IReadOnlyList<SeedNode> GetNodes() => Nodes;

    private static List<SeedNode> Build()
    {
        var nodes = new List<SeedNode>();

        NodeId Add(NodeId? parent, string value)
        {
            var id = IdFor(nodes.Count + 1);
            nodes.Add(new SeedNode(id, parent, NodeValue.Create(value)));
            return id;
        }

        var company = Add(null, "Company");
        var engineering = Add(company, "Engineering");
        var platform = Add(engineering, "Platform");
        var storage = Add(platform, "Storage");
        var replication = Add(storage, "Replication");
        Add(replication, "Raft log compaction");
        Add(replication, "Snapshot transfer");
        var backups = Add(storage, "Backups");
        Add(backups, "Nightly full backup");
        Add(backups, "Point-in-time recovery");
        var networking = Add(platform, "Networking");
        Add(networking, "Load balancers");
        Add(networking, "Service mesh");
        var product = Add(engineering, "Product");
        var web = Add(product, "Web app");
        Add(web, "Checkout");
        Add(web, "Search page");
        Add(product, "Mobile app");
        var operations = Add(company, "Operations");
        var support = Add(operations, "Support");
        Add(support, "Tier 1");
        Add(support, "Tier 2");
        Add(operations, "Facilities");
        var sales = Add(company, "Sales");
        var europe = Add(sales, "Europe");
        var germany = Add(europe, "Germany");
        Add(germany, "Berlin office");
        Add(europe, "France");
        Add(sales, "Asia");

        var library = Add(null, "Library");
        var catalog = Add(library, "Catalog");
        for (var book = 1; book <= 25; book++)
        {
            Add(catalog, $"Book {book:00}");
        }

        Add(library, "Reading room");
        Add(null, "Archive");
        return nodes;
    }

    private static NodeId IdFor(long counter)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes[..8], (TimestampMilliseconds << 16) | 0x7000);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], 0x8000_0000_0000_0000UL | (ulong)counter);
        return NodeId.Create(new Guid(bytes, bigEndian: true));
    }
}
