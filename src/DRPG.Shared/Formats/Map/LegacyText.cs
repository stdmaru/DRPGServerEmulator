using System.Text;

namespace DRPG.Shared.Formats.Map;

/// <summary>
/// The file names inside the map files are written in the Korean code page (949), like the original's strings
/// (✅ e.g. <c>074_자갈.obj</c> next to <c>074_01.obj</c>); the pack index stores the same names in UTF-16.
/// </summary>
internal static class LegacyText
{
    private static readonly Encoding Korean = Create();

    private static Encoding Create()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(949);
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1;                     // no code page support: ASCII names still work
        }
    }

    /// <summary>The text of a zero-terminated fixed field.</summary>
    public static string FixedString(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return Korean.GetString(end < 0 ? field : field[..end]);
    }
}
