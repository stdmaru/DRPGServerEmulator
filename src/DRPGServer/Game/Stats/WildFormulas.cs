namespace DRPGServer.Game.Stats;

/// <summary>
/// Status dos digimons selvagens. ✅ Só dependem do nível, não da espécie nem dos atributos (o 0x63 manda os
/// atributos zerados): 21 combinações espécie × nível em 125 batalhas v322, de L1 a L26, todas exatas.
/// Ver docs/mecanicas/selvagens.md. ❓ Chefes (byte 300 = 3: Otamamon L17, L50 da sessão 56) fogem da fórmula.
/// </summary>
public static class WildFormulas
{
    public static CombatStats Combat(int level)
    {
        int l = Math.Max(1, level);
        return new CombatStats(
            MaxHp: (13 * l + 220) / 2,          // ⌈116 + 6,5·(L − 1)⌉
            MaxVp: 25 * l + 25,
            MaxEvp: StatFormulas.MaxEvp(l),
            Atk: 6 * l + 74,
            Def: 3 * l + 60,
            Br: 5 * l + 80,
            MaxActionGauge: StatFormulas.MaxActionGauge);
    }
}
