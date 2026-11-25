using DRPGServer.Game.Entities;
using DRPGServer.Game.Npcs;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

/// <summary>Alcance e contexto das interações (docs/emulador/seguranca.md#interação-e-alcance).</summary>
public class InteractionGuardTests
{
    // pacote de teste: loja 40101 é do NPC 40:12 em (81, 154); Digimon Center 79:129 em (50, 60)
    private const uint GekomonItemShop = 40101;

    private readonly InteractionGuard _guard = TestSupport.Guard(options: TestSupport.GameOptions(o =>
    {
        o.NpcInteractionRange = 5;
        o.WildInteractionRange = 8;
        o.DropPickupRange = 4;
    }));

    private static Player At(byte map, short x, short y)
    {
        var character = Entities.Character(map);
        character.X = x;
        character.Y = y;
        return new Player(TestSupport.NewSession(ServerRole.Map), new AuthenticatedAccount(1, "u", Authority.None), character);
    }

    [Theory]
    [InlineData(81, 154, true)]
    [InlineData(86, 149, true)]    // 5 casas nos dois eixos: o maior eixo é o que conta
    [InlineData(86, 160, false)]   // 6 no eixo Y
    [InlineData(75, 154, false)]
    public void Shop_needs_its_owner_within_range(short x, short y, bool allowed) =>
        Assert.Equal(allowed, _guard.CanUseShop(At(40, x, y), GekomonItemShop));

    [Fact]
    public void Shop_is_refused_on_another_map_and_without_an_owner()
    {
        Assert.False(_guard.CanUseShop(At(79, 81, 154), GekomonItemShop));   // mesma posição, mapa errado
        Assert.False(_guard.CanUseShop(At(40, 81, 154), 999_999));           // nenhuma loja com esse id
    }

    [Fact]
    public void Digimon_center_needs_one_nearby_on_the_same_map()
    {
        Assert.True(_guard.CanUseDigimonCenter(At(79, 52, 58)));
        Assert.False(_guard.CanUseDigimonCenter(At(79, 56, 56)));            // 6 casas
        Assert.False(_guard.CanUseDigimonCenter(At(1, 50, 60)));             // mapa 1 não tem
    }

    [Fact]
    public void Wild_needs_to_be_in_range_and_free()
    {
        var wild = new WildDigimon(TestSupport.Data, TestSupport.Data.SpawnsOf(79)[0], 27, 65);

        Assert.True(_guard.CanEngage(At(79, (short)(wild.X + 8), wild.Y), wild));
        Assert.False(_guard.CanEngage(At(79, (short)(wild.X + 9), wild.Y), wild));

        wild.State = WildState.InBattle;
        Assert.False(_guard.CanEngage(At(79, wild.X, wild.Y), wild));
    }

    [Fact]
    public void Choice_runs_only_next_to_its_npc()
    {
        var registry = TestSupport.NewNpcRegistry(new DigitalBureauNpcs());
        var player = At(DigitalBureauNpcs.BureauMapId, 0, 0);
        player.Character.Inventory.Main.Add(VillageOfBeginningNpcs.TunomonCard, 1);

        Assert.False(registry.Choose(player, DigitalBureauNpcs.NyangIntroLetterChoice));   // longe: nada muda
        Assert.Equal(1u, player.Character.Inventory.Main.Count(VillageOfBeginningNpcs.TunomonCard));
        Assert.Empty(player.Session.DrainOutgoing());

        player.Character.AtNpc(DigitalBureauNpcs.BureauMapId, DigitalBureauNpcs.NyangNpc);
        Assert.True(registry.Choose(player, DigitalBureauNpcs.NyangIntroLetterChoice));
        Assert.Equal(0u, player.Character.Inventory.Main.Count(VillageOfBeginningNpcs.TunomonCard));
    }

    private sealed class ChoiceOfMissingNpc : NpcScriptBase
    {
        public override byte MapId => 1;
        public ChoiceOfMissingNpc() => RegisterChoice(77, 9_999, _ => true);
    }

    [Fact]
    public void Registry_refuses_a_choice_whose_npc_is_not_on_the_map()
    {
        var error = Assert.Throws<InvalidOperationException>(() => TestSupport.NewNpcRegistry(new ChoiceOfMissingNpc()));
        Assert.Contains("1:9999", error.Message);
    }

    // ---------- drops (relógio do mundo parado em zero) ----------

    private static ItemDrop DropAt(short x, short y, uint? owner, TimeSpan droppedAt = default) =>
        new(10_000, 41, x, y, droppedAt, owner);

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Pickup_needs_the_drop_within_range(short dx, bool allowed) =>
        Assert.Equal(allowed, _guard.CanPickUp(At(79, (short)(30 + dx), 30), DropAt(30, 30, owner: null)));

    [Fact]
    public void Only_the_owner_picks_up_until_the_owner_window_ends()
    {
        var other = At(79, 30, 30);            // personagem 1
        Assert.True(_guard.CanPickUp(other, DropAt(30, 30, owner: 1)));
        Assert.False(_guard.CanPickUp(other, DropAt(30, 30, owner: 2)));
        Assert.False(_guard.CanPickUp(other, DropAt(30, 30, owner: 2, droppedAt: -Zone.DropOwnerWindow + TimeSpan.FromSeconds(1))));
        Assert.True(_guard.CanPickUp(other, DropAt(30, 30, owner: 2, droppedAt: -Zone.DropOwnerWindow)));
    }

    [Fact]
    public void Drop_of_another_character_stays_on_the_ground()
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var zone = zones.Find(79)!;
        var player = At(79, 40, 40);
        player.Session.Player = player;
        zone.Enter(player);
        var drop = zone.Drop(41, 40, 40, ownerCharacterUid: 2);
        player.Session.DrainOutgoing();

        var w = new PacketWriter();
        new ItemMoveHeader(ItemPlace.Ground, ItemPlace.Inventory, drop.Y, drop.X, 4, 6).Write(w);
        w.WriteU32(drop.Uid);
        w.WriteU32(drop.ItemId);
        new ItemMoveHandler(TestSupport.Guard(world: world)).Dispatch(player.Session, new PacketReader(w.Payload.ToArray()));

        Assert.Same(drop, zone.FindDrop(drop.Uid));
        Assert.Equal(0u, player.Character.Inventory.Main.Count(41));
        Assert.Empty(player.Session.DrainOutgoing());
    }

    [Fact]
    public void Owned_drop_is_announced_again_when_the_window_ends_and_removed_at_its_lifetime()
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var zone = zones.Find(79)!;
        var player = At(79, 40, 40);
        player.Session.Player = player;
        zone.Enter(player);
        var drop = zone.Drop(41, 40, 40, ownerCharacterUid: 1);
        player.Session.DrainOutgoing();
        var second = TimeSpan.FromSeconds(1);

        List<byte[]> DropFramesAt(TimeSpan now)
        {
            zone.Update(now, second);
            return player.Session.DrainOutgoing().Where(f => PacketHeader.Read(f).Opcode == (ushort)MapOpcode.ItemDrop).ToList();
        }

        Assert.Empty(DropFramesAt(Zone.DropOwnerWindow - second));
        var announced = Assert.Single(DropFramesAt(Zone.DropOwnerWindow));
        Assert.Equal(new ItemDropPacket(drop).EncodePayload(), announced.AsSpan(PacketHeader.Length).ToArray());
        Assert.Empty(DropFramesAt(Zone.DropOwnerWindow + second));             // uma vez só

        Assert.Single(DropFramesAt(Zone.DropLifetime));                        // remoção
        Assert.Null(zone.FindDrop(drop.Uid));
    }
}
