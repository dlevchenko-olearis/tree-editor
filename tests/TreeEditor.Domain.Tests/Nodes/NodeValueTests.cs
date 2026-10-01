using System.Text;
using TreeEditor.Domain.Nodes;

namespace TreeEditor.Domain.Tests.Nodes;

public sealed class NodeValueTests
{
    [Fact]
    public void Accepts_200_code_units()
    {
        var text = new string('a', 200);

        Assert.Null(NodeValue.Validate(text));
        Assert.Equal(text, NodeValue.Create(text).Value);
    }

    [Fact]
    public void Rejects_201_code_units()
    {
        Assert.Equal(NodeValueError.TooLong, NodeValue.Validate(new string('a', 201)));
    }

    [Fact]
    public void Counts_utf16_code_units_rather_than_code_points()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        var hundredEmoji = string.Concat(Enumerable.Repeat(emoji, 100));

        Assert.Null(NodeValue.Validate(hundredEmoji));
        Assert.Equal(NodeValueError.TooLong, NodeValue.Validate(hundredEmoji + emoji));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Rejects_an_empty_value(string? text)
    {
        Assert.Equal(NodeValueError.Empty, NodeValue.Validate(text));
    }

    [Theory]
    [InlineData(0x0020)]
    [InlineData(0x0009)]
    [InlineData(0x000A)]
    [InlineData(0x0085)]
    [InlineData(0x00A0)]
    [InlineData(0x2000)]
    [InlineData(0x200A)]
    [InlineData(0x2028)]
    [InlineData(0x3000)]
    [InlineData(0xFEFF)]
    public void Rejects_leading_or_trailing_whitespace_from_the_shared_set(int code)
    {
        var whitespace = (char)code;

        Assert.Equal(NodeValueError.SurroundingWhitespace, NodeValue.Validate(whitespace + "abc"));
        Assert.Equal(NodeValueError.SurroundingWhitespace, NodeValue.Validate("abc" + whitespace));
        Assert.Equal(NodeValueError.SurroundingWhitespace, NodeValue.Validate(whitespace.ToString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Rejects_a_nul_character_anywhere(int position)
    {
        var text = "ab".Insert(position, ((char)0).ToString());

        Assert.Equal(NodeValueError.NullCharacter, NodeValue.Validate(text));
    }

    [Theory]
    [InlineData(0x0020)]
    [InlineData(0x0085)]
    [InlineData(0xFEFF)]
    public void Accepts_whitespace_inside_the_value(int code)
    {
        Assert.Null(NodeValue.Validate("a" + (char)code + "b"));
    }

    [Theory]
    [InlineData(0x200B)]
    [InlineData(0x180E)]
    public void Accepts_edge_characters_outside_the_shared_set(int code)
    {
        Assert.Null(NodeValue.Validate((char)code + "abc"));
    }

    [Fact]
    public void Never_transforms_the_value()
    {
        var text = new StringBuilder("a").Append("  ").Append('b').ToString();

        Assert.Same(text, NodeValue.Create(text).Value);
    }

    [Fact]
    public void Create_throws_for_an_invalid_value()
    {
        Assert.Throws<ArgumentException>(() => NodeValue.Create(" abc"));
    }
}
