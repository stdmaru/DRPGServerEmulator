using DRPGServer.Data.GameData;
using DRPGServer.Game.Digimons;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.Items;

/// <summary>Por qual pacote o item é usado. Cada entrada tem a sua sequência de resposta (<see cref="ItemUseService"/>).</summary>
public enum ItemUseEntry : byte
{
    /// <summary><c>0x41</c>: usar item, com um selvagem como alvo.</summary>
    OnTarget,
    /// <summary><c>0x85</c>: usar item numa posição do mapa.</summary>
    AtPosition,
}

/// <summary>O que um efeito de uso enxerga. Montado pelo <see cref="ItemUseService"/>; o item ainda não foi consumido em <see cref="IItemUseEffect.CanApply"/>.</summary>
public sealed record ItemUseContext(Player Player, InventoryItem Item, WildDigimon? Wild, int X, int Y, Taming Taming, InteractionGuard Guard, Random Random, TimeSpan Now);

/// <summary>
/// Efeito de uso de item (<c>Resources/Item/ItemEffects.xml</c>), registrado pelo tipo em <see cref="ItemUseEffectTypes"/>.
/// O serviço chama <see cref="CanApply"/> de todos os efeitos antes de consumir e aplicar qualquer um.
/// </summary>
public interface IItemUseEffect
{
    ItemUseEntry Entry { get; }

    /// <summary>Pode aplicar agora? Sem efeito colateral.</summary>
    bool CanApply(ItemUseContext ctx);

    /// <summary>Aplica e manda os pacotes do efeito. False = falhou depois de validado (ex.: a rede não pegou).</summary>
    bool Apply(ItemUseContext ctx);
}

/// <summary>Efeito novo = uma classe e uma linha aqui.</summary>
public static class ItemUseEffectTypes
{
    private static readonly Dictionary<string, Func<EffectParams, IItemUseEffect>> Factories = new()
    {
        ["TeleportInMap"] = _ => new TeleportInMapEffect(),
        ["TamingNet"] = p => new TamingNetEffect(Stage(p), Chance(p)),
    };

    public static IItemUseEffect Create(EffectParams p)
    {
        if (!Factories.TryGetValue(p.Type, out var factory))
            throw p.Fail("desconhecido");
        var effect = factory(p);
        p.EnsureAllRead();
        return effect;
    }

    private static byte Stage(EffectParams p) =>
        StatFormulas.StageFromName(p.Name("Stage")) ?? throw p.Fail("Stage desconhecido (InTraining, Rookie, Champion, Ultimate, Mega)");

    private static int Chance(EffectParams p)
    {
        int chance = p.PositiveInt("Chance");
        if (chance > 100) throw p.Fail($"Chance={chance} acima de 100");
        return chance;
    }
}

/// <summary>
/// ✅ Teleporte no mesmo mapa (Teleport 900 / Teleport X 907, <c>0x85</c>, conn=125/234/469): leva o tamer à célula
/// andável pedida e avisa a zona (<c>0x10</c>).
/// </summary>
public sealed record TeleportInMapEffect : IItemUseEffect
{
    public ItemUseEntry Entry => ItemUseEntry.AtPosition;

    public bool CanApply(ItemUseContext ctx)
    {
        if (ctx.Player.Zone is not { } zone) return false;
        if (ctx.X < short.MinValue || ctx.X > short.MaxValue || ctx.Y < short.MinValue || ctx.Y > short.MaxValue) return false;
        return zone.Grid is not { } grid || grid.IsWalkable(ctx.X, ctx.Y);
    }

    public bool Apply(ItemUseContext ctx)
    {
        var player = ctx.Player;
        player.Character.X = (short)ctx.X;
        player.Character.Y = (short)ctx.Y;
        player.LastMoveAt = ctx.Now;
        player.Zone!.Broadcast(new RefreshCharacterInfoPacket(player.Character));
        return true;
    }
}

/// <summary>
/// Taming Net no selvagem (<c>0x41</c>, docs/mecanicas/captura.md): só fora de batalha, no alcance, no estágio da rede
/// e com posição livre no time. ✅ Sucesso: <c>0xEF</c> (bloco do novo) e <c>0x41</c> (rede consumida + bloco do novo).
/// </summary>
public sealed record TamingNetEffect(byte Stage, int Chance) : IItemUseEffect
{
    public ItemUseEntry Entry => ItemUseEntry.OnTarget;

    public bool CanApply(ItemUseContext ctx) =>
        !ctx.Player.InBattle && ctx.Wild is { } wild && ctx.Guard.CanEngage(ctx.Player, wild)
        && wild.Leader.Species.Stage == Stage && Taming.HasFreeSlot(ctx.Player.Character);

    public bool Apply(ItemUseContext ctx)
    {
        // ❓ falha de rede com chance < 100 não capturada
        if (ctx.Random.Next(100) >= Chance || ctx.Taming.Tame(ctx.Player.Character, ctx.Wild!.Leader.Species) is not { } digimon)
            return false;
        ctx.Player.Send(new DigimonReceivedPacket(digimon));
        ctx.Player.Send(new ItemUsePacket(ctx.Item, digimon));
        return true;
    }
}
