namespace DRPG.Shared.Formats.Pack;

/// <summary>A <c>.hf/.pf</c> pair is missing, corrupt or outside the supported format.</summary>
public sealed class PackFormatException : Exception
{
    public PackFormatException(string message) : base(message) { }
    public PackFormatException(string message, Exception inner) : base(message, inner) { }
}
