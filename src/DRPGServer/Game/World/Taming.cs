using DRPGServer.Configuration;
using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Network.Packets.Map;
using Microsoft.Extensions.Options;

namespace DRPGServer.Game.World;

/// <summary>
/// Captura de digimons (docs/mecanicas/captura.md). Duas formas:
/// <list type="bullet">
/// <item>Taming Net no mapa (0x41), fora de batalha, só no estágio da rede. ✅ O selvagem continua no mapa.</item>
/// <item>Captura comum na batalha (ícone 1 do 0x35): 3 tentativas por batalha, só in-training, e o jogador precisa
/// ter um digimon rookie ou acima. ❓ Chance real muito baixa e sem amostra de sucesso; aqui é configurável.</item>
/// </list>
/// Nas duas é preciso posição livre no time. ✅ O digimon novo se chama "InputName", tem uma renomeação grátis e
/// começa no nível de entrada do estágio (in-training L1, rookie L11, mesmo com o selvagem L31).
/// </summary>
public sealed class Taming(GameData data, DigimonUids uids, IOptions<GameOptions> options)
{
    /// <summary>✅ Tentativas de captura comum por batalha (o cliente não pede a quarta).</summary>
    public const int AttemptsPerBattle = 3;

    public double BattleChance => options.Value.CaptureChance;

    public static bool HasFreeSlot(Character c) => c.Party.Any(d => d is null);

    /// <summary>✅ Captura comum: só in-training, e o jogador precisa de um digimon rookie ou acima.</summary>
    public static bool CanBattleCapture(Character c, Digimon target) =>
        target.Species.Stage <= StatFormulas.InTraining && HasFreeSlot(c)
        && c.Digimons.Any(d => d.BaseForm.Species.Stage >= StatFormulas.Rookie);

    /// <summary>Cria o digimon capturado e põe na primeira posição livre do time. Null sem posição livre.</summary>
    public Digimon? Tame(Character character, DigimonSpecies species)
    {
        int slot = character.Party.ToList().IndexOf(null);
        if (slot < 0) return null;
        var level = (ushort)StatFormulas.StageLevel(species.Stage);
        var digimon = new Digimon(data, species, level)
        {
            Uid = uids.Next(),
            Name = Digimon.DefaultName,
            FreeRename = true,
        };
        character.Place(slot, digimon);
        return digimon;
    }

    /// <summary>
    /// 0x41 com uma Taming Net no selvagem. ✅ Sucesso: 0xEF (bloco do novo) e 0x41 (rede consumida + bloco do novo).
    /// Qualquer recusa: 0xC3. False se o item não é Taming Net (outro uso de item, ainda não tratado).
    /// </summary>
    public bool UseNet(Player player, InventoryItem item, WildDigimon? wild, Random random)
    {
        if (data.FindTamingNet(item.ItemId) is not { } net) return false;
        var c = player.Character;
        if (player.InBattle || wild is not { CanBattle: true } || wild.Leader.Species.Stage != net.Stage
            || !HasFreeSlot(c) || item.Amount == 0)
        {
            player.Send(new ItemUseFailedPacket());
            return true;
        }

        c.Inventory.Main.Consume(item, 1);
        player.RequestImmediateSave();
        if (random.Next(100) >= net.Chance || Tame(c, wild.Leader.Species) is not { } digimon)
        {
            player.Send(new ItemUseFailedPacket());     // ❓ falha de rede com chance < 100 não capturada
            return true;
        }
        player.Send(new DigimonReceivedPacket(digimon));
        player.Send(new ItemUsePacket(item, digimon));
        return true;
    }
}
