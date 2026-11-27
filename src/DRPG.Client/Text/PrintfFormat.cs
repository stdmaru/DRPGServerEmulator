using System.Globalization;
using System.Text;

namespace DRPG.Client.Text;

/// <summary>
/// The <c>sprintf</c> subset the interface texts use (✅ <c>TextAttach</c>: <c>Level %3d</c>, <c>%d Win %d Lose %0.2f%%</c>,
/// <c>(%d,%d)</c>, <c>(%d)</c>, <c>%s</c>). A conversion the subset does not know, or one with no argument left, is
/// written as it is: the text of a table never makes this throw.
/// </summary>
public static class PrintfFormat
{
    public static string Format(string format, params object[] arguments)
    {
        var result = new StringBuilder(format.Length + 16);
        int argument = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%')
            {
                result.Append(format[i]);
                continue;
            }
            int start = i++;
            if (i < format.Length && format[i] == '%')
            {
                result.Append('%');
                continue;
            }
            bool zeroPad = i < format.Length && format[i] == '0';
            int width = ReadNumber(format, ref i);
            int precision = -1;
            if (i < format.Length && format[i] == '.')
            {
                i++;
                precision = ReadNumber(format, ref i);
            }
            // ✅ the length prefixes of the MSVC printf the texts use ("EXP %I64d/%I64d", 0x885) only change the argument's size
            if (string.CompareOrdinal(format, i, "I64", 0, 3) == 0)
                i += 3;
            while (i < format.Length && format[i] is 'l' or 'h')
                i++;
            if (i >= format.Length || argument >= arguments.Length)
            {
                result.Append(format, start, Math.Min(i + 1, format.Length) - start);
                continue;
            }
            switch (format[i])
            {
                case 'd':
                    result.Append(Pad(ToNumber(arguments[argument++]).ToString("F0", CultureInfo.InvariantCulture), width, zeroPad));
                    break;
                case 'f':
                    var number = ToNumber(arguments[argument++]);
                    result.Append(Pad(number.ToString("F" + (precision < 0 ? 6 : Math.Min(precision, 20)), CultureInfo.InvariantCulture), width, zeroPad));
                    break;
                case 's':
                    result.Append(Pad(Convert.ToString(arguments[argument++], CultureInfo.InvariantCulture) ?? "", width, false));
                    break;
                default:
                    result.Append(format, start, i + 1 - start);
                    break;
            }
        }
        return result.ToString();
    }

    /// <summary>The argument as a number; something that is not one prints as 0 (the text must never throw).</summary>
    private static double ToNumber(object argument)
    {
        try
        {
            return Convert.ToDouble(argument, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }

    private static int ReadNumber(string text, ref int index)
    {
        int value = 0;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            value = Math.Min(value * 10 + (text[index] - '0'), 200);
            index++;
        }
        return value;
    }

    private static string Pad(string text, int width, bool zeros) =>
        zeros && text.StartsWith('-') ? "-" + text[1..].PadLeft(width - 1, '0') : text.PadLeft(width, zeros ? '0' : ' ');
}
