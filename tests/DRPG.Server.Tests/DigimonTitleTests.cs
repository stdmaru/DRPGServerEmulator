using System.Collections.Frozen;
using DRPG.Server.Data.GameData;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game;
using DRPG.Server.Game.Digimons;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.Stats;
using DRPG.Server.Game.World;
using DRPG.Server.Network;
using DRPG.Server.Network.Handlers;
using DRPG.Server.Network.Packets.Map;
using DRPG.Shared.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPG.Server.Tests;

public class DigimonTitleTests
{
    // Ids de DigimonTitles.xml usados nos testes
    private const uint NewStart = 20000, BreakThroughFixedExp = 20010, LordOfDigimon = 20020, EndlessJourney = 20030;
    private const uint FirstWin = 20100, Win10 = 20110, FirstLose = 20200;

    private static readonly FrozenDictionary<uint, DigimonTitleInfo> RealTitles =
        GameDataLoader.LoadDigimonTitles(Path.Combine(AppContext.BaseDirectory, "Resources", "Digimon", "DigimonTitles.xml"));

    private static GameData DataWithTitles() => new()
    {
        Species = TestSupport.Data.Species,
        Items = TestSupport.Data.Items,
        Cards = TestSupport.Data.Cards,
        DigimonTitles = RealTitles,
        Skills = TestSupport.Data.Skills,
        Evolutions = TestSupport.Data.Evolutions,
        Maps = TestSupport.Data.Maps,
        Grids = TestSupport.Data.Grids,
        DropGroups = TestSupport.Data.DropGroups,
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
        Assert.Contains(FirstWin, unlocked);
        Assert.Contains(Win10, unlocked);
        Assert.Contains(FirstLose, unlocked);
        Assert.Contains(NewStart, unlocked);
        Assert.Contains(BreakThroughFixedExp, unlocked);
        Assert.Contains(LordOfDigimon, unlocked);
        Assert.Contains(EndlessJourney, unlocked);

        // Chamada repetida não duplica
        Assert.Empty(DigimonTitles.CheckUnlocks(digimon));

        Assert.True(digimon.EquipTitle(EndlessJourney));

        // Round-trip de persistência via CharacterSnapshot + CharacterFactory
        var character = new Character(digimon, new Inventory(data.FindItem, 999, _ => null, TestSupport.Clock))
        {
            Uid = 5, AccountUid = 1, Slot = 0, Name = "Saver", TamerId = 1, MapId = 79,
        };
        var snap = CharacterSnapshot.Of(character);
        Assert.Equal(EndlessJourney, snap.Digimons[0].TitleId);
        Assert.Contains(snap.Titles, t => t.DigimonUid == 77 && t.TitleId == FirstWin);
        Assert.Contains(snap.Titles, t => t.DigimonUid == 77 && t.TitleId == EndlessJourney);

        var row = new CharacterRow { Uid = 5, AccountUid = 1, Slot = 0, Name = "Saver", TamerId = 1, Level = 1, MapId = 79 };
        var dRow = new DigimonRow
        {
            Uid = 77, Slot = 0, DigimonId = 50, Name = "Imperial", Level = 120,
            TotalWins = 10, TotalBattles = 11, TitleId = snap.Digimons[0].TitleId,
        };
        var loaded = new CharacterFactory(data, TestSupport.GameOptions(), TestSupport.Clock).Build(
            new CharacterRecord(row, [dRow], [], [], [], snap.Titles))!;

        Assert.Equal(EndlessJourney, loaded.Leader.TitleId);
        Assert.True(loaded.Leader.HasTitle(FirstWin));
        Assert.True(loaded.Leader.HasTitle(FirstLose));
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
        Assert.Equal([FirstWin], unlocked);

        var frame = Assert.Single(session.DrainOutgoing());
        Assert.Equal((ushort)MapOpcode.SystemMessage, PacketHeader.Read(frame).Opcode);
        var payload = frame.AsSpan(PacketHeader.Length);
        Assert.Equal(257, payload.Length);
        Assert.Equal((byte)1, payload[0]);
        string msg = System.Text.Encoding.ASCII.GetString(payload[1..]).TrimEnd('\0');
        Assert.Equal("You gained the Title of [First Win].", msg);
    }

