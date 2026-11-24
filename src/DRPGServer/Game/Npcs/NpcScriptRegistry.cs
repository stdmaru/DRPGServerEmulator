using DRPGServer.Game.Entities;

namespace DRPGServer.Game.Npcs;

/// <summary>
/// Roteador central das escolhas de NPC (<c>Map 0x56</c>). Coleta todos os <see cref="NpcScriptBase"/> registrados
/// no DI, detecta conflitos de <c>choiceId</c> na partida e valida se o jogador está no mapa correto antes de executar.
/// </summary>
public sealed class NpcScriptRegistry
{
    private readonly Dictionary<uint, (NpcScriptBase Script, Func<Player, bool> Handler)> _routes = [];

    public NpcScriptRegistry(IEnumerable<NpcScriptBase> scripts)
    {
        foreach (var script in scripts)
        {
            foreach (var (choiceId, handler) in script.Choices)
            {
                if (!_routes.TryAdd(choiceId, (script, handler)))
                {
                    var existing = _routes[choiceId].Script.GetType().Name;
                    throw new InvalidOperationException(
                        $"Escolha 0x56 #{choiceId} registrada em duplicidade por {existing} e {script.GetType().Name}.");
                }
            }
        }
    }

    /// <summary>Executa a escolha <paramref name="choice"/> se ela existir e o jogador estiver no mapa correspondente.</summary>
    public bool Choose(Player player, uint choice)
    {
        if (choice > ushort.MaxValue || !_routes.TryGetValue(choice, out var route))
        {
            player.SendDebug($"NPC Choice ID {choice} not registered (map={player.Character.MapId})");
            return false;
        }
        if (player.Character.MapId != route.Script.MapId)
        {
            player.SendDebug($"NPC Choice ID {choice} requires map {route.Script.MapId} (current={player.Character.MapId})");
            return false;
        }
        return route.Handler(player);
    }
}
