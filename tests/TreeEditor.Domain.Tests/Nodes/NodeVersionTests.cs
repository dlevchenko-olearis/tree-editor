using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Tests.Nodes;

public sealed class NodeVersionTests
{
    [Fact]
    public void Accepts_a_positive_version()
    {
        Assert.True(NodeVersion.TryCreate(42, out var version));
        Assert.Equal(42, version.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_version_that_is_not_positive(long value)
    {
        Assert.False(NodeVersion.TryCreate(value, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => NodeVersion.Create(value));
    }

    [Fact]
    public void Compares_by_value()
    {
        Assert.Equal(NodeVersion.Create(7), NodeVersion.Create(7));
        Assert.NotEqual(NodeVersion.Create(7), NodeVersion.Create(8));
    }
}
