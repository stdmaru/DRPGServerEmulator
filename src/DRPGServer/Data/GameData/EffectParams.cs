using System.Globalization;
using System.Xml.Linq;

namespace DRPGServer.Data.GameData;

/// <summary>
/// Parâmetros de um <c>&lt;Effect Type="..." /&gt;</c>. A fábrica do efeito lê o que declara; o loader chama
/// <see cref="EnsureAllRead"/> depois, e atributo que nenhum parâmetro leu derruba a carga.
/// </summary>
public sealed class EffectParams
{
    private readonly XElement _element;
    private readonly HashSet<string> _read = ["Type"];

    public EffectParams(XElement element, string where)
    {
        _element = element;
        Where = where;
        Type = element.Attribute("Type")?.Value ?? throw new GameDataException($"{where}: <Effect> sem Type");
    }

    public string Type { get; }
    public string Where { get; }

    public bool Has(string name) => _element.Attribute(name) is not null;

    /// <summary>Inteiro &gt; 0 obrigatório.</summary>
    public int PositiveInt(string name) =>
        OptionalPositiveInt(name) ?? throw Fail($"falta o parâmetro {name}");

    /// <summary>Inteiro &gt; 0, ou null se o atributo não existe.</summary>
    public int? OptionalPositiveInt(string name)
    {
        if (Raw(name) is not { } text) return null;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value <= 0)
            throw Fail($"{name}=\"{text}\" não é inteiro > 0");
        return value;
    }

    /// <summary>Valor de enum pelo nome exato (número não vale).</summary>
    public T Enum<T>(string name) where T : struct, Enum
    {
        string text = Raw(name) ?? throw Fail($"falta o parâmetro {name}");
        if (int.TryParse(text, out _) || !System.Enum.TryParse<T>(text, out var value) || !System.Enum.IsDefined(value))
            throw Fail($"{name}=\"{text}\" desconhecido");
        return value;
    }

    /// <summary>Exatamente um dos dois parâmetros (ex.: <c>Amount</c> ou <c>Percent</c>).</summary>
    public void RequireOneOf(string first, string second)
    {
        if (Has(first) == Has(second))
            throw Fail($"precisa de {first} ou {second} (só um)");
    }

    public GameDataException Fail(string message) => new($"{Where}: <Effect Type=\"{Type}\"> {message}");

    public void EnsureAllRead()
    {
        foreach (var attr in _element.Attributes())
        {
            if (!_read.Contains(attr.Name.LocalName))
                throw Fail($"não aceita o atributo {attr.Name}");
        }
    }

    private string? Raw(string name)
    {
        _read.Add(name);
        return _element.Attribute(name)?.Value;
    }
}
