using DRPGServer.Configuration;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPGServer.Game.Commands;

/// <summary>Comandos de GM pelo chat ("/nome args"). Rodam na thread do mundo.</summary>
public sealed class ChatCommands
{
    private sealed record Command(string Name, string Usage, Authority Required, Action<Player, string[]> Run);

    private readonly Dictionary<string, Command> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ChatCommands> _log;

    public ChatCommands(ZoneSystem zones, IOptions<GameOptions> options, ILogger<ChatCommands> log,
        ExpEventService? expEvents = null)
    {
        _log = log;
        int maxLevel = options.Value.DigimonMaxLevel;

        Add("help", "/help", Authority.PrivilegedUser, (p, _) =>
            Reply(p, string.Join(' ', _commands.Values.Where(c => p.Account.Authority >= c.Required).Select(c => c.Usage))));

        Add("heal", "/heal", Authority.GameMaster, (p, _) =>
        {
            foreach (var d in p.Character.Digimons)
            {
                d.RestoreAll();
                p.Send(new SingleDigimonInfoPacket(d));
            }
        });

        Add("levelup", "/levelup [n]", Authority.GameMaster, (p, args) =>
        {
            int n = args.Length > 0 && int.TryParse(args[0], out var v) ? Math.Clamp(v, 1, maxLevel) : 1;
            if (p.InBattle) return;
            for (int i = 0; i < n && p.Leader.Level < maxLevel; i++)
            {
                p.Leader.LevelUp();
                Progression.AfterLevelUp(p, p.Leader, inBattle: false);
            }
            p.Send(new SingleDigimonInfoPacket(p.Leader));
        });

        Add("killenemies", "/killenemies", Authority.GameMaster, (p, _) => p.Battle?.KnockOut(BattleSide.B));

        Add("reloadspawns", "/reloadspawns", Authority.Administrator, (p, _) => p.Zone?.ReloadSpawns());

        Add("reloadevents", "/reloadevents", Authority.Administrator, (p, args) =>
        {
            if (expEvents is null) return;
            _ = ReloadEventsAsync(p, expEvents);
        });

        Add("teleport", "/teleport x y [mapa]", Authority.GameMaster, (p, args) =>
        {
            if (args.Length < 2 || !short.TryParse(args[0], out var x) || !short.TryParse(args[1], out var y)) return;
            byte map = p.Character.MapId;
            if (args.Length >= 3 && !byte.TryParse(args[2], out map)) return;
            zones.Teleport(p, map, x, y);
        });

        Add("givetitle", "/givetitle <titleId>", Authority.GameMaster, (p, args) =>
        {
            if (args.Length < 1 || !uint.TryParse(args[0], out var titleId) || titleId == 0) return;
            if (p.Leader.UnlockTitle(titleId))
            {
                p.RequestImmediateSave();
                Reply(p, $"Titulo {titleId} desbloqueado para {p.Leader.Name}.");
            }
            else
            {
                Reply(p, $"Titulo {titleId} ja desbloqueado ou lista cheia.");
            }
        });
    }

    private async Task ReloadEventsAsync(Player requester, ExpEventService expEvents)
    {
        try
        {
            var active = await expEvents.ReloadAsync(forceNotify: true);
            string msg = active is null
                ? "Nenhum evento de EXP ativo."
                : $"EXP +{active.BonusPercent}% ate {active.EndsAt:yyyy-MM-dd}";
            Reply(requester, msg);
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Falha em /reloadevents");
            Reply(requester, "Erro ao recarregar eventos.");
        }
    }

    private void Add(string name, string usage, Authority required, Action<Player, string[]> run) =>
        _commands[name] = new Command(name, usage, required, run);

    /// <summary>True se a linha era um comando conhecido e permitido (não vai para o chat).</summary>
    public bool TryExecute(Player player, string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !_commands.TryGetValue(parts[0], out var command)) return false;
        if (player.Account.Authority < command.Required) return false;
        _log.LogInformation("{Player} executou /{Command} {Args}", player, command.Name, string.Join(' ', parts[1..]));
        command.Run(player, parts[1..]);
        return true;
    }

    private static void Reply(Player player, string text) => player.Send(new ChatNormalPacket("[server]", text));
}
