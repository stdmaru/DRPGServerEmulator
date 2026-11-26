using DRPG.Server.Data.GameData;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.World;

namespace DRPG.Server.Game.Npcs;

/// <summary>
/// Roteador central das escolhas de NPC (<c>Map 0x56</c>). Coleta todos os <see cref="NpcScriptBase"/> registrados
/// no DI e, na partida, recusa <c>choiceId</c> duplicado e NPC que não existe no mapa do script. Antes de executar,
/// confere pelo <see cref="InteractionGuard"/> que o jogador está perto do NPC da escolha.
/// </summary>
public sealed class NpcScriptRegistry
{
    private readonly Dictionary<uint, (NpcScriptBase Script, NpcScriptBase.Choice Choice)> _routes = [];
    private readonly InteractionGuard _guard;

    public NpcScriptRegistry(IEnumerable<NpcScriptBase> scripts, GameData data, InteractionGuard guard)
    {
        _guard = guard;
        foreach (var script in scripts)
        {
            foreach (var (choiceId, choice) in script.Choices)
            {
                if (data.Npcs.Find(script.MapId, choice.NpcId) is null)
                    throw new InvalidOperationException(
                        $"{script.GetType().Name}: escolha 0x56 #{choiceId} aponta para o NPC {script.MapId}:{choice.NpcId}, que não está em npc_info_us.");
                if (!_routes.TryAdd(choiceId, (script, choice)))
                {
                    var existing = _routes[choiceId].Script.GetType().Name;
                    throw new InvalidOperationException(
                        $"Escolha 0x56 #{choiceId} registrada em duplicidade por {existing} e {script.GetType().Name}.");
                }
            }
        }
    }

    /// <summary>Executa a escolha <paramref name="choice"/> se ela existir e o jogador estiver perto do NPC dela.</summary>
    public bool Choose(Player player, uint choice)
    {
        if (choice > ushort.MaxValue || !_routes.TryGetValue(choice, out var route))
        {
            player.SendDebug($"NPC Choice ID {choice} not registered (map={player.Character.MapId})");
            return false;
        }
        if (!_guard.CanTalkTo(player, route.Script.MapId, route.Choice.NpcId))
        {
            player.SendDebug($"NPC Choice ID {choice} requires NPC {route.Script.MapId}:{route.Choice.NpcId} nearby");
            return false;
        }
        return route.Choice.Handler(player);
    }
}
