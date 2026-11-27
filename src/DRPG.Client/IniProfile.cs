namespace DRPG.Client;

/// <summary>✅ <c>GetPrivateProfileInt</c> over the values of <c>IniFile</c> (keys <c>section.key</c>, lower case).</summary>
public static class IniProfile
{
    /// <summary>A missing key is <paramref name="fallback"/>; a present one is its leading digits (none = 0), as Windows reads it.</summary>
    public static int ReadInt(IReadOnlyDictionary<string, string> ini, string key, int fallback)
    {
        if (!ini.TryGetValue(key, out var text))
            return fallback;
        int value = 0;
        foreach (char c in text)
        {
            if (c is < '0' or > '9')
                break;
            value = unchecked(value * 10 + (c - '0'));
        }
        return value;
    }

    /// <summary>The options the original keeps in a byte: only the low byte counts (256 is 0).</summary>
    public static int ReadByte(IReadOnlyDictionary<string, string> ini, string key, int fallback) => (byte)ReadInt(ini, key, fallback);
}
