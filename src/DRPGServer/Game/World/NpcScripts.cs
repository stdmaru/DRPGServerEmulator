using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.World;

/// <summary>
/// Escolhas de diálogo de NPC (0x56) da área inicial, como na sessão 53. ✅ Cada escolha é feita uma vez: o número
/// da escolha vira flag do personagem (persistida). Resposta: eco do 0x56, os itens recebidos (0xC8/0xC9) e o 0xFC.
/// </summary>
public sealed class NpcScripts(ZoneSystem zones)
{
    /// <summary>Yuramon's Seed: o Tunomon pede 5.</summary>
    public const uint YuramonSeed = 41;
    public const uint SeedsForTunomon = 5;

    /// <summary>Woodmon's Leaf, na bolsa de batalha: 50 no fim do tutorial.</summary>
    public const uint WoodmonLeaf = 74;
    public const uint LeavesAtEnd = 50;

    /// <summary>✅ Escolhas 1–4: um manual cada (22004 a 22007).</summary>
    private static readonly Dictionary<uint, uint> Manuals = new() { [1] = 22004, [2] = 22005, [3] = 22006, [4] = 22007 };

    /// <summary>✅ Escolha 5 (Tunomon): carta 22008 em troca das sementes.</summary>
    public const uint TunomonCard = 22008;

    /// <summary>✅ Escolha 8 (fim do tutorial): 24171 e 24172 no inventário.</summary>
    private static readonly uint[] FinalItems = [24171, 24172];

    /// <summary>✅ Escolha 8 leva para o mapa 1 em (58, 82) antes do eco.</summary>
    public const byte FinalMap = 1;
    public const short FinalX = 58, FinalY = 82;

    public const uint TunomonChoice = 5, FinalChoice = 8;

    /// <summary>Aplica a escolha. False se ela não existe, já foi feita ou falta algo (nada é enviado).</summary>
    public bool Choose(Player player, uint choice)
    {
        var c = player.Character;
        if (choice > ushort.MaxValue || c.HasFlag((ushort)choice)) return false;
        var inventory = c.Inventory;

        if (Manuals.TryGetValue(choice, out var manual))
        {
            if (inventory.Main.Room(manual) < 1) return false;
            var changed = inventory.Main.Add(manual, 1);
            Finish(player, choice, changed);
            return true;
        }

        switch (choice)
        {
            case TunomonChoice:
            {
                if (inventory.Main.Count(YuramonSeed) < SeedsForTunomon) return false;
                var removed = inventory.Main.Remove(YuramonSeed, SeedsForTunomon);
                var added = inventory.Main.Add(TunomonCard, 1);
                if (added.Count == 0)
                {
                    inventory.Main.Add(YuramonSeed, SeedsForTunomon);   // não coube: devolve
                    return false;
                }
                Finish(player, choice, [.. removed, .. added]);
                return true;
            }
            case FinalChoice:
            {
                if (!c.HasFlag((ushort)TunomonChoice)) return false;
                if (FinalItems.Any(i => inventory.Main.Room(i) < 1) || inventory.Battle.Room(WoodmonLeaf) < LeavesAtEnd) return false;
                zones.Teleport(player, FinalMap, FinalX, FinalY);
                player.Send(new NpcChoicePacket(choice));
                c.SetFlag((ushort)choice);
                player.Send(new BattleItemReceivePacket(inventory.Battle.Add(WoodmonLeaf, LeavesAtEnd)));
                player.Send(new ItemReceivePacket(FinalItems.SelectMany(i => inventory.Main.Add(i, 1)).ToList()));
                player.Send(new InventoryRefreshPacket(c));
                // ✅ o oficial repete aqui os 0x7E e 0x7F da entrada
                for (int i = 0x29; i <= 0x2F; i++) player.Send(new Unknown7EPacket(i));
                for (int i = 0x3D; i <= 0x43; i++) player.Send(new Unknown7FPacket(i));
                return true;
            }
            default:
                return false;
        }
    }

    private static void Finish(Player player, uint choice, IReadOnlyList<InventoryItem> changed)
    {
        player.Character.SetFlag((ushort)choice);
        player.Send(new NpcChoicePacket(choice));
        // posição esvaziada e ocupada de novo na mesma troca: vale o último estado
        player.Send(new ItemReceivePacket(changed.GroupBy(i => i.Slot).Select(g => g.Last()).ToList()));
        player.Send(new InventoryRefreshPacket(player.Character));
    }
}
