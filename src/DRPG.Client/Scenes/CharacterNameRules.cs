using System.Text;
using DRPG.Client.Text;
using DRPG.Shared.Packets.Channel;

namespace DRPG.Client.Scenes;

/// <summary>Which edit box receives focus when character creation validation fails.</summary>
public enum NameField
{
    Tamer,
    Digimon,
}

/// <summary>The first validation error found by <see cref="CharacterNameRules.Validate"/>.</summary>
public readonly record struct NameValidationError(int TextId, NameField Focus);

/// <summary>
/// Pure character creation rules of <c>CSceneCharSel</c> (✅ v322 <c>0x00b48c60</c>, <c>0x00b45510</c>, <c>0x00b45990</c>,
/// docs/cliente/cenas.md#criação-de-personagem-modo-1--re): name validation in the original's exact order, target slot
/// selection, fourth starter mapping and the create request header.
/// </summary>
public static class CharacterNameRules
{
    // ✅ TextAttach ids from 0x00b48c60
    public const int NonAsciiText = 0xee5;
    public const int TamerInvalidCharText = 0x25e;
    public const int TamerProhibitedText = 0x25f;
    public const int TamerLengthText = 0x260;
    public const int DigimonInvalidCharText = 0x261;
    public const int DigimonProhibitedText = 0x262;
    public const int DigimonLengthText = 0x263;
    public const int NoEmptySlotText = 0x264;

    public const int MinLength = 3;
    public const int MaxLength = 16;

    /// <summary>✅ <c>0x00b45990</c>: tutorial map id sent in the create header.</summary>
    public const byte CreateMapId = 0x4F;

    /// <summary>
    /// Validates tamer and digimon names in the exact order of <c>0x00b48c60</c>:
    /// 1. Any non-ASCII byte in either name → <c>0xee5</c> (focus tamer).
    /// 2. Tamer empty or non-alphanumeric → <c>0x25e</c>.
    /// 3. Tamer contains a prohibited word → <c>0x25f</c>.
    /// 4. Tamer byte length &lt;= 2 or &gt;= 17 → <c>0x260</c>.
    /// 5–7. Same three checks for digimon → <c>0x261</c>, <c>0x262</c>, <c>0x263</c>.
    /// </summary>
    public static NameValidationError? Validate(string tamerName, string digimonName, ProhibitedWords prohibited)
    {
        if (HasNonAscii(tamerName) || HasNonAscii(digimonName))
            return new(NonAsciiText, NameField.Tamer);

        if (!IsAsciiAlphanumeric(tamerName))
            return new(TamerInvalidCharText, NameField.Tamer);
        if (prohibited.ContainsProhibited(tamerName))
            return new(TamerProhibitedText, NameField.Tamer);
        if (!HasValidByteLength(tamerName))
            return new(TamerLengthText, NameField.Tamer);

        if (!IsAsciiAlphanumeric(digimonName))
            return new(DigimonInvalidCharText, NameField.Digimon);
        if (prohibited.ContainsProhibited(digimonName))
            return new(DigimonProhibitedText, NameField.Digimon);
        if (!HasValidByteLength(digimonName))
            return new(DigimonLengthText, NameField.Digimon);

        return null;
    }

    /// <summary>
    /// ✅ <c>0x00b45510</c>: picks the target slot when entering creation mode: the selected slot if it is empty,
    /// otherwise the lowest empty slot; -1 when all slots are occupied.
    /// </summary>
    public static int PickTargetSlot(IReadOnlyList<CharacterListSlot> slots, int selectedSlot)
    {
        if (selectedSlot >= 0 && selectedSlot < slots.Count && IsSlotEmpty(slots[selectedSlot]))
            return selectedSlot;
        for (int i = 0; i < slots.Count; i++)
        {
            if (IsSlotEmpty(slots[i]))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// ✅ <c>0x00b45510</c>: maps the server's fourth starter index (from <c>0x03</c>) to its species id;
    /// outside 0..3 the fourth button is not shown.
    /// </summary>
    public static ushort? FourthStarterSpecies(int fourthStarter) => fourthStarter switch
    {
        0 => 24, // Chibimon
        1 => 27, // Yarmon
        2 => 31, // Hopmon
        3 => 40, // Dorimon
        _ => null,
    };

    /// <summary>
    /// ✅ <c>0x00b45990</c>: header of <see cref="CharacterCreateRequest"/>: slot, map <c>0x4F</c>,
    /// <c>A = 62 - rand() % 4</c> (<c>0x3B..0x3E</c>), <c>B = 55 + rand() % 4</c> (<c>0x37..0x3A</c>), in that order.
    /// </summary>
    public static CharacterCreateHeader BuildCreateHeader(byte slot, Random random)
    {
        ushort a = (ushort)(62 - random.Next(4));
        ushort b = (ushort)(55 + random.Next(4));
        return new CharacterCreateHeader(slot, CreateMapId, a, b);
    }

    private static bool IsSlotEmpty(CharacterListSlot slot) => slot.IsEmpty || string.IsNullOrEmpty(slot.Name);

    private static bool HasNonAscii(string value)
    {
        foreach (char c in value)
        {
            if (c > 0x7F)
                return true;
        }
        return false;
    }

    private static bool IsAsciiAlphanumeric(string value)
    {
        if (value.Length == 0)
            return false;
        foreach (char c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c))
                return false;
        }
        return true;
    }

    private static bool HasValidByteLength(string value)
    {
        int bytes = Encoding.UTF8.GetByteCount(value);
        return bytes >= MinLength && bytes <= MaxLength;
    }
}
