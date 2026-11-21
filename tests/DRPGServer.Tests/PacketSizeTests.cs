using DRPGServer.Game;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Channel;
using DRPGServer.Network.Packets.Global;
using DRPGServer.Network.Packets.Login;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Tests;

/// <summary>
/// Tamanho do payload de cada pacote contra o tamanho capturado do servidor oficial (v242,
/// captures.db: tamanho total − 14). Pega erro de layout sem precisar do cliente.
/// </summary>
public class PacketSizeTests
{
    [Fact]
    public void Payload_sizes_match_captures()
    {
        var character = Entities.Character();
        var digimon = character.Leader;
        var drop = new ItemDrop(10_001, 41, 50, 50, TimeSpan.Zero, null);
        var item = new Inventory(999).Add(41, 1)!;
        var wild = new WildDigimon(TestSupport.Data, TestSupport.Data.SpawnsOf(79)[0], new Random(1));

        var packets = new (ServerPacket Packet, int Size)[]
        {
            (new HandshakePacket("user"), 28),
            (new ChannelSelectPacket(0), 52),
            (new AuthenticationPacket(0, "user"), 448),
            (new CharacterCreatePacket(0, 1, "a", 10, "b"), 64),
            (new CharacterListPacket([null, null, null, null], new Random(1)), 500),
            (new UserCheckPacket("user"), 22),
            (new CharacterDataPacket(character), 7600),                // ✅ v322 (sessão 52)
            (new InventoryDataPacket(), 4416),
            (new Unknown05Packet(), 164),
            (new HelloPacket(0x0E), 16),
            (new RefreshCharacterInfoPacket(character), 204),          // ✅ v322
            (new WildDigimonUpdatePacket(wild), 56),
            (new ItemDropPacket(drop), 112),
            (new ItemDropPacket(drop, removed: true), 112),
            (new ExitGamePacket(), 0),
            (new JoinHandshakePacket(), 504),
            (new ActionRequestPacket(1, Serial.New(), Serial.New()), 36),
            (new NpcChoicePacket(1), 4),
            (new StartBattlePacket(Serial.New()), 20),
            (new ActionResultsPacket([digimon], [digimon], digimon, digimon, false), 1372),
            (new LockActionBarPacket(), 0),
            (new BattleBeginPacket(), 0),
            (new ActionCooldownPacket(Serial.New(), 10), 24),
            (new BattleEndPacket(BattleResult.Win), 1),
            (new RewardBitExpPacket(Serial.New(), 1, 1), 32),
            (new LevelUpPacket(digimon), 696),                         // ❓ v322 deduzido do bloco de 696
            (new AddAbilityPointPacket(digimon, 1, 1), 704),           // ❓ v322 deduzido
            (new Unknown73Packet(), 280),
            (new Unknown74Packet(), 60),
            (new Unknown7EPacket(0x29), 1940),
            (new Unknown7FPacket(0x3D), 2708),
            (new ItemDropGetPacket(drop, item), 236),
            (new TeleportCharacterPacket(79, 1, 1), 8),
            (new RefreshDigimonStatusPacket(digimon), 696),            // ❓ v322 deduzido
            (new SingleDigimonInfoPacket(digimon), 696),               // ✅ v322 (0xB4)
            (new KeepAlivePacket(0xC4), 0),
            (new ItemReceivePacket(item), 604),
            (new UnknownE6Packet(), 32),
            (new UnknownF0Packet(), 28),
            (new Unknown121Packet(), 10304),
            (new Unknown152Packet(), 7048),
            (new UnknownE1Packet(), 1342),
            (new Unknown65Packet(), 0),
            (new EncyclopediaDataPacket(new byte[16], new byte[16]), 1104),
            (new EncyclopediaCollectionPacket(), 64),
        };
        var wrong = packets
            .Select(p => (Name: p.Packet.ToString(), p.Size, Actual: p.Packet.EncodePayload().Length))
            .Where(p => p.Actual != p.Size)
            .Select(p => $"{p.Name}: esperado {p.Size}, gerado {p.Actual}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void Battle_create_has_ten_status_blocks()
    {
        var world = TestSupport.NewWorld();
        var zones = new DRPGServer.Game.World.ZoneSystem(TestSupport.Data, world, Microsoft.Extensions.Logging.Abstractions.NullLogger<DRPGServer.Game.World.ZoneSystem>.Instance);
        var battles = new BattleSystem(world, TestSupport.Data, TestSupport.GameOptions(), Microsoft.Extensions.Logging.Abstractions.NullLogger<BattleSystem>.Instance);
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), Entities.Character());
        zones.Find(79)!.Enter(player);
        var battle = battles.StartPve(player, zones.Find(79)!.WildDigimons.First().Serial)!;
        Assert.Equal(6960, new BattleCreatePacket   /* ❓ v322: 10 blocos de 696 */(battle).EncodePayload().Length);
    }
}
