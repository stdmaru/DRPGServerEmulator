# Batalha (PvE)

Dados: 67 batalhas v322 em `captures.db` (sessões 53, 54, 56 e 58; 362 golpes, 5.888 `0x6B`). Código em `Game/Battle/` e `Network/Packets/Map/BattlePackets.cs`. Bloco de status em [status de digimon](../protocolo/structs/status-digimon.md).

## ✅ Fluxo
1. **Abertura:**
   - C2S `0x61` (u32 1, serial do grupo);
   - S2C `0x62` (serial da batalha, `01 02 00 00`);
   - C2S `0x62` (eco);
   - S2C `0x63` (10 blocos: time B, depois A);
   - C2S `0x69` (serial da batalha);
   - S2C `0x10` do tamer com estado 11;
   - S2C `0x69` vazio.
2. **Barras:** a cada **~165 ms** o servidor manda um `0x6B` (serial, i32 barra, i32 0) para cada combatente vivo. A barra cheia vale **500**.
3. **Escolha (jogador):**
   - O cliente manda C2S `0x35` (50 B): u8 2, u16 ícone (`0x0A` ataque, `0x04` fuga), u8 0, quem age, alvo.
   - O servidor responde com S2C `0x35` (36 B), com o mesmo ícone e o mesmo alvo:
     - **1** = marcada;
     - **4** = desmarcada (vem quando se clica de novo na mesma ação).
   - O cliente pode mandar a escolha durante a animação.
4. **Ação do jogador** (barra cheia e escolha marcada):
   1. `0x35` 3, com ícone e alvo zerados;
   2. `0x68`;
   3. se derrubou: `0x6E` e, se subiu de nível, `0x70`;
   4. `0x67` com lado 2.
   - A escolha é consumida, e o cliente marca de novo.
5. **Ação do selvagem:** age sozinho quando a barra enche: `0x68`, depois `0x67` com lado 1. O alvo é sorteado entre os vivos do time A (só há uma amostra com mais de um).
6. **Animação:**
   - Não há `0x6B` até o cliente mandar `0x69` (~3 s). O servidor responde com `0x69`, e as barras voltam, com a de quem agiu em 0.
   - Se o alvo escolhido morreu, o servidor desmarca: `0x35` 4, com ícone e alvo zerados.
7. **Vitória** (o último inimigo cai):
   - `0x68`, `0x6E`, [`0x70`], `0x67`;
   - logo depois, `0xB1` (EXP do tamer), `0x10` (estado 1), `0x84` (bloco do digimon) e `0x6D` 1.
8. **Fuga:**
   - `0x35` 3;
   - `0x65` (serial do digimon), `0x6A` (serial do tamer);
   - `0x6D` **2**. Conta como derrota: o jogo dá o título "First Lose".
9. **Saída da tela de resultado:** 6,6 s depois do `0x6D`, o cliente manda C2S `0xB4` com o bloco de cada digimon, e o servidor devolve cada um.

## ✅ `0x67` (1372 bytes)
- Posições 0..4 são o time de quem age e 5..9 o outro time, com 136 bytes por posição.
- O fim do pacote tem três u32: lado (1 inimigo, 2 jogador), id da skill (0 = ataque básico) e 0.

| Off | Tipo | Campo |
|---|---|---|
| 0 | 16 B | serial |
| 16 | i32 ×3 | HP, VP, EVP depois do golpe |
| 28 | i32 | barra de ação |
| 36 | u8 | 1 = quem age |
| 37 | u8 | alvo: **1 acertou, 3 errou** (dano 0) |
| 64, 68, 80 | | ❓ só com skill (1078 do L160, multi-alvo) |
| 134 | u16 | ações feitas na batalha |

## ✅ Dano do ataque básico
**dano = ⌊ATK² / (10·DEF)⌋**, com o ATK de quem ataca e o DEF de quem apanha, como estão no `0x63`.
- O `0x70` atualiza o ATK no meio da batalha: Mondrake L1 → L3 com 145 / 163 / 180 de ATK dá 33 / 42 / 51 de dano contra DEF 63.
- O dano é exato nos pares sem atributo (in-training contra in-training): 28 de 28 pares.
- ❓ Entre atributos (byte 161), o dano sai ×0,90…×1,05 do base, fixo para cada par. Exemplos: 1→2 ×0,90 / ×0,94, 1→1 ×1,00 / ×1,05, 1→0 ×0,95. Ainda não há regra; o emulador usa ×1.
- Skills: o id vem no fim do `0x67` (236 ×1,08, 144/145 ×1,15 e ×2, 701/702 dos inimigos, 1078 multi-alvo). ❓ Ainda não implementadas.

