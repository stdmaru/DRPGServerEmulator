# Fórmulas de status

Dados: blocos de status de todas as capturas em `captures.db` (1634 blocos, 434 únicos). O ajuste usa as 80 amostras v322 sem bônus (byte 328 zerado), de in-training a champion (níveis 1–21). Layout em [status de digimon](../protocolo/structs/status-digimon.md). Código em `Game/Stats/StatFormulas.cs`.

## ✅ Atributos (STR, AGI, CON, INT)
**atributo = 10 + bônus do estágio + status-base × (L − 1) + pontos distribuídos**

- **Status-base:** crescimento por nível, nas colunas 15..18 da `dex_us` ([arquivos de dados](../cliente/arquivos-de-dados.md)). Exemplo: Gigimon 3/2/2/1, Yuramon 2/2/2/2.
- **Bônus do estágio:** 0 em in-training e **+20** em rookie.
  - Rookie confirmado no nível 11, sem pontos distribuídos: Monodramon (3/2/2/2) → 60/50/50/50, V-mon (3/2/2/1) → 60/50/50/40, Palmon (3/3/1/1) → 60/60/40/40.
- ❓ **Champion em diante:** só há uma amostra limpa. XV-mon L21 = 114/98/91/91, que é o V-mon L21 do mesmo jogador (91/76/70/69, com 20 pontos em INT) +21..23 em cada atributo. Não dá para separar bônus de estágio de pontos herdados. Cada forma tem pontos próprios ([evolução](evolucao.md)).
- O nível **não entra direto** nos status: o mesmo digimon com os mesmos atributos tem os mesmos status no nível 10 e 11 (e 20 e 21).
- A espécie e o estágio também não entram: a fórmula abaixo vale igual de in-training a champion.

## ✅ Status de combate
**status = ⌊c₁·√a + c₂·√b⌋**, com os atributos que o jogo descreve:

| Status | a | c₁ | b | c₂ | Amostras v322 com erro de 1 |
|---|---|---|---|---|---|
| ATK | STR | 34,521 | AGI | 11,364 | 1 / 80 |
| MaxHP | STR | 11,536 | CON | 25,054 | 0 / 80 |
| DEF | CON | 21,115 | INT | 6,603 | 0 / 80 |
| BR | AGI | 9,900 | INT | 31,642 | 8 / 80 |
| MaxVP | CON | 11,213 | INT | 25,446 | 2 / 80 |

- O expoente 0,5 sai do ajuste livre (melhor p = 0,500 em todos). Ajustes alternativos piores: log, soma linear, √(w·a + b), arredondamento por termo, `round`/`ceil`.
- Os coeficientes são o centro da região que zera (ou minimiza) os erros. A região do HP inclui 11,5 / 25,0 e a do BR inclui c₂² ≈ 1000, mas não há um padrão "redondo" claro.
- Nível 1 (10 em tudo): ATK 145, DEF 87, BR 131, HP 115, VP 115, igual ao jogo.
- Marginal por ponto: d/dx = c/(2√x), por isso o ganho cai com o atributo. Exemplos: STR 20 → ATK +3,8/pt; STR 65 → +2,1/pt.
- ❓ **BR** erra 1 ponto em 8 amostras, todas do V-mon L12–18 com INT 61–67. Pode haver um termo pequeno que falta ou um arredondamento diferente.
- Nas v242 (13 amostras) os coeficientes que fecham são um pouco diferentes (ATK 33,68 / 12,25). ❓ Pode ser outra versão da fórmula ou poucas amostras.

## ✅ Outros
- **MaxEVP = L × (L − 1)** (L2 → 2, L6 → 30, L13 → 156).
- **MaxActionGauge = 500** em todas as amostras.

## ❓ Bônus fora da fórmula
- Amostras com o byte 328 = 15/15/15/15 (personagem nível 160) e algumas `0xB4`/`0x82` em batalha ficam 1–45 % acima da fórmula. Candidatos: equipamento, título, buff de batalha, status do tamer. O emulador ainda não aplica nada disso.
- O mega Tactimon L160 sem bônus (927/768/528/489) fica a ±2 da fórmula, mesmo com atributos ~10× maiores que as amostras do ajuste.

## Próximo passo
- Champion+: capturar a evolução de um digimon sem pontos distribuídos (ou anotar os pontos de cada forma). Ver [campanha](../captura/campanha.md).
- Bônus: capturar o mesmo digimon com e sem equipamento/título.
