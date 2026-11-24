using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.Npcs;

/// <summary>
/// Mapa 79 — <c>Village of Beginning</c> (área inicial / tutorial, sessões 53 e 61).
/// ✅ Escolhas 1–4 (manuais), 5 (Tunomon: 5 Yuramon's Seeds → Carta de Apresentação) e 8 (fim do tutorial → Mapa 1).
/// </summary>
public sealed class VillageOfBeginningNpcs : NpcScriptBase
{
    public const byte TutorialMapId = 79;

    /// <summary>Yuramon's Seed: o Tunomon pede 5.</summary>
    public const uint YuramonSeed = 41;
    public const uint SeedsForTunomon = 5;

    /// <summary>Woodmon's Leaf, na bolsa de batalha: 50 no fim do tutorial.</summary>
    public const uint WoodmonLeaf = 74;
    public const uint LeavesAtEnd = 50;

    /// <summary>✅ Escolha 5 (Tunomon): carta 22008 em troca das sementes.</summary>
    public const uint TunomonCard = 22008;

    /// <summary>✅ Escolha 8 (fim do tutorial): 24171 e 24172 na aba Items + teleporte para o Mapa 1 (58, 82).</summary>
    private static readonly (uint ItemId, uint Amount)[] FinalItems = [(24171, 1), (24172, 1)];
    public const byte FinalMap = 1;
    public const short FinalX = 58, FinalY = 82;

    public const uint TunomonChoice = 5;
    public const uint FinalChoice = 8;

    private readonly ZoneSystem _zones;

    public override byte MapId => TutorialMapId;

    public VillageOfBeginningNpcs(ZoneSystem zones)
    {
        _zones = zones;

        // ✅ Escolhas 1–4: um manual cada (22004 a 22007)
        RegisterChoice(1, p => GiveItemOnce(p, 1, 22004));
        RegisterChoice(2, p => GiveItemOnce(p, 2, 22005));
        RegisterChoice(3, p => GiveItemOnce(p, 3, 22006));
        RegisterChoice(4, p => GiveItemOnce(p, 4, 22007));

        RegisterChoice(TunomonChoice, p => ExchangeItemsOnce(p, TunomonChoice, YuramonSeed, SeedsForTunomon, (TunomonCard, 1)));
        RegisterChoice(FinalChoice, FinishTutorial);
    }

    private bool FinishTutorial(Player player)
    {
        if (HasDone(player, FinalChoice) || !HasDone(player, TunomonChoice))
            return false;

        var inventory = player.Character.Inventory;
        if (FinalItems.Any(i => inventory.Main.Room(i.ItemId) < i.Amount) || inventory.Battle.Room(WoodmonLeaf) < LeavesAtEnd)
            return false;

        _zones.Teleport(player, FinalMap, FinalX, FinalY);
        player.Send(new NpcChoicePacket(FinalChoice));
        MarkDone(player, FinalChoice);
        player.GiveCard(WoodmonLeaf, LeavesAtEnd, refresh: false);
        player.GiveItems(FinalItems, refresh: true);

        // ✅ o oficial repete aqui os 0x7E e 0x7F da entrada
        for (int i = 0x29; i <= 0x2F; i++) player.Send(new Unknown7EPacket(i));
        for (int i = 0x3D; i <= 0x43; i++) player.Send(new Unknown7FPacket(i));
        return true;
    }
}
