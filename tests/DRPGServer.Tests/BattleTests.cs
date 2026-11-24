using DRPGServer.Game;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class BattleTests
{
    private readonly GameWorld _world = TestSupport.NewWorld();
    private readonly ZoneSystem _zones;
    private readonly BattleSystem _battles;
    private readonly Session _session = TestSupport.NewSession();
    private readonly Player _player;

    public BattleTests()
    {
        _zones = new ZoneSystem(TestSupport.Data, _world, NullLogger<ZoneSystem>.Instance);
        _battles = new BattleSystem(_world, TestSupport.Data, TestSupport.GameOptions(), NullLogger<BattleSystem>.Instance);
        _player = new Player(_session, new AuthenticatedAccount(1, "u", Authority.GameMaster), Entities.Character());
        _zones.Find(79)!.Enter(_player);
        _session.DrainOutgoing();
    }

    private void Tick(int times = 1)
    {
        for (int i = 0; i < times; i++)
            _battles.Update(TimeSpan.Zero, Battle.TickInterval);
    }

    private void Advance(TimeSpan duration) => _battles.Update(TimeSpan.Zero, duration);

    /// <summary>Abre a batalha até as barras começarem a encher.</summary>
    private Battle Open()
    {
        var wild = _zones.Find(79)!.WildDigimons.First();
        var battle = _battles.StartPve(_player, wild.Serial)!;
        Assert.Equal([(ushort)0x62], _session.SentOpcodes());
        battle.SendTeams(_player);
        battle.ClientReady(_player);
        Assert.Equal([(ushort)0x63, (ushort)0x10, (ushort)0x69], _session.SentOpcodes());
        Assert.True(battle.IsRunning);
        return battle;
    }

    [Fact]
    public void Shared_wild_group_keeps_roaming_and_battle_uses_copies()
    {
        var wild = _zones.Find(79)!.WildDigimons.First();
        var battle = Open();
        Assert.Same(battle, _player.Battle);
        Assert.NotEqual(WildState.InBattle, wild.State);
        Assert.DoesNotContain(battle.TeamB, c => wild.Members.Any(m => m.Digimon == c.Digimon));
        Assert.Null(_battles.StartPve(_player, wild.Serial));   // já em batalha
    }

    [Fact]
    public void Gauges_fill_every_step_and_the_marked_attack_happens_when_full()
    {
        var battle = Open();
        var me = battle.TeamA[0];
        var enemy = battle.TeamB[0];
        // ✅ na abertura o servidor aguarda OpeningDelay antes do primeiro passo
        Tick();
        Assert.DoesNotContain((ushort)0x6B, _session.SentOpcodes());
        Assert.Equal(0, me.Gauge);
        Advance(Battle.OpeningDelay);
        Assert.Equal(1 + battle.TeamB.Count, _session.SentOpcodes().Count(o => o == 0x6B));
        Assert.Equal(BattleFormulas.GaugeGain(me.Digimon, wild: false), me.Gauge);

        // ✅ clicar alterna: marca (1), desmarca (4)
        battle.RequestAction(_player, BattleIcon.Attack, me.Digimon.Serial, enemy.Digimon.Serial);
        battle.RequestAction(_player, BattleIcon.Attack, me.Digimon.Serial, enemy.Digimon.Serial);
        var frames = _session.DrainOutgoing();
        Assert.Equal([(byte)BattleAction.Selected, (byte)BattleAction.Cleared], frames.Select(f => f[PacketHeader.Length]).ToArray());
        battle.RequestAction(_player, BattleIcon.Attack, me.Digimon.Serial, enemy.Digimon.Serial);
        _session.DrainOutgoing();

        enemy.Gauge = int.MinValue / 2;                         // o inimigo não age neste teste
        var sent = new List<ushort>();
        for (int i = 0; i < 20 && battle.IsRunning; i++)
        {
            Tick();
            sent.AddRange(_session.SentOpcodes());
        }
        Assert.False(battle.IsRunning);
        int at = sent.IndexOf(0x68);
        Assert.Equal((ushort)0x35, sent[at - 1]);               // 3: ação executada
        Assert.Equal((ushort)0x67, sent[^1]);
        Assert.Equal(0, me.Gauge);
        Assert.Equal(1, me.Actions);
        Assert.Equal(BattleIcon.None, me.Choice);

        // ✅ barras paradas até o cliente terminar a animação (0x69) + ResumeDelay
        Tick(5);
        Assert.DoesNotContain((ushort)0x6B, _session.SentOpcodes());
        battle.ClientReady(_player);
        Assert.Contains((ushort)0x69, _session.SentOpcodes());
        Tick();
        Assert.DoesNotContain((ushort)0x6B, _session.SentOpcodes());
        Tick();
        Assert.Contains((ushort)0x6B, _session.SentOpcodes());
    }

    [Fact]
    public void Full_gauge_sends_6B_once_and_does_not_spam()
    {
        var battle = Open();
        var me = battle.TeamA[0];
        foreach (var e in battle.TeamB) e.Gauge = int.MinValue / 2;
        me.Gauge = BattleFormulas.MaxGauge - 1;
        Advance(Battle.OpeningDelay + Battle.TickInterval);
        _session.DrainOutgoing();

        // já está em 500 e sem ação marcada: não deve reenviar 0x6B do jogador (apenas os inimigos)
        Tick();
        Assert.Equal(battle.TeamB.Count, _session.SentOpcodes().Count(o => o == 0x6B));
    }

    [Fact]
    public void Wild_attacks_on_its_own_when_full()
    {
        var battle = Open();
        battle.TeamB[0].Gauge = BattleFormulas.MaxGauge;
        Advance(Battle.OpeningDelay + Battle.TickInterval);
        var sent = _session.SentOpcodes();
        Assert.Equal([(ushort)0x68, (ushort)0x67], sent.Where(o => o is 0x68 or 0x67 or 0x35).ToList());
        Assert.False(battle.IsRunning);
    }

    [Fact]
    public void Win_sends_end_sequence_and_recovery_restarts_right_after()
    {
        var battle = Open();
        _player.Leader.TakeDamage(10);
        _zones.Find(79)!.Update(TimeSpan.FromHours(1), TimeSpan.FromMilliseconds(50));
        Assert.DoesNotContain((ushort)0xB2, _session.SentOpcodes());   // ✅ sem recuperação durante a batalha

        battle.KnockOut(BattleSide.B);
        var sent = _session.SentOpcodes();
        Assert.Equal([(ushort)0xB1, (ushort)0x10, (ushort)0x84, (ushort)0x6D], sent.Where(o => o is 0xB1 or 0x10 or 0x84 or 0x6D).ToList());
        Assert.Contains((ushort)0x6E, sent);
        Assert.True(battle.IsOver);
        Assert.Null(_player.Battle);
        Assert.Equal(1, _player.Character.TotalWins);
        Tick();
        Assert.Equal(0, _battles.Count);

        // ✅ o oficial cura ~0,2 s depois do 0x6D, ainda na tela de resultado
        Assert.InRange(_player.NextRecoveryAt - _world.Now, TimeSpan.Zero, Zone.RecoveryAfterBattle);
    }

    [Fact]
    public void Flee_happens_when_gauge_fills()
    {
        var battle = Open();
        var me = battle.TeamA[0];
        battle.RequestAction(_player, BattleIcon.Flee, me.Digimon.Serial, Serial.Empty);
        foreach (var c in battle.TeamB) c.Gauge = int.MinValue / 2;
        me.Gauge = BattleFormulas.MaxGauge;
        _session.DrainOutgoing();
        Advance(Battle.OpeningDelay + Battle.TickInterval);
        var sent = _session.SentOpcodes();
        Assert.Equal([(ushort)0x35, (ushort)0x65, (ushort)0x6A], sent.Where(o => o is 0x35 or 0x65 or 0x6A).ToList());
        Assert.Equal((ushort)0x6D, sent[^1]);
        Assert.True(battle.IsOver);
        Assert.Equal(0, _player.Character.TotalWins);
        Assert.Equal(1, _player.Character.TotalBattles);
    }

    [Fact]
    public void Disconnect_cancels_battle_silently()
    {
        var battle = Open();
        _battles.PlayerLeft(_player);
        Tick();
        Assert.True(battle.IsOver);
        Assert.DoesNotContain((ushort)0x6D, _session.SentOpcodes());
        Assert.Equal(0, _battles.Count);
        Assert.Equal(0, _player.Character.TotalBattles);
    }

    [Fact]
    public void Cannot_command_a_digimon_you_do_not_own()
    {
        var battle = Open();
        var enemy = battle.TeamB[0].Digimon.Serial;
        battle.RequestAction(_player, BattleIcon.Attack, enemy, battle.TeamA[0].Digimon.Serial);
        battle.RequestAction(_player, BattleIcon.Attack, battle.TeamA[0].Digimon.Serial, battle.TeamA[0].Digimon.Serial);
        Assert.DoesNotContain((ushort)0x35, _session.SentOpcodes());
    }

    [Fact]
    public void Knockout_defeat_loses_one_percent_exp_and_keeps_zero_hp_in_0x84()
    {
        var battle = Open();
        var me = battle.TeamA[0];
        // Coloca o digimon no nível 15 (ExpToNext = 4622 -> 1% = 46) com 1000 EXP e 1 HP
        while (me.Digimon.Level < 15) me.Digimon.LevelUp();
        me.Digimon.Load(1000, me.Digimon.UnlockedStage, 0, 0);
        me.Digimon.TakeDamage(me.Digimon.Hp - 1);
        var enemy = battle.TeamB[0];
        enemy.Gauge = BattleFormulas.MaxGauge;

        // Garante golpe fatal (pode errar em 10%, então tenta até derrubar)
        List<byte[]> frames = [];
        for (int attempt = 0; attempt < 20 && !battle.IsOver; attempt++)
        {
            enemy.Gauge = BattleFormulas.MaxGauge;
            Advance(Battle.OpeningDelay + Battle.TickInterval);
            frames.AddRange(_session.DrainOutgoing());
            if (!battle.IsOver && !battle.IsRunning)
                battle.ClientReady(_player);
        }

        Assert.True(battle.IsOver);
        // ✅ 0x84 sai com HP = 0, mas o líder revive com 10 HP no mapa (conn=408 S2C 0xB4) para regenerar via 0xB2
        Assert.Equal(10, me.Digimon.Hp);
        Assert.Equal(1000 - 46, me.Digimon.Exp);

        var opcodes = frames.Select(f => PacketHeader.Read(f).Opcode).ToList();
        int expIdx = opcodes.LastIndexOf((ushort)MapOpcode.BattleRewardBitExp);
        int resIdx = opcodes.LastIndexOf((ushort)MapOpcode.BattleActionResults);
        Assert.True(expIdx >= 0 && expIdx < resIdx);
        var expPayload = frames[expIdx].AsSpan(PacketHeader.Length);
        Assert.Equal(-46L, BitConverter.ToInt64(expPayload.Slice(16, 8)));
        Assert.Equal(0.0, BitConverter.ToDouble(expPayload.Slice(24, 8)));

        // ✅ derrota por nocaute envia 0xB1, 0x10, 0x84 (com HP = 0) e 0x6D (2), sem teleporte 0xB0
        Assert.Equal([(ushort)0xB1, (ushort)0x10, (ushort)0x84, (ushort)0x6D],
            opcodes.Where(o => o is 0xB1 or 0x10 or 0x84 or 0x6D).ToList());
        Assert.DoesNotContain((ushort)0xB0, opcodes);
        var statusFrame = frames.Last(f => PacketHeader.Read(f).Opcode == (ushort)MapOpcode.BattleDigimonStatus);
        Assert.Equal(0, BitConverter.ToInt32(statusFrame.AsSpan(PacketHeader.Length + 192, 4)));
        var endFrame = frames.Last(f => PacketHeader.Read(f).Opcode == (ushort)MapOpcode.BattleEnd);
        Assert.Equal((byte)BattleResult.Lose, endFrame[PacketHeader.Length]);
    }
}

