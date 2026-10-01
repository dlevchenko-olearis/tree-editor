namespace TreeEditor.Domain.Nodes;

/// <summary>
/// The whitespace set shared with the web client. It is the union of what C# <c>Trim()</c> and JavaScript
/// <c>trim()</c> remove, so both sides agree on which values are trimmed.
/// </summary>
public static class SharedWhitespace
{
    private const char NextLine = (char)0x0085;
    private const char NoBreakSpace = (char)0x00A0;
    private const char OghamSpaceMark = (char)0x1680;
    private const char EnQuad = (char)0x2000;
    private const char HairSpace = (char)0x200A;
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;
    private const char NarrowNoBreakSpace = (char)0x202F;
    private const char MediumMathematicalSpace = (char)0x205F;
    private const char IdeographicSpace = (char)0x3000;
    private const char ZeroWidthNoBreakSpace = (char)0xFEFF;

    /// <summary>Tells whether <paramref name="character"/> belongs to the shared whitespace set.</summary>
    public static bool Contains(char character) => character switch
    {
        '\t' or '\n' or '\v' or '\f' or '\r' or ' ' => true,
        NextLine or NoBreakSpace or OghamSpaceMark => true,
        >= EnQuad and <= HairSpace => true,
        LineSeparator or ParagraphSeparator or NarrowNoBreakSpace => true,
        MediumMathematicalSpace or IdeographicSpace or ZeroWidthNoBreakSpace => true,
        _ => false,
    };
}
