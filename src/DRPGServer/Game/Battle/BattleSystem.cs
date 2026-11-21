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
    private readonly ILogger<BattleSystem> _log;

    public BattleSystem(GameWorld world, GameData data, IOptions<GameOptions> options, ILogger<BattleSystem> log)
    {
        _world = world;
        _data = data;
        _options = options.Value;
        _log = log;
        world.AddSystem(this);
    }

    internal Random Random => _world.Random;
    public int Count => _battles.Count;

    public Battle? StartPve(Player player, Serial wildSerial)
    {
        if (player.InBattle || player.Zone is not { } zone) return null;
        if (zone.FindWild(wildSerial) is not { State: WildState.Roaming } wild) return null;

        var battle = new Battle(this, player, wild, _world.Random);
        _battles.Add(battle);
        battle.Start();
        zone.Broadcast(new WildDigimonUpdatePacket(wild));
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
        foreach (var c in battle.TeamA)
        {
            c.Digimon.TotalBattles++;
            if (result == BattleResult.Win) c.Digimon.TotalWins++;
        }
        foreach (var player in battle.Participants)
        {
            player.Character.TotalBattles++;
            if (result == BattleResult.Win) player.Character.TotalWins++;
        }

        var zone = battle.Participants.FirstOrDefault()?.Zone;
        if (result == BattleResult.Win && zone is not null)
            zone.Kill(battle.Wild);
        else
        {
            battle.Wild.Reset();
            zone?.Broadcast(new WildDigimonUpdatePacket(battle.Wild));
        }
        _log.LogDebug("{Battle} terminou: {Result}", battle, result);
    }

    internal void GrantRewards(Player owner, Digimon digimon, WildMember defeated)
    {
        long exp = (long)(defeated.Option.ExpReward * _options.ExpRate);
        double bits = defeated.Option.BitReward * _options.BitRate;

        int levels = digimon.GainExp(exp, _options.DigimonMaxLevel);
        owner.Character.AddBits(bits);
        owner.Send(new RewardBitExpPacket(digimon.Serial, exp, bits));
        if (levels > 0)
            owner.Send(new LevelUpPacket(digimon));

        var drops = _data.DropGroup(defeated.Option.DropGroup);
        if (drops.Count > 0 && owner.Zone is { } zone)
        {
            int roll = _world.Random.Next(101);
            foreach (var drop in drops.Where(d => roll <= d.Rate))
                zone.Drop(drop.ItemId, owner.Character.X, owner.Character.Y, owner.Character.Uid);
        }
    }

    public void Shutdown()
    {
        foreach (var battle in _battles.ToList())
            foreach (var player in battle.Participants.ToList())
                battle.Remove(player);
    }
}
