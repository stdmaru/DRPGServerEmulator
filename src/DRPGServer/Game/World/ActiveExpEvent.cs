namespace DRPGServer.Game.World;

/// <summary>Evento de bônus de EXP em vigor, lido da tabela <c>exp_event</c>.</summary>
public sealed record ActiveExpEvent(
    int Id,
    string Name,
    int BonusPercent,
    bool IsHotTime,
    DateTime StartsAt,
    DateTime EndsAt)
{
    /// <summary>✅ Limite de frames em <c>Exp_Bonus.spr</c> (81 frames: 0..80 = até +2000%) e <c>Exp_Bonus_Hot.spr</c> (29 frames: 0..28 = até +700%).</summary>
    public const int MaxNormalSpriteIndex = 80;
    public const int MaxHotTimeSpriteIndex = 28;

    /// <summary>Multiplicador de EXP aplicado em batalha (ex.: 500% -> 6.0x, 1200% -> 13.0x).</summary>
    public double ExpMultiplier => 1.0 + Math.Max(0, BonusPercent) / 100.0;

    /// <summary>✅ Índice do frame em <c>Exp_Bonus.spr</c> / <c>Exp_Bonus_Hot.spr</c> (passos de +25%, limitado ao teto do sprite).</summary>
    public byte SpriteIndex => (byte)Math.Clamp(BonusPercent / 25, 0, IsHotTime ? MaxHotTimeSpriteIndex : MaxNormalSpriteIndex);
}
