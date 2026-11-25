using System.Collections.Frozen;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class DigimonTitleTests
{
    private static readonly FrozenDictionary<uint, DigimonTitleInfo> SampleTitles = new Dictionary<uint, DigimonTitleInfo>
    {
        [20030] = new(20030, "Endless Journey") { AttributeBonus = new Attributes(15, 15, 15, 15) },
        [20100] = new(20100, "First Win") { AttributeBonus = new Attributes(1, 0, 0, 0) },
        [20200] = new(20200, "First Lose") { AttributeBonus = new Attributes(0, 0, 1, 0) },
        [20230] = new(20230, "King of Lose") { BonusHp = 250, BonusDefPercent = 2 },
    }.ToFrozenDictionary();

    private static GameData DataWithTitles() => new()
    {
        Species = TestSupport.Data.Species,
        Items = TestSupport.Data.Items,
        Cards = TestSupport.Data.Cards,
        DigimonTitles = SampleTitles,
        Skills = TestSupport.Data.Skills,
        Evolutions = TestSupport.Data.Evolutions,
        Maps = TestSupport.Data.Maps,
        Grids = TestSupport.Data.Grids,
        DropGroups = TestSupport.Data.DropGroups,
        TamingNets = TestSupport.Data.TamingNets,
        Portals = TestSupport.Data.Portals,
        Spawns = TestSupport.Data.Spawns,
        ZoneIds = TestSupport.Data.ZoneIds,
    };

    private static byte[] TitleListRequest(uint action, uint digimonUid)
    {
        var w = new PacketWriter();
        w.WriteU32(action);
        w.WriteU32(digimonUid);
        w.WriteZeros(1622 - 8);
        return w.Payload.ToArray();
    }

    private static byte[] TitleEquipRequest(uint action, uint digimonUid, uint titleId)
    {
        var w = new PacketWriter();
        w.WriteU32(action);
        w.WriteU32(digimonUid);
        w.WriteU32(titleId);
        w.WriteZeros(26 - 12);
        return w.Payload.ToArray();
    }

    [Fact]
    public void TitleList_and_TitleEquip_handlers_open_window_apply_stats_and_enforce_guards()
    {
        var data = DataWithTitles();
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var digimon = new Digimon(data, data.Species[50], 15) { Uid = 42, Name = "V-mon" };
        var character = new Character(digimon, new Inventory(data.FindItem, 999, _ => null, TestSupport.Clock))
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "TitleTester", TamerId = 1, MapId = 79, X = 56, Y = 56,
        };
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        zones.Find(79)!.Enter(player);
        session.DrainOutgoing();

        Assert.True(digimon.UnlockTitle(20100));
        Assert.True(digimon.UnlockTitle(20030));

        // 0x97: lista de títulos desbloqueados (1608 B)
        var listHandler = new DigimonTitleListHandler();
        listHandler.Dispatch(session, new PacketReader(TitleListRequest(2, digimon.Uid)));
        var listFrame = Assert.Single(session.DrainOutgoing());
        Assert.Equal((ushort)MapOpcode.DigimonTitleList, PacketHeader.Read(listFrame).Opcode);
        var listPayload = listFrame.AsSpan(PacketHeader.Length);
        Assert.Equal(1608, listPayload.Length);
        Assert.Equal(1u, BitConverter.ToUInt32(listPayload[0..]));
        Assert.Equal(42u, BitConverter.ToUInt32(listPayload[4..]));
        Assert.Equal(20100u, BitConverter.ToUInt32(listPayload[8..]));
        Assert.Equal(1u, BitConverter.ToUInt32(listPayload[12..]));
        Assert.Equal(20030u, BitConverter.ToUInt32(listPayload[16..]));
        Assert.Equal(1u, BitConverter.ToUInt32(listPayload[20..]));

        // Guard: Digimon de outro jogador / inexistente devolve result = 0
        listHandler.Dispatch(session, new PacketReader(TitleListRequest(2, 99999)));
        var badList = Assert.Single(session.DrainOutgoing());
        Assert.Equal(0u, BitConverter.ToUInt32(badList.AsSpan(PacketHeader.Length)));

        // Guard: tentar equipar título NÃO desbloqueado (20200) falha com result = 0
        var equipHandler = new DigimonTitleEquipHandler();
        equipHandler.Dispatch(session, new PacketReader(TitleEquipRequest(2, digimon.Uid, 20200)));
        var denied = Assert.Single(session.DrainOutgoing());
        Assert.Equal((ushort)MapOpcode.DigimonTitleEquip, PacketHeader.Read(denied).Opcode);
        Assert.Equal(0u, BitConverter.ToUInt32(denied.AsSpan(PacketHeader.Length)));
        Assert.Equal(0u, digimon.TitleId);

        // Equipar 20030 (Endless Journey: +15 todos os atributos)
        var statsBefore = digimon.Stats;
        equipHandler.Dispatch(session, new PacketReader(TitleEquipRequest(2, digimon.Uid, 20030)));
        Assert.Equal(20030u, digimon.TitleId);
        Assert.True(digimon.Stats.Atk > statsBefore.Atk);
        Assert.True(digimon.Stats.MaxHp > statsBefore.MaxHp);

        var outFrames = session.DrainOutgoing();
        Assert.Equal(
            [(ushort)MapOpcode.SingleDigimonInfo, (ushort)MapOpcode.RefreshCharacterInfo, (ushort)MapOpcode.DigimonTitleEquip],
            outFrames.Select(f => PacketHeader.Read(f).Opcode).ToList());

        // 0xB4: TitleId em +320, flag 1 em +324, bônus STR/AGI/CON/INT = (15,15,15,15) em +328..343
        var b4 = outFrames[0].AsSpan(PacketHeader.Length);
        Assert.Equal(20030u, BitConverter.ToUInt32(b4[320..]));
        Assert.Equal(1u, BitConverter.ToUInt32(b4[324..]));
        Assert.Equal(15, BitConverter.ToInt32(b4[328..]));
        Assert.Equal(15, BitConverter.ToInt32(b4[332..]));
        Assert.Equal(15, BitConverter.ToInt32(b4[336..]));
        Assert.Equal(15, BitConverter.ToInt32(b4[340..]));

        // 0x10: TitleId do líder em +100
        var p10 = outFrames[1].AsSpan(PacketHeader.Length);
        Assert.Equal(20030u, BitConverter.ToUInt32(p10[100..]));

        // 0x99: confirmação (1, 42, 20030)
        var p99 = outFrames[2].AsSpan(PacketHeader.Length);
        Assert.Equal(1u, BitConverter.ToUInt32(p99[0..]));
        Assert.Equal(42u, BitConverter.ToUInt32(p99[4..]));
        Assert.Equal(20030u, BitConverter.ToUInt32(p99[8..]));

        // Desequipar (titleId = 0) volta aos status originais
        equipHandler.Dispatch(session, new PacketReader(TitleEquipRequest(2, digimon.Uid, 0)));
        Assert.Equal(0u, digimon.TitleId);
        Assert.Equal(statsBefore, digimon.Stats);
    }

    [Fact]
    public void Automatic_title_unlocks_and_db_round_trip()
    {
        var data = DataWithTitles();
        var digimon = new Digimon(data, data.Species[50], 120) { Uid = 77, Name = "Imperial" };
        digimon.TotalBattles = 11;
        digimon.TotalWins = 10; // 10 vitórias e 1 derrota

        var unlocked = DigimonTitles.CheckUnlocks(digimon);
        Assert.Contains(DigimonTitles.FirstWin, unlocked);
        Assert.Contains(DigimonTitles.Win10, unlocked);
        Assert.Contains(DigimonTitles.FirstLose, unlocked);
        Assert.Contains(DigimonTitles.NewStart, unlocked);
        Assert.Contains(DigimonTitles.BreakThroughFixedExp, unlocked);
        Assert.Contains(DigimonTitles.LordOfDigimon, unlocked);
        Assert.Contains(DigimonTitles.EndlessJourney, unlocked);

        // Chamada repetida não duplica
        Assert.Empty(DigimonTitles.CheckUnlocks(digimon));

        Assert.True(digimon.EquipTitle(DigimonTitles.EndlessJourney));

        // Round-trip de persistência via CharacterSnapshot + CharacterFactory
        var character = new Character(digimon, new Inventory(data.FindItem, 999, _ => null, TestSupport.Clock))
        {
            Uid = 5, AccountUid = 1, Slot = 0, Name = "Saver", TamerId = 1, MapId = 79,
        };
        var snap = CharacterSnapshot.Of(character);
        Assert.Equal(DigimonTitles.EndlessJourney, snap.Digimons[0].TitleId);
        Assert.Contains(snap.Titles, t => t.DigimonUid == 77 && t.TitleId == DigimonTitles.FirstWin);
        Assert.Contains(snap.Titles, t => t.DigimonUid == 77 && t.TitleId == DigimonTitles.EndlessJourney);

        var row = new CharacterRow { Uid = 5, AccountUid = 1, Slot = 0, Name = "Saver", TamerId = 1, Level = 1, MapId = 79 };
        var dRow = new DigimonRow
        {
            Uid = 77, Slot = 0, DigimonId = 50, Name = "Imperial", Level = 120,
            TotalWins = 10, TotalBattles = 11, TitleId = snap.Digimons[0].TitleId,
        };
        var loaded = new CharacterFactory(data, TestSupport.GameOptions(), TestSupport.Clock).Build(
            new CharacterRecord(row, [dRow], [], [], [], snap.Titles))!;

        Assert.Equal(DigimonTitles.EndlessJourney, loaded.Leader.TitleId);
        Assert.True(loaded.Leader.HasTitle(DigimonTitles.FirstWin));
        Assert.True(loaded.Leader.HasTitle(DigimonTitles.FirstLose));
    }

    [Fact]
    public void CheckAndNotify_sends_0x4F_system_message_with_official_text()
    {
        var data = DataWithTitles();
        var digimon = new Digimon(data, data.Species[50], 10) { Uid = 88, Name = "V-mon" };
        digimon.TotalBattles = 1;
        digimon.TotalWins = 1;
        var character = new Character(digimon, new Inventory(data.FindItem, 999, _ => null, TestSupport.Clock))
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "NotifyTester", TamerId = 1, MapId = 79,
        };
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);

        var unlocked = DigimonTitles.CheckAndNotify(player, digimon);
        Assert.Equal([DigimonTitles.FirstWin], unlocked);

        var frame = Assert.Single(session.DrainOutgoing());
        Assert.Equal((ushort)MapOpcode.SystemMessage, PacketHeader.Read(frame).Opcode);
        var payload = frame.AsSpan(PacketHeader.Length);
        Assert.Equal(257, payload.Length);
        Assert.Equal((byte)1, payload[0]);
        string msg = System.Text.Encoding.ASCII.GetString(payload[1..]).TrimEnd('\0');
        Assert.Equal("You gained the Title of [First Win].", msg);
    }
}
