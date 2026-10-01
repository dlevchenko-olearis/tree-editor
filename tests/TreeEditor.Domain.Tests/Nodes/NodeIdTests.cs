using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Tests.Nodes;

public sealed class NodeIdTests
{
    [Fact]
    public void Accepts_a_version_7_uuid()
    {
        var value = Guid.CreateVersion7();

        Assert.True(NodeId.TryCreate(value, out var id));
        Assert.Equal(value, id.Value);
    }

    [Fact]
    public void Rejects_a_version_4_uuid()
    {
        Assert.False(NodeId.TryCreate(Guid.NewGuid(), out _));
    }

    [Fact]
    public void Rejects_the_nil_uuid()
    {
        Assert.False(NodeId.TryCreate(Guid.Empty, out _));
        Assert.Throws<ArgumentException>(() => NodeId.Create(Guid.Empty));
    }

    [Fact]
    public void Rejects_the_max_uuid()
    {
        Assert.False(NodeId.TryCreate(Guid.AllBitsSet, out _));
    }

    [Theory]
    [InlineData("01890a5d-ac96-7a3b-0c2d-0123456789ab")]
    [InlineData("01890a5d-ac96-7a3b-cc2d-0123456789ab")]
    [InlineData("01890a5d-ac96-7a3b-ec2d-0123456789ab")]
    public void Rejects_a_version_7_uuid_without_the_rfc_variant(string text)
    {
        Assert.False(NodeId.TryCreate(Guid.Parse(text), out _));
    }

    [Theory]
    [InlineData("01890a5d-ac96-7a3b-8c2d-0123456789ab")]
    [InlineData("01890a5d-ac96-7a3b-bc2d-0123456789ab")]
    public void Accepts_both_ends_of_the_rfc_variant(string text)
    {
        Assert.True(NodeId.TryCreate(Guid.Parse(text), out _));
    }

    [Fact]
    public void Formats_as_the_lowercase_uuid()
    {
        var value = Guid.Parse("01890A5D-AC96-7A3B-8C2D-0123456789AB");

        Assert.Equal("01890a5d-ac96-7a3b-8c2d-0123456789ab", NodeId.Create(value).ToString());
    }
}
