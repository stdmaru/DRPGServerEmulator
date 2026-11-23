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
**status = ⌊√(192 · (w₁·a + w₂·b))⌋ = ⌊8 · √(3 · (w₁·a + w₂·b))⌋**, com os atributos que o jogo descreve:

| Status | a | w₁ | b | w₂ | Erros nas amostras v322 limpas |
|---|---|---|---|---|---|
| ATK | STR | 8 | AGI | 3 | **0 / 74** |
| MaxHP | STR | 2 | CON | 5 | **0 / 56** |
| DEF | CON | 3 | INT | 1 | **0 / 31** |
| BR | AGI | 2 | INT | 7 | **0 / 48** |
| MaxVP | CON | 2 | INT | 5 | **0 / 31** |

- **Precisão exata:** Todos os 5 status compartilham a constante base $192 = 8^2 \times 3$ com pesos inteiros simples. Zera todos os desvios de ±1 presentes na antiga aproximação $\lfloor c_1\sqrt{a} + c_2\sqrt{b} \rfloor$ (incluindo o Level 2 onde DEF 94 virava 95, e os 8 erros de BR no V-mon).
- Nível 1 (10 em tudo): ATK 145, DEF 87, BR 131, HP 115, VP 115, igual ao jogo.
- Nível 2 (13/12/12/11): DEF = $\lfloor\sqrt{192 \cdot (3\cdot 12 + 1\cdot 11)}\rfloor = \lfloor\sqrt{9024}\rfloor = 94$ exato.
- Simetria: MaxHP e MaxVP possuem exatamente a mesma proporção de pesos $(2, 5)$, com deslocamento de atributos $(\text{STR}, \text{CON}) \to (\text{CON}, \text{INT})$.

## ✅ Outros
- **MaxEVP = L × (L − 1)** (L2 → 2, L6 → 30, L13 → 156).
- **MaxActionGauge = 500** em todas as amostras.

## ❓ Bônus fora da fórmula
- Amostras com o byte 328 = 15/15/15/15 (personagem nível 160) e algumas `0xB4`/`0x82` em batalha ficam 1–45 % acima da fórmula. Candidatos: equipamento, título, buff de batalha, status do tamer. O emulador ainda não aplica nada disso.
- O mega Tactimon L160 sem bônus (927/768/528/489) fica a ±2 da fórmula, mesmo com atributos ~10× maiores que as amostras do ajuste.

## Próximo passo
- Champion+: capturar a evolução de um digimon sem pontos distribuídos (ou anotar os pontos de cada forma). Ver [campanha](../captura/campanha.md).
- Bônus: capturar o mesmo digimon com e sem equipamento/título.