## ❓ Acerto
- O jogador erra 32 de 313 golpes (10 %). Os selvagens acertam 22 de 25, sem contar o Otamamon L50 contra o L160, que erra metade. Sem relação clara com nível ou BR.
- O emulador usa 90 % de acerto para todos.

## Barra de ação (por passo de 165 ms)
- ✅ **Jogador:**
  - A barra cresce com o **nível do digimon**; o BR pesa pouco. Valores: L1 42, L2 77, L3 89, L4 95, L5 99, L10 109, L12 115–118, L20 124, L160 132. O nível do tamer não entra.
  - Ajuste: ⌊60 + 17,5·ln L + 2·ln BR⌋ de L4 em diante (19 de 23 amostras a ±1), com teto de 132.
- ✅ **Selvagem:**
  - Pelo BR: ⌊15,98·BR^0,2 − 30,29⌋. Valores: BR 85 → 8, 165 → 14, 1040 → 33. Ficam 13 de 16 exatas e 2 a ±1.
  - ❓ O Yuramon L2 com BR 65 enche 8.
- Os valores secundários (+8 %, como 42/45 ou 117/127) são passos mais longos do relógio do servidor, não outra fórmula.

## ✅ Selvagens no mapa
- **Grupo comum:** a batalha é instanciada. O grupo **segue andando** (estado 1/4, nunca 11) durante e depois da batalha, e o mesmo serial é enfrentado várias vezes (`b1268476`, 6 vezes). Os inimigos voltam com HP cheio. O emulador cria cópias novas dos membros a cada batalha.
- **Grupo exclusivo:** fica em 11 durante a batalha e morre na vitória (8, depois 12). É o caso dos Otamamon da sessão 56 (byte 44 do `0x11` = 3) e do `V-mon(Tamed)`. No emulador é `Exclusive="true"` no `<Spawn>`.

## ✅ Tamer
- O offset 42 do `0x10` guarda o estado, com os mesmos códigos do `0x11`: 1 parado, 5 andando, 11 em batalha. O offset 43 é a direção.
- `0xB1` (1252 B): u16 ?, u16 nível, u32 0, i32 EXP, i32 EXP para o próximo (5·(L + 1)). Ver [EXP](formulas-exp.md).

## ✅ Recuperação
- Nenhum `0xB2` durante as batalhas.
- No fim, o oficial cura **logo**: 0,04–0,34 s depois do `0x6D` (41 de 41 batalhas sem HP/VP cheio), ainda na tela de resultado. Depois o tick de 5,2 s segue a partir daí. Ver [tick do mapa](tick-mapa.md).
- O emulador para a recuperação na batalha e agenda a próxima para 0,2 s depois do fim.

## ✅ Skills, invocação e recompensas (sessão 56)
- Skill: ícones `0x0B`/`0x0C` no `0x35`, só aprendida e com VP para o custo (20 + nível exigido); o id vai no fim do `0x67`. ❓ Multiplicador ajustado `1 + 0,0075·(nível exigido + 4) + 0,01·(nível − 1)` em 4 medidas.
- Invocação: `0x35` ícone 2 (alvo = serial do digimon do time) → `0x35` 1; com a barra cheia `0x68` → `0x64` (720: `u8 2`, `u8 posição`, `u16 0`, `u32 0`, bloco, 16 zeros) → `0x35` 3. Entra com a barra zerada.
- Inimigo derrubado: cada digimon do jogador que está de pé ganha a **EXP inteira** (um `0x6E` por digimon, quem derrubou primeiro); os **bits vêm uma vez**, no `0x6E` de quem derrubou.
- Fim com vários: `0xB1`, `0x84` e `0x6D` um por digimon; o `0xB4` de cada um na saída do resultado.

## ❓ Em aberto
- Derrota por nocaute: não capturada. O emulador manda `0x6D` 2 e deixa o digimon com 1 de HP.
- Fator de atributo no dano, itens em batalha, precisão do multiplicador de skill.
- Chance de acerto real e a chance de fuga (as 2 fugas capturadas deram certo).
