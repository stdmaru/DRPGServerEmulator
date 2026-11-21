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
        var wild = new WildDigimon(TestSupport.Data, TestSupport.Data.SpawnsOf(79)[0], 27, 65);

        var packets = new (ServerPacket Packet, int Size)[]
        {
            (new HandshakePacket("user"), 28),
            (new ChannelSelectPacket(0), 52),
            (new AuthenticationPacket(0, "user"), 448),
            (new CharacterCreatePacket(0, new CharacterCreateHeader(3, 0x4F, 0x3E, 0x38), 1, "a", 10, "b"), 64),
            (new CharacterListPacket([null, null, null, null], new Random(1)), 500),
            (new UserCheckPacket("user"), 22),
            (new CharacterDataPacket(character), 7600),                // ✅ v322 (sessão 52)
            (new InventoryDataPacket(new Inventory(999)), 4416),
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
            (new LevelUpPacket(digimon), 696),                         // ✅ v322 (sessões 54, 56)
            (new AddAbilityPointPacket(digimon, 1, 1), 704),           // ✅ v322 (sessões 54, 56)
            (new Unknown73Packet(), 280),
            (new Unknown74Packet(), 60),
            (new Unknown7EPacket(0x29), 1940),
            (new Unknown7FPacket(0x3D), 2708),
            (new ItemDropGetPacket(drop, item), 236),
            (new TeleportCharacterPacket(79, 1, 1), 8),
            (new RefreshDigimonStatusPacket(digimon), 696),            // ✅ v322 (sessão 56)
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
        Assert.Equal(6960, new BattleCreatePacket   /* ✅ v322: 10 blocos de 696 (sessão 56) */(battle).EncodePayload().Length);
    }

    [Fact]
    public void Character_create_echoes_request_header()
    {
        // ✅ sessão 53: resultado 2/0 e os 6 bytes do pedido (slot 3, mapa 0x4F, 0x3E, 0x38)
        var payload = new CharacterCreatePacket(0, new CharacterCreateHeader(3, 0x4F, 0x3E, 0x38), 1, "a", 31, "b").EncodePayload();
        Assert.Equal(Convert.FromHexString("0200000000000000034F3E003800"), payload[..14]);
    }

    [Fact]
    public void Character_list_check_is_offset_plus_map_plus_level()
    {
        // ✅ 18 listas v322 (sessões 52–56): check − deslocamento = mapa + nível, e slot vazio leva só o índice
        var summary = new CharacterSummary(7, 0, "a", 1, 14, 6, 57, 91, 5, 6, 619, 12, "b");
        var payload = new CharacterListPacket([summary, null, null, null], new Random(1)).EncodePayload();
        for (int slot = 0; slot < 4; slot++)
        {
            var b = payload.AsSpan(4 + 124 * slot, 124);
            Assert.Equal(slot, b[112]);
            int check = BitConverter.ToInt32(b[116..]), offset = BitConverter.ToInt32(b[120..]);
            Assert.Equal(slot == 0 ? 20 : 0, check - offset);
        }
    }

    [Fact]
    public void Inventory_items_go_to_their_slot_with_amount_and_check()
    {
        // ✅ sessões 52–56: 24 × 76 bytes e depois 24 registros de 108; checagem = uid + item
        var inventory = new Inventory(999);
        inventory.Add(41, 3);
        var payload = new InventoryDataPacket(inventory).EncodePayload();
        var record = payload.AsSpan(24 * 76, 108);
        Assert.Equal(1u, BitConverter.ToUInt32(record));
        Assert.Equal(41u, BitConverter.ToUInt32(record[4..]));
        Assert.Equal(3u, BitConverter.ToUInt32(record[12..]));
        Assert.Equal(42u, BitConverter.ToUInt32(record[100..]));
        Assert.True(payload.AsSpan(0, 24 * 76).IndexOfAnyExcept((byte)0) < 0);
    }

    [Fact]
    public void Refresh_character_info_has_tamer_level_position_twice_and_leader_level()
    {
        // ✅ 0x10 v322 (sessões 52–56): nível do tamer em 40, X/Y em 44 e de novo em 48, nível do líder em 96
        var character = Entities.Character();
        character.Level = 14;
        character.X = 57;
        character.Y = 91;
        var payload = new RefreshCharacterInfoPacket(character).EncodePayload();
        Assert.Equal(14, BitConverter.ToUInt16(payload, 40));
        Assert.Equal(new short[] { 57, 91, 57, 91 }, Enumerable.Range(0, 4).Select(k => BitConverter.ToInt16(payload, 44 + 2 * k)));
        Assert.Equal(character.Leader.SpeciesId, BitConverter.ToUInt16(payload, 72));
        Assert.Equal(character.Leader.Level, BitConverter.ToInt32(payload, 96));
    }
}