    [Fact]
    public void DigimonTitles_xml_has_names_bonuses_and_unlock_rules()
    {
        Assert.True(RealTitles.Count > 200);
        Assert.Equal("King of Lose", RealTitles[20230].Name);
        Assert.Equal((250, 2), (RealTitles[20230].Modifiers.Flat(StatModifierKind.Hp), RealTitles[20230].Modifiers.Percent(StatModifierKind.Def)));
        Assert.Equal([new LossesUnlock(10000)], RealTitles[20230].Unlocks);
        Assert.Equal([new StageUnlock(StatFormulas.Mega)], RealTitles[NewStart].Unlocks);
        Assert.Equal([new LevelUnlock(160)], RealTitles[30444].Unlocks);
        Assert.Empty(RealTitles[10000].Unlocks); // Scent of Roses: só por evento
    }

    [Fact]
    public void Unlock_needs_every_condition_and_ignores_titles_without_rules()
    {
        var title = new DigimonTitleInfo(1, "Teste") { Unlocks = [new WinsUnlock(5), new LevelUnlock(20)] };
        var data = new GameData
        {
            Species = TestSupport.Data.Species, Items = TestSupport.Data.Items, Maps = TestSupport.Data.Maps,
            Grids = TestSupport.Data.Grids, DropGroups = TestSupport.Data.DropGroups, Portals = TestSupport.Data.Portals,
            Spawns = TestSupport.Data.Spawns, ZoneIds = TestSupport.Data.ZoneIds,
            DigimonTitles = new Dictionary<uint, DigimonTitleInfo> { [1] = title, [2] = new(2, "Evento") }.ToFrozenDictionary(),
        };
        var digimon = new Digimon(data, data.Species[50], 10) { Uid = 1, Name = "V-mon", TotalWins = 5, TotalBattles = 5 };

        Assert.Empty(DigimonTitles.CheckUnlocks(digimon)); // nível 10 < 20

        var veteran = new Digimon(data, data.Species[50], 20) { Uid = 2, Name = "V-mon", TotalWins = 5, TotalBattles = 5 };
        Assert.Equal([1u], DigimonTitles.CheckUnlocks(veteran));
    }

    [Theory]
    [InlineData("""<Title ID="1" Name="x"><Unlock Condition="Kills" Value="1" /></Title>""")]   // condição desconhecida
    [InlineData("""<Title ID="1" Name="x"><Unlock Condition="0" Value="1" /></Title>""")]       // número no lugar do nome
    [InlineData("""<Title ID="1" Name="x"><Unlock Condition="Wins" Value="-1" /></Title>""")]   // valor inválido
    [InlineData("""<Title ID="1" Name="x"><Unlock Condition="Stage" Value="5" /></Title>""")]   // estágio sem nome
    [InlineData("""<Title ID="1" Name="x"><Unlock Condition="Wins" /></Title>""")]              // sem valor
    [InlineData("""<Title ID="1" Name="x"><Unlock Condition="Wins" Value="1" Max="2" /></Title>""")] // atributo a mais
    [InlineData("""<Title ID="1" Name="x" Str="1" />""")]                                        // bônus no formato antigo
    [InlineData("""<Title ID="1" Name="x"><Bonus /></Title>""")]                                 // elemento estranho
    [InlineData("""<Title ID="1" Name="x" /><Title ID="1" Name="y" />""")]                       // id duplicado
    [InlineData("""<Title ID="0" Name="x" />""")]                                                // id reservado
    public void DigimonTitles_xml_rejects_malformed_titles(string titles)
    {
        var path = Path.Combine(Path.GetTempPath(), $"titles-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, $"<DigimonTitles>{titles}</DigimonTitles>");
        try
        {
            Assert.Throws<GameDataException>(() => GameDataLoader.LoadDigimonTitles(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
