using DRPGServer.Game.Entities;

namespace DRPGServer.Game.Battle;

/// <summary>Resultado de um golpe.</summary>
public readonly record struct Hit(bool Landed, int Damage);

/// <summary>
/// Fórmulas de batalha ajustadas nas 67 batalhas v322 (sessões 53–58). Detalhes e resíduos em
/// docs/mecanicas/batalha.md.
/// </summary>
public static class BattleFormulas
{
    /// <summary>✅ Barra de ação cheia (500 em todas as amostras, igual ao MaxActionGauge do bloco de status).</summary>
    public const int MaxGauge = 500;

    /// <summary>❓ Chance de acerto: 313 golpes do jogador com 32 erros (10 %); dos selvagens, 22/25 sem o Otamamon L50.</summary>
    public const double HitChance = 0.90;

    /// <summary>
    /// ✅ Ataque básico (skill 0 no fim do 0x67): ⌊ATK² / (10·DEF)⌋, ATK de quem ataca e DEF de quem apanha.
    /// Exato nos pares sem atributo (in-training contra in-training). ❓ Entre atributos o dano sai ×0,90…×1,05
    /// do base, fixo por par; ainda sem regra, então não entra aqui.
    /// </summary>
    public static Hit Attack(Digimon attacker, Digimon target, Random random)
    {
        if (random.NextDouble() >= HitChance) return new Hit(false, 0);
        return new Hit(true, Math.Max(1, (int)BaseDamage(attacker.Stats.Atk, target.Stats.Def)));
    }

    public static double BaseDamage(int atk, int def) => (double)atk * atk / (10.0 * Math.Max(1, def));

    /// <summary>
    /// Quanto a barra enche por passo de <see cref="Battle.TickInterval"/>.
    /// Jogador: ✅ cresce com o nível (o BR pesa pouco), L1 42, L2 77, L3 89, L4 95 … L20 124, L160 132.
    /// Ajuste ⌊60 + 17,5·ln L + 2·ln BR⌋ de L4 em diante (19 de 23 amostras a ±1), teto 132.
    /// Selvagem: ✅ bem mais lento e pelo BR, ⌊15,98·BR^0,2 − 30,29⌋ (13 de 16 exatas, 2 a ±1; ❓ o Yuramon L2 com BR 65 enche 8, não 6), BR 85 → 8, 165 → 14, 1040 → 33.
    /// </summary>
    public static int GaugeGain(Digimon digimon, bool wild) => GaugeGain(digimon.Level, digimon.Stats.Br, wild);

    public static int GaugeGain(int level, int br, bool wild)
    {
        br = Math.Max(1, br);
        if (wild) return Math.Max(1, (int)(15.98 * Math.Pow(br, 0.2) - 30.29));
        return level switch
        {
            <= 1 => 42,
            2 => 77,
            3 => 89,
            _ => Math.Min(132, (int)(60 + 17.5 * Math.Log(level) + 2 * Math.Log(br))),
        };
    }
}
