using System.Net;
using System.Text.RegularExpressions;
using DRPGServer.Game.Entities;
using Microsoft.Extensions.Options;

namespace DRPGServer.Configuration;

/// <summary>
/// Validação das seções do appsettings na partida (<c>ValidateOnStart</c>). Valor fora do intervalo derruba a partida
/// com a lista de erros, em vez de virar comportamento estranho em jogo. Os limites aqui são de sanidade; o que depende
/// das tabelas (mapa inicial existe) é conferido por quem carrega as tabelas.
/// </summary>
internal sealed class OptionsErrors
{
    private readonly List<string> _errors = [];

    public void Check(bool ok, string message)
    {
        if (!ok) _errors.Add(message);
    }

    public void Range(double value, double min, double max, string name) =>
        Check(double.IsFinite(value) && value >= min && value <= max, $"{name} = {value}: precisa estar entre {min} e {max}");

    public ValidateOptionsResult Result() =>
        _errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(_errors);
}

public sealed class ServersOptionsValidator : IValidateOptions<ServersOptions>
{
    public ValidateOptionsResult Validate(string? name, ServersOptions o)
    {
        var e = new OptionsErrors();
        var endpoints = new[] { ("Login", o.Login), ("Channel", o.Channel), ("Map", o.Map), ("Global", o.Global) };
        foreach (var (server, endpoint) in endpoints)
        {
            e.Range(endpoint.Port, 1, 65535, $"{ServersOptions.Section}:{server}:Port");
            e.Check(IPAddress.TryParse(endpoint.BindIP, out _), $"{ServersOptions.Section}:{server}:BindIP = '{endpoint.BindIP}': não é um IP");
        }
        e.Check(endpoints.Select(x => x.Item2.Port).Distinct().Count() == endpoints.Length,
            $"{ServersOptions.Section}: os quatro servidores precisam de portas diferentes");
        return e.Result();
    }
}

public sealed partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    /// <summary>O nome entra em <c>CREATE DATABASE `nome`</c>: só letras, números e sublinhado.</summary>
    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex SafeName();

    public ValidateOptionsResult Validate(string? name, DatabaseOptions o)
    {
        var e = new OptionsErrors();
        const string s = DatabaseOptions.Section;
        e.Check(!string.IsNullOrWhiteSpace(o.Host), $"{s}:Host vazio");
        e.Range(o.Port, 1, 65535, $"{s}:Port");
        e.Check(!string.IsNullOrWhiteSpace(o.User), $"{s}:User vazio");
        e.Check(SafeName().IsMatch(o.Name), $"{s}:Name = '{o.Name}': use só letras, números e '_' (até 64)");
        e.Check(!string.IsNullOrWhiteSpace(o.MigrationsPath), $"{s}:MigrationsPath vazio");
        e.Range(o.ConnectTimeoutSeconds, 1, 600, $"{s}:ConnectTimeoutSeconds");
        return e.Result();
    }
}

public sealed class GameOptionsValidator : IValidateOptions<GameOptions>
{
    public ValidateOptionsResult Validate(string? name, GameOptions o)
    {
        var e = new OptionsErrors();
        const string s = GameOptions.Section;
        e.Check(!string.IsNullOrWhiteSpace(o.ResourcesPath), $"{s}:ResourcesPath vazio");
        e.Check(!string.IsNullOrWhiteSpace(o.ClientTablePath), $"{s}:ClientTablePath vazio");
        e.Range(o.DigimonMaxLevel, 1, ushort.MaxValue, $"{s}:DigimonMaxLevel");
        e.Range(o.MaxStackPerItem, 1, ushort.MaxValue, $"{s}:MaxStackPerItem");
        e.Range(o.ExpRate, 0, 1000, $"{s}:ExpRate");
        e.Range(o.BitRate, 0, 1000, $"{s}:BitRate");
        e.Range(o.CaptureChance, 0, 1, $"{s}:CaptureChance");
        e.Range(o.FourthStarterDigimon, 0, 3, $"{s}:FourthStarterDigimon");
        e.Range(o.StartPosX, 0, short.MaxValue, $"{s}:StartPosX");
        e.Range(o.StartPosY, 0, short.MaxValue, $"{s}:StartPosY");
        e.Range(o.AutosaveSeconds, 5, 3600, $"{s}:AutosaveSeconds");
        e.Range(o.EventPollSeconds, 5, 86_400, $"{s}:EventPollSeconds");
        e.Range(o.AccountTicketGraceSeconds, 0, 3600, $"{s}:AccountTicketGraceSeconds");
        e.Range(o.DefaultDepositorySlots, 0, Character.MaxDepositorySlots, $"{s}:DefaultDepositorySlots");
        e.Range(o.DigimonDepositoryFee, -1, 1_000_000_000, $"{s}:DigimonDepositoryFee");
        return e.Result();
    }
}
