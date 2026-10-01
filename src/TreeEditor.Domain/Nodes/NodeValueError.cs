namespace TreeEditor.Domain.Nodes;

/// <summary>Reasons a text is not a valid node value.</summary>
public enum NodeValueError
{
    Empty,
    TooLong,
    SurroundingWhitespace,
    NullCharacter,
}
