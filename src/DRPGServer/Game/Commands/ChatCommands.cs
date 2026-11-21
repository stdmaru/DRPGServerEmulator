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

    public ChatCommands(ZoneSystem zones, IOptions<GameOptions> options, ILogger<ChatCommands> log)
    {
        _log = log;
        int maxLevel = options.Value.DigimonMaxLevel;

        Add("help", "/help", Authority.PrivilegedUser, (p, _) =>
            Reply(p, string.Join(' ', _commands.Values.Where(c => p.Account.Authority >= c.Required).Select(c => c.Usage))));

        Add("heal", "/heal", Authority.GameMaster, (p, _) =>
        {
            p.Leader.RestoreAll();
            p.Send(new SingleDigimonInfoPacket(p.Leader));
        });

        Add("levelup", "/levelup [n]", Authority.GameMaster, (p, args) =>
        {
            int n = args.Length > 0 && int.TryParse(args[0], out var v) ? Math.Clamp(v, 1, maxLevel) : 1;
            for (int i = 0; i < n && p.Leader.Level < maxLevel; i++)
                p.Leader.LevelUp();
            p.Send(new LevelUpPacket(p.Leader));
            p.Send(new SingleDigimonInfoPacket(p.Leader));
        });

        Add("killenemies", "/killenemies", Authority.GameMaster, (p, _) => p.Battle?.KnockOut(BattleSide.B));

        Add("reloadspawns", "/reloadspawns", Authority.Administrator, (p, _) => p.Zone?.ReloadSpawns());

        Add("teleport", "/teleport x y [mapa]", Authority.GameMaster, (p, args) =>
        {
            if (args.Length < 2 || !short.TryParse(args[0], out var x) || !short.TryParse(args[1], out var y)) return;
            byte map = p.Character.MapId;
            if (args.Length >= 3 && !byte.TryParse(args[2], out map)) return;
            zones.Teleport(p, map, x, y);
        });
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
