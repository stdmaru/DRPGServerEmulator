using DRPGServer.Configuration;
using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPGServer.Game.Battle;

/// <summary>Batalhas ativas. Atualizadas pelo tick do mundo (nada de thread própria).</summary>
public sealed class BattleSystem : IWorldSystem
{
    private readonly List<Battle> _battles = [];
    private readonly GameWorld _world;
    private readonly GameData _data;
    private readonly GameOptions _options;
    private readonly ExpEventService? _expEvents;
    private readonly ILogger<BattleSystem> _log;

    public BattleSystem(GameWorld world, GameData data, IOptions<GameOptions> options, ILogger<BattleSystem> log,
        Taming? taming = null, ExpEventService? expEvents = null)
    {
        _world = world;
        _data = data;
        _options = options.Value;
        _expEvents = expEvents;
        _log = log;
        Taming = taming ?? new Taming(data, new Game.DigimonUids(), options);
        world.AddSystem(this);
    }

    internal Random Random => _world.Random;
    internal Taming Taming { get; }
    internal TimeSpan Now => _world.Now;
    public int Count => _battles.Count;

    /// <summary>
    /// 0x61: batalha contra um grupo do mapa. ✅ Grupo comum é compartilhado: segue andando no mapa (nunca vai a 11)
    /// e a batalha usa cópias novas dos membros (b1268476 enfrentado 6 vezes nas sessões 54 e 56). Grupo
    /// <see cref="SpawnArea.Exclusive"/> fica em batalha (11) e morre na vitória.
    /// </summary>
    public Battle? StartPve(Player player, Serial wildSerial)
    {
        if (player.InBattle || player.Zone is not { } zone) return null;
        if (zone.FindWild(wildSerial) is not { CanBattle: true } wild) return null;

        bool exclusive = wild.Spawn.Exclusive;
        Func<WildMember, Digimon> enemyOf = exclusive
            ? m => { m.Digimon.RestoreAll(); return m.Digimon; }
            : m => WildDigimon.CreateMember(_data, m.Option, wild.Spawn.Grade);
        var battle = new Battle(this, player, wild, enemyOf, _world.Random);
        _battles.Add(battle);
        battle.Start();
        if (exclusive)
        {
            wild.State = WildState.InBattle;
            zone.BroadcastNear(wild.X, wild.Y, new WildDigimonUpdatePacket(wild));
        }
        _log.LogDebug("{Player} começou {Battle} contra {Wild}", player, battle, wild.Leader.Name);
        return battle;
    }

    public void Update(TimeSpan now, TimeSpan dt)
    {
        foreach (var battle in _battles.ToList())
            battle.Update(dt);
        _battles.RemoveAll(b => b.IsOver);
    }

    public void PlayerLeft(Player player) => player.Battle?.Remove(player);

    internal void Finish(Battle battle, BattleResult result)
    {
        if (result != BattleResult.Cancel)
        {
            foreach (var c in battle.TeamA)
            {
                c.Digimon.TotalBattles++;
                if (result == BattleResult.Win) c.Digimon.TotalWins++;
                var unlocked = DigimonTitles.CheckUnlocks(c.Digimon);
                if (unlocked.Count > 0 && c.Owner is { } owner)
                    owner.SendDebug($"Unlocked title(s) for {c.Digimon.Name}: {string.Join(", ", unlocked)}");
            }
            foreach (var player in battle.Participants)
            {
                player.Character.TotalBattles++;
                if (result == BattleResult.Win) player.Character.TotalWins++;
                player.RequestImmediateSave();
            }
        }

        if (battle.Wild.Spawn.Exclusive && battle.Zone is { } zone)
        {
            if (result == BattleResult.Win) zone.Kill(battle.Wild);
            else
            {
                battle.Wild.Reset();
                zone.BroadcastNear(battle.Wild.X, battle.Wild.Y, new WildDigimonUpdatePacket(battle.Wild));
            }
        }
        _log.LogDebug("{Battle} terminou: {Result}", battle, result);
    }

    /// <summary>
    /// Inimigo derrubado. ✅ Cada digimon do dono que está de pé na batalha ganha a EXP inteira (um 0x6E por
    /// digimon, quem derrubou primeiro); os bits vão uma vez só, no 0x6E de quem derrubou, sorteados entre ¾ do
    /// máximo e o máximo. Cada item do grupo de drop é sorteado à parte.
    /// </summary>
    internal void GrantRewards(Player owner, Combatant killer, IReadOnlyList<Combatant> participants, WildMember defeated, int enemyCount = 1)
    {
        double eventMultiplier = _expEvents?.ExpMultiplier ?? 1.0;
        double bits = RollBits(defeated.Option.BitReward) * _options.BitRate;
        owner.Character.AddBits(bits);

        var order = participants.Where(c => c != killer).Prepend(killer).Select(c => c.Digimon).Distinct();
        bool first = true;
        foreach (var digimon in order)
        {
            long withBonus = Stats.ExpFormulas.ApplyEnemyCountBonus(defeated.Option.ExpReward, enemyCount);
            long baseExp = Stats.ExpFormulas.ApplyLevelDifference(withBonus, defeated.Digimon.Level, digimon.Level);
            long exp = (long)(baseExp * _options.ExpRate * eventMultiplier);
            Progression.GainExp(owner, digimon, exp, first ? bits : 0, _options.DigimonMaxLevel, inBattle: true, _options.AllowMultiLevelUp);
            first = false;
        }

        if (owner.Zone is { } zone)
            foreach (var drop in _data.DropGroup(defeated.Option.DropGroup))
                if (_world.Random.Next(100) < drop.Rate)
                    zone.Drop(drop.ItemId, owner.Character.X, owner.Character.Y, owner.Character.Uid);
    }

    /// <summary>✅ Bits de um selvagem: inteiro entre ⌊¾·máx⌋ e máx (Yuramon L2 do tutorial: 5, 5, 6, 7 com máximo 7).</summary>
    internal int RollBits(double max)
    {
        int hi = (int)Math.Floor(max);
        if (hi <= 0) return 0;
        int lo = (int)Math.Floor(0.75 * max);
        return _world.Random.Next(Math.Min(lo, hi), hi + 1);
    }

    public void Shutdown()
    {
        foreach (var battle in _battles.ToList())
            foreach (var player in battle.Participants.ToList())
                battle.Remove(player);
    }
}
