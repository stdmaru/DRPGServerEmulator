using System.Globalization;
using System.Xml.Linq;
using DRPGServer.Game.Stats;

namespace DRPGServer.Data.GameData;

/// <summary>
/// Lê os filhos <c>&lt;Modifier Kind="Atk" Value="15" /&gt;</c> ou <c>&lt;Modifier Kind="Atk" Percent="5" /&gt;</c> de uma
/// fonte de bônus (equipamento, título, card). Tipo desconhecido, forma não aceita, atributo a mais ou elemento
/// estranho derrubam a carga.
/// </summary>
public static class ModifierXml
{
    /// <summary>Lê os filhos de <paramref name="owner"/>, que só pode ter <c>&lt;Modifier&gt;</c>.</summary>
    public static StatModifiers Read(XElement owner, string where)
    {
        foreach (var child in owner.Elements())
        {
            if (child.Name.LocalName != "Modifier")
                throw new GameDataException($"{where}: elemento <{child.Name}> desconhecido");
        }
        return Read(owner.Elements("Modifier"), where);
    }

    /// <summary>Lê elementos <c>&lt;Modifier&gt;</c> já separados (o dono valida os outros filhos).</summary>
    public static StatModifiers Read(IEnumerable<XElement> modifiers, string where) =>
        StatModifiers.Of(modifiers.Select(e => ReadOne(e, where)).ToList());

    private static StatModifier ReadOne(XElement e, string where)
    {
        string kindText = e.Attribute("Kind")?.Value ?? throw new GameDataException($"{where}: <Modifier> sem Kind");
        if (!Enum.TryParse<StatModifierKind>(kindText, ignoreCase: false, out var kind) || !Enum.IsDefined(kind)
            || int.TryParse(kindText, out _))
            throw new GameDataException($"{where}: Kind \"{kindText}\" desconhecido");

        foreach (var attr in e.Attributes())
        {
            if (attr.Name.LocalName is not ("Kind" or "Value" or "Percent"))
                throw new GameDataException($"{where}: <Modifier Kind=\"{kind}\"> com atributo {attr.Name} desconhecido");
        }

        var value = e.Attribute("Value");
        var percent = e.Attribute("Percent");
        if ((value is null) == (percent is null))
            throw new GameDataException($"{where}: <Modifier Kind=\"{kind}\"> precisa de Value ou Percent (só um)");

        var form = percent is null ? StatModifierForm.Flat : StatModifierForm.Percent;
        if (!StatModifierRules.Allows(kind, form))
            throw new GameDataException($"{where}: {kind} não aceita {(form == StatModifierForm.Percent ? "Percent" : "Value")}");

        string text = (value ?? percent)!.Value;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
            throw new GameDataException($"{where}: <Modifier Kind=\"{kind}\"> com valor \"{text}\" inválido (inteiro > 0)");

        return new StatModifier(kind, amount, form);
    }
}
