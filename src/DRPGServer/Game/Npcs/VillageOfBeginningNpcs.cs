using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.Npcs;

/// <summary>
/// Mapa 79 — <c>Village of Beginning</c> (área inicial / tutorial, sessões 53, 61 e 64).
/// ✅ Escolhas 1–4 (manuais), 5 (Tunomon: 5 Yuramon's Seeds → Carta de Apresentação),
/// 7 (quiz perfeito: 100× Golden Drill X → Mapa 1, conn=480) e 8 (quiz médio: 50× Golden Drill X → Mapa 1, conn=92/205).
/// </summary>
public sealed class VillageOfBeginningNpcs : NpcScriptBase
{
    public const byte TutorialMapId = 79;

    /// <summary>Yuramon's Seed: o Tunomon pede 5.</summary>
    public const uint YuramonSeed = 41;
    public const uint SeedsForTunomon = 5;

    /// <summary>✅ Card #74 Golden Drill X (card_info_us.dat): 100 no quiz perfeito (escolha 7) ou 50 no médio (escolha 8).</summary>
    public const uint GoldenDrillX = 74;
    public const uint GoldenDrillAtEndNormal = 50;
    public const uint GoldenDrillAtEndPerfect = 100;

    /// <summary>✅ Escolha 5 (Tunomon): carta 22008 em troca das sementes.</summary>
    public const uint TunomonCard = 22008;

    /// <summary>✅ Escolhas 7/8 (fim do tutorial): 24171 e 24172 na aba Items + teleporte para o Mapa 1 (58, 82).</summary>
    private static readonly (uint ItemId, int Amount)[] FinalItems = [(24171, 1), (24172, 1)];
    public const byte FinalMap = 1;
    public const short FinalX = 58, FinalY = 82;

    /// <summary>✅ NPCs do mapa 79 que oferecem as escolhas (<c>npc_info_us</c>; posição de cada escolha nas capturas).</summary>
    public const ushort PatamonNpc = 123, YukimiBotamonNpc = 124, MochimonNpc = 125, PyocomonNpc = 126,
        TunomonNpc = 127, ElecmonNpc = 128;

    public const uint TunomonChoice = 5;
    public const uint FinalChoicePerfect = 7;
    public const uint FinalChoice = 8;

    private readonly ZoneSystem _zones;

    public override byte MapId => TutorialMapId;

    public VillageOfBeginningNpcs(ZoneSystem zones)
    {
        _zones = zones;

        // ✅ Escolhas 1–4: um manual cada (22004 a 22007)
        RegisterChoice(1, PatamonNpc, p => GiveItemOnce(p, 1, 22004));
        RegisterChoice(2, YukimiBotamonNpc, p => GiveItemOnce(p, 2, 22005));
        RegisterChoice(3, MochimonNpc, p => GiveItemOnce(p, 3, 22006));
        RegisterChoice(4, PyocomonNpc, p => GiveItemOnce(p, 4, 22007));

        RegisterChoice(TunomonChoice, TunomonNpc,
            p => ExchangeItemsOnce(p, TunomonChoice, YuramonSeed, SeedsForTunomon, (TunomonCard, 1)));
        RegisterChoice(FinalChoicePerfect, ElecmonNpc, p => FinishTutorial(p, FinalChoicePerfect, GoldenDrillAtEndPerfect));
        RegisterChoice(FinalChoice, ElecmonNpc, p => FinishTutorial(p, FinalChoice, GoldenDrillAtEndNormal));
    }

    private bool FinishTutorial(Player player, uint choiceId, uint drillAmount)
    {
        if (HasDone(player, FinalChoice) || HasDone(player, FinalChoicePerfect) || !HasDone(player, TunomonChoice))
            return false;

        var inventory = player.Character.Inventory;
        if (FinalItems.Any(i => inventory.Main.Room(i.ItemId) < i.Amount) || inventory.Battle.Room(GoldenDrillX) < drillAmount)
            return false;

        _zones.Teleport(player, FinalMap, FinalX, FinalY);
        player.Send(new NpcChoicePacket(choiceId));
        MarkDone(player, FinalChoice);
        player.GiveCard(GoldenDrillX, drillAmount, refresh: false);
        player.GiveItems(FinalItems, refresh: true);

        // ✅ o oficial repete aqui os 0x7E e 0x7F da entrada
        for (int i = 0x29; i <= 0x2F; i++) player.Send(new Unknown7EPacket(i));
        for (int i = 0x3D; i <= 0x43; i++) player.Send(new Unknown7FPacket(i));
        return true;
    }
}
