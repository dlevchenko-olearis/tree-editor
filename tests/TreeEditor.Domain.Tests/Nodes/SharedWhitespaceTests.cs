using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Tests.Nodes;

public sealed class SharedWhitespaceTests
{
    private static readonly int[] Expected =
    [
        0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x0020,
        0x0085, 0x00A0, 0x1680,
        0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A,
        0x2028, 0x2029, 0x202F, 0x205F, 0x3000, 0xFEFF,
    ];

    [Fact]
    public void Contains_exactly_the_shared_set()
    {
        var actual = Enumerable.Range(char.MinValue, char.MaxValue + 1)
            .Where(code => SharedWhitespace.Contains((char)code))
            .ToArray();

        Assert.Equal(Expected, actual);
    }

    [Fact]
    public void Covers_everything_that_dotnet_trim_removes()
    {
        var trimmedByDotnet = Enumerable.Range(char.MinValue, char.MaxValue + 1)
            .Select(code => (char)code)
            .Where(char.IsWhiteSpace);

        Assert.All(trimmedByDotnet, character => Assert.True(SharedWhitespace.Contains(character)));
    }
}
