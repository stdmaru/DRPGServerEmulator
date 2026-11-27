namespace DRPG.Shared.Formats.Map;

/// <summary>A <c>.map</c>, <c>.til</c> or <c>.obj</c> file is truncated, too large or does not match the format.</summary>
public sealed class MapFormatException(string message) : Exception(message);