public class BattleFormulaTests
{
    [Theory]
    // ✅ v322, ataque básico entre in-training (sessões 53–54): ATK, DEF, dano
    [InlineData(145, 63, 33)]
    [InlineData(145, 43, 48)]
    [InlineData(218, 63, 75)]
    [InlineData(261, 63, 108)]
    [InlineData(283, 69, 116)]
    [InlineData(80, 94, 6)]
    public void Basic_attack_damage_matches_captures(int atk, int def, int damage) =>
        Assert.Equal(damage, (int)BattleFormulas.BaseDamage(atk, def));

    [Fact]
    public void Gauge_gain_matches_captures()
    {
        // ✅ v322: fórmula exata do cliente por (Level, AGI) — 0 erros de L1 a L160; selvagem pelo BR
        Assert.Equal(42, BattleFormulas.PlayerGaugeGain(1, 10));
        Assert.Equal(76, BattleFormulas.PlayerGaugeGain(2, 12));
        Assert.Equal(88, BattleFormulas.PlayerGaugeGain(3, 14));
        Assert.Equal(94, BattleFormulas.PlayerGaugeGain(4, 16));
        Assert.Equal(108, BattleFormulas.PlayerGaugeGain(10, 32));
        Assert.Equal(116, BattleFormulas.PlayerGaugeGain(11, 60));
        Assert.Equal(123, BattleFormulas.PlayerGaugeGain(20, 76));
        Assert.Equal(131, BattleFormulas.PlayerGaugeGain(160, 95));
        Assert.Equal(8, BattleFormulas.WildGaugeGain(85));
        Assert.Equal(14, BattleFormulas.WildGaugeGain(165));
        Assert.Equal(33, BattleFormulas.WildGaugeGain(1040));
    }
}
