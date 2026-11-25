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
        var combatant = new Combatant(digimon, BattleSide.A, null, null);
        var drop = new ItemDrop(10_001, 41, 50, 50, TimeSpan.Zero, null);
        var item = TestSupport.EmptyInventory(999).Add(41, 1)!;
        var header = new ItemMoveHeader(ItemPlace.Ground, ItemPlace.Inventory, 64, 27, 0, 0);
        var wild = new WildDigimon(TestSupport.Data, TestSupport.Data.SpawnsOf(79)[0], 27, 65);

        var packets = new (ServerPacket Packet, int Size)[]
        {
            (new HandshakePacket("user"), 28),
            (new ChannelSelectPacket(0), 52),
            (new AuthenticationPacket(0, "user"), 448),
            (new CharacterCreatePacket(0, new CharacterCreateHeader(3, 0x4F, 0x3E, 0x38), 1, "a", 10, "b"), 64),
            (new CharacterListPacket([null, null, null, null], new Random(1)), 500),
            (new UserCheckPacket("user"), 22),
            (new CharacterDataPacket(character, TestSupport.Clock.UnixNow), 7600),                // ✅ v322 (sessão 52)
            (new InventoryDataPacket(TestSupport.EmptyInventory(999)), 4416),
            (new Unknown05Packet(), 164),
            (new HelloPacket(0x0E), 16),
            (new RefreshCharacterInfoPacket(character), 204),          // ✅ v322
            (new WildDigimonUpdatePacket(wild), 56),
            (new ItemDropPacket(drop), 112),
            (new ItemDropPacket(drop, removed: true), 112),
            (new ChatNormalPacket("[server]", "ok"), 277),             // ✅ v322 (0x21, DigimonRPG.bin @ 0x00ABE221)
            (new ChatNoticePacket("[DEBUG]", "ok"), 277),              // ✅ v322 (0x22, DigimonRPG.bin @ 0x00ABE247)
            (new SystemMessagePacket("You gained the Title of [First Win]."), 257), // ✅ v322 (0x4F, conn=92/100/137)
            (new ExitGamePacket(), 0),
            (new JoinHandshakePacket(), 504),
            (new ActionRequestPacket(BattleAction.Selected, BattleIcon.Attack, Serial.New(), Serial.New()), 36), // ✅ v322
            (new NpcChoicePacket(1), 4),
            (new StartBattlePacket(Serial.New()), 20),
            (new ActionResultsPacket([combatant], [combatant], combatant, new Dictionary<Combatant, bool> { [combatant] = true }), 1372), // ✅ v322
            (new FleeDigimonPacket(Serial.New()), 16),                 // ✅ v322 (sessão 53)
            (new FleeTamerPacket(Serial.New()), 16),
            (new BattleDigimonStatusPacket(digimon), 696),             // ✅ v322 (0x84)
            (new TamerExpPacket(character), 1252),                     // ✅ v322 (0xB1)
            (new LockActionBarPacket(), 0),
            (new BattleBeginPacket(), 0),
            (new ActionCooldownPacket(Serial.New(), 10), 24),
            (new BattleEndPacket(BattleResult.Win), 1),
            (new RewardBitExpPacket(Serial.New(), 1, 1), 32),
            (new LevelUpPacket(digimon), 696),                         // ✅ v322 (sessões 54, 56)
            (new AddAbilityPointPacket(digimon, digimon.Current, 1, 1), 704), // ✅ v322 (sessões 54, 56)
            (new SkillUpgradePacket(digimon, digimon.Current, 1, 1), 704),   // ✅ v322 (0x72, sessão 56)
            (new DigimonFormInfoPacket(0, digimon, digimon.Current), 720),   // ✅ v322 (0x82)
            (new DigimonEvolvedPacket(digimon), 696),                  // ✅ v322 (0x80)
            (new BattleEvolvePacket(digimon), 720),                    // ✅ v322 (0x81)
            (new DepositoryActionPacket(1, DepositoryAction.Open, 0, 0, 0, DepositoryResult.OpenOk), 736), // ✅ v322 (0x89)
            (new DepositoryListPacket(1, character), 808),             // ✅ v322 (0x8A)
            (new DepositoryExpandPacket(DepositoryExpandResult.Ok), 1),// ✅ v322 (0x94)
            (new DigimonTitleListPacket(7, [20100, 20200]), 1608),     // ✅ v322 (0x97, conn=76/141/162)
            (new DigimonTitleEquipPacket(7, 20100), 12),               // ✅ v322 (0x99, conn=76/141)
            (new BattleSummonPacket(1, digimon), 720),                 // ✅ v322 (0x64)
            (new EvolutionListPacket([]), 8),                          // ✅ v322 (0x9E)
            (new SetLeaderPacket(1, 7), 12),                           // ✅ v322 (0xF1)
            (new PartySwapPacket(0), 8),                               // ✅ v322 (0x173)
            (new Unknown73Packet(), 280),
            (new Unknown74Packet(), 60),
            (new Unknown7EPacket(0x29), 1940),
            (new Unknown7FPacket(0x3D), 2708),
            (new CardMovePacket(header, item), 172),                   // ✅ v322 (0x90)
            (new ItemMovePacket(header, item), 236),                   // ✅ v322 (0x91)
            (new ItemDestroyPacket(header, item), 240),                // ✅ v322 (0xBD)
            (new BattleItemReceivePacket([item]), 444),                // ✅ v322 (0xC8)
            (new InventoryRefreshPacket(character), 4424),             // ✅ v322 (0xFC)
            (new TeleportCharacterPacket(79, 1, 1), 8),
            (new RefreshDigimonStatusPacket(digimon), 696),            // ✅ v322 (sessão 56)
            (new SingleDigimonInfoPacket(digimon), 696),               // ✅ v322 (0xB4)
            (new KeepAlivePacket(0xC4), 0),
            (new ItemReceivePacket(item), 604),
            (new ExpEventPacket(null, null), 32),                      // ✅ v322 (0xE6, AES-128-ECB)
            (new RenamePacket(RenameResult.NotAvailable, RenameKind.Tamer, 0, ""), 28), // ✅ 0xF0
            (new DigimonReceivedPacket(digimon), 696),                 // ✅ v322 (0xEF, sessão 56)
            (new ItemUsePacket(item, digimon), 808),                   // ✅ v322 (0x41, sessão 56)
            (new ItemUseFailedPacket(), 0),                            // ✅ 0xC3
            (new ItemTeleportPacket(80, 162, item), 124),              // ✅ v322 (0x85, conn=125/234/469)
            (new BattleCapturePacket(false, Serial.New(), combatant, combatant, null), 744), // ✅ v322 (0xB3)
            (new Unknown121Packet(), 10304),
            (new Unknown152Packet(), 7048),
            (new UnknownE1Packet(), 1342),
            (new Unknown65Packet(), 0),
            (new EncyclopediaDataPacket(new byte[16], 0, []), 1104),
            (new EncyclopediaCollectionPacket(new byte[16], []), 64),
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
        var zones = new DRPGServer.Game.World.ZoneSystem(TestSupport.Data, world, TestSupport.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<DRPGServer.Game.World.ZoneSystem>.Instance);
        var battles = TestSupport.NewBattles(world, TestSupport.Data, TestSupport.GameOptions());
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), Entities.Character());
        zones.Find(79)!.Enter(player);
        var battle = battles.StartPve(player, player.StandBy(zones.Find(79)!.WildDigimons.First()))!;
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
        var inventory = TestSupport.EmptyInventory(999);
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

    [Fact]
    public void Character_data_writes_six_equipped_card_slots_at_offset_784()
    {
        // ✅ 0x02 v322 (conn=428/437/442/446): 6 × 76 bytes de CardSlots nos offsets 784..1239
        var character = Entities.Character();
        var card0 = character.Inventory.CardSlots.Add(74, 50)[0];
        var card1 = character.Inventory.CardSlots.Add(19, 2)[0];
        var payload = new CharacterDataPacket(character, TestSupport.Clock.UnixNow).EncodePayload();

        Assert.Equal(7600, payload.Length);
        var slot0 = payload.AsSpan(784, CardRecord.Length);
        var slot1 = payload.AsSpan(784 + CardRecord.Length, CardRecord.Length);
        Assert.Equal(card0.Uid, BitConverter.ToUInt32(slot0));
        Assert.Equal(74u, BitConverter.ToUInt32(slot0[4..]));
        Assert.Equal((ushort)50, BitConverter.ToUInt16(slot0[14..]));
        Assert.Equal(card1.Uid, BitConverter.ToUInt32(slot1));
        Assert.Equal(19u, BitConverter.ToUInt32(slot1[4..]));
        Assert.Equal((ushort)2, BitConverter.ToUInt16(slot1[14..]));
        // slots 2..5 vazios
        Assert.True(payload.AsSpan(784 + 2 * CardRecord.Length, 4 * CardRecord.Length).IndexOfAnyExcept((byte)0) < 0);
    }
}
