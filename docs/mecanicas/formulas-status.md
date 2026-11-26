# Fórmulas de status

Dados: blocos de status de todas as capturas em `captures.db` (1634 blocos, 434 únicos). O ajuste usa as 80 amostras v322 sem bônus (byte 328 zerado), de in-training a champion (níveis 1–21). Layout em [status de digimon](../protocolo/structs/status-digimon.md). Código em `Game/Stats/StatFormulas.cs`.

## ✅ Atributos (STR, AGI, CON, INT)
**In-Training / Rookie:**
`atributo = 10 + bônus do estágio + status-base × (min(L, teto) − 1) + pontos distribuídos`

**Champion / Ultimate (cumulativo por janela de estágio):**
`atributo(estágio) = base(estágio anterior, teto anterior) + 20 + status-base(estágio) × max(0, min(L, teto) − nível inicial + 1) + Σ pontos distribuídos (da forma-base até o estágio atual)`

- **Status-base:** crescimento por nível, nas colunas 15..18 da `dex_us` ([arquivos de dados](../cliente/arquivos-de-dados.md)). Exemplo: Gigimon 3/2/2/1, Yuramon 2/2/2/2.
- **Bônus do estágio:** 0 em in-training e **+20 por estágio** acima dele (+20 rookie, +20 champion, +20 ultimate, +20 mega).
  - Rookie confirmado no nível 11, sem pontos distribuídos: Monodramon (3/2/2/2) → 60/50/50/50, V-mon (3/2/2/1) → 60/50/50/40, Palmon (3/3/1/1) → 60/60/40/40.
- **✅ Champion e Ultimate (sessão 56, 0 erros):** cada estágio parte do base acumulado do estágio anterior no teto (L20 para rookie, L30 para champion, L40 para ultimate), soma `+20` e aplica o seu próprio crescimento apenas nos níveis da sua janela (`21..30` champion, `31..40` ultimate). Além disso, **herda todos os pontos de atributo distribuídos nas formas anteriores da linha** (Rookie → Champion → Ultimate → Mega):
  - **XV-mon L21 (`735`, growth `3/2/1/2`) vindo de V-mon (`635`, growth `3/2/2/1`):** base V-mon L20 `(87,68,68,49)` + alloc V-mon `(4,8,2,20)` + `20` + `1×(3,2,1,2)` = **`(114,98,91,91)`** (exato).
  - **Gryzmon L160 (`781`, growth `3/2/2/1`) vindo de Bearmon (`674`, growth `3/2/2/1`):** `(87,68,68,49) + 20 + 10×(3,2,2,1) = (137,108,108,79)`; com alloc do rookie `(13,27,0,0)` → **`(150,135,108,79)`** (exato).
  - **Grappu Leomon L160 (`886`, growth `3/2/2/1`):** `(137,108,108,79) + 20 + 10×(3,2,2,1) = (187,148,148,109)`; com alloc do rookie `(13,27,0,0)` → **`(200,175,148,109)`**; após gastar 20 pts no champion `(9,11,0,0)` e 20 no ultimate `(20,0,0,0)` → **`(229,186,148,109)`** (exato).
- **❓ Mega (amostra única):** na amostra L160 (`921`, growth `4/3/1/1`), a diferença acima do Ultimate foi `+20 + 120×(6,5,3,3)`, equivalente a `growth + (2,2,2,2)` por nível (além da herança de pontos distribuídos, confirmada em `0x71`/`0x82`). Aguardando mais amostras de Mega para confirmar se `+2/nível` é regra geral de Mega.
- O nível **não entra direto** nos status: o mesmo digimon com os mesmos atributos tem os mesmos status no nível 10 e 11 (e 20 e 21).
- A espécie e o estágio também não entram: a fórmula abaixo vale igual de in-training a mega.

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
