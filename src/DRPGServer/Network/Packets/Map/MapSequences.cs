using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPG.Shared.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>Sequências de pacotes que o oficial repete em mais de um lugar, na ordem capturada.</summary>
public static class MapSequences
{
    /// <summary>
    /// ✅ Estado do mundo na entrada no mapa (capturas v242/v322): personagem, inventário, moedas se houver, as páginas
    /// 0x7E/0x7F, evento de EXP e o líder.
    /// </summary>
    public static IEnumerable<ServerPacket> WorldState(Player player, ActiveExpEvent? expEvent)
    {
        var character = player.Character;
        yield return new CharacterDataPacket(character, character.Inventory.Clock.UnixNow);
        yield return new InventoryDataPacket(character.Inventory);
        if (character.Wallet.Medals > 0 || character.Wallet.Coins > 0)
            yield return new CurrencyUpdatePacket(character);
        foreach (var packet in ListPages())
            yield return packet;
        yield return new Unknown05Packet();
        yield return new ExpEventPacket(player.Session.MapAesKey, expEvent);
        yield return new Unknown74Packet();
        yield return new Unknown152Packet();
        yield return new SingleDigimonInfoPacket(player.Leader);
        yield return new Unknown73Packet();
    }

    /// <summary>✅ 0x7E × 7 (0x29..0x2F) e 0x7F × 7 (0x3D..0x43): na entrada no mapa e de novo no fim do tutorial.</summary>
    public static IEnumerable<ServerPacket> ListPages()
    {
        for (int i = 0x29; i <= 0x2F; i++) yield return new Unknown7EPacket(i);
        for (int i = 0x3D; i <= 0x43; i++) yield return new Unknown7FPacket(i);
    }
}
