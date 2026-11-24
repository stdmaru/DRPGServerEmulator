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
2. **Barras:** a cada **200 ms** o servidor manda um `0x6B` (serial, i32 barra, i32 0) para cada combatente vivo com barra `< 500` (após ~540 ms na abertura e ~200 ms pós-animação). A barra cheia vale **500** (`0x6B` 500 enviado uma única vez).
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
8. **Derrota por nocaute** (✅ sessão 64, `conn=408`):
   - Quando um digimon do jogador é nocauteado, perde **1 % de `ExpToNext(L)`** via `0x6E` negativo (`EXP = −⌊ExpToNext/100⌋`, `bits = 0`) antes do `0x67`.
   - Ao cair o último digimon do jogador: `0xB1`, `0x10` (estado 1), `[0x4F se ganhou título]`, `0x84` (**com `HP = 0`**) e `0x6D` **2** (um por digimon), **sem teleporte (`0xB0`)**.
   - Na volta ao mapa (`S2C 0xB4`), **apenas o líder** revive (`HP = 10` em `conn=408`) e volta a regenerar via `0xB2` (`15 → 20 → ...`); os demais digimons nocauteados permanecem com `HP = 0` e **não** regeneram automaticamente.
9. **Fuga:**
   - `0x35` 3;
   - `0x65` (serial do digimon), `0x6A` (serial do tamer);
   - `0x6D` **2**. Conta como derrota: o jogo dá o título "First Lose".
10. **Saída da tela de resultado:** 6,6 s depois do `0x6D`, o cliente manda C2S `0xB4` com o bloco de cada digimon, e o servidor devolve cada um.

## ✅ `0x67` (1372 bytes)
- Posições 0..4 são o time de quem age e 5..9 o outro time, com 136 bytes por posição.
- O fim do pacote tem três u32: lado (1 inimigo, 2 jogador), id da skill (0 = ataque básico) e 0.

| Off | Tipo | Campo |
|---|---|---|
| 0 | 16 B | serial |
| 16 | i32 ×3 | HP, VP, EVP depois do golpe |
| 28 | i32 | barra de ação |
| 36 | u8 | quem age: **1** ataque básico/skill corpo-a-corpo (`Type = 1`), **2** skill à distância (`Type = 2`, `DigimonRPG.bin @ 0x00AF552B`) |
| 37 | u8 | alvo: **1 acertou, 3 errou** (dano 0); em skills multi-alvo, preenchido independentemente para cada alvo vivo atingido |
| 64, 68, 80 | i32 | triggers de efeitos/passivas (`DigimonRPG.bin @ 0x00AF6350`) |
| 134 | u16 | ações feitas na batalha |

## ✅ Dano do ataque básico
**dano = ⌊ATK² / (10·DEF) · mult⌋**, com o ATK de quem ataca e o DEF de quem apanha, como estão no `0x63`, multiplicado pelo fator de afinidade e atributo `mult`.
- O `0x70` atualiza o ATK no meio da batalha: Mondrake L1 → L3 com 145 / 163 / 180 de ATK dá 33 / 42 / 51 de dano contra DEF 63.
- **Ataques neutros:** O dano é exato nos pares sem vantagem/desvantagem (ex: Hopmon neutro contra Yuramon): $mult = 1{,}0$.
- **Vantagem de afinidade e atributo:**
  - Triângulo clássico (byte 161 do bloco): Vacina (1) > Vírus (3) > Dados (2) > Vacina (1). Vantagem dá $\times 1{,}05$, desvantagem dá $\times 0{,}90\dots 0{,}95$.
  - Afinidade elemental (coluna 22 de `digimon_us`, bitmask no byte 162): Fogo (1) > Natureza (2, 9) > Água/Gelo (8, 10) > Fogo (1). Vantagem dá $\times 1{,}05$ (ex: Gigimon fogo vs Yuramon planta dá dano $+5\%$, explicando os 2~5 pontos a mais).
- Skills: o id vem no fim do `0x67` (236 ×1,08, 144/145 ×1,15 e ×2, 701/702 dos inimigos, 1078 multi-alvo). Multiplicador de skill acumula com a afinidade.

## ❓ Acerto
- O jogador erra 32 de 313 golpes (10 %). Os selvagens acertam 22 de 25, sem contar o Otamamon L50 contra o L160, que erra metade. Sem relação clara com nível ou BR.
- O emulador usa 90 % de acerto para todos.

## Barra de ação (por passo de 200 ms)
- ✅ **Jogador (fórmula exata do cliente, `DigimonRPG.bin @ 0x0085E0B8`):**
  - O cliente só começa a encher a barra após a animação de entrada/retorno (`[digimon + 0x149]` sair dos estados `2..16`): **~540 ms** na abertura da batalha (mediana de 536 ms em 76 batalhas) e **~200 ms** na retomada pós-animação (mediana de 196 ms em 272 turnos).
  - Depois disso, anima localmente a cada `30 ms` somando o inteiro `step = ⌊min(50, (4 − 2,5/L)·4 + AGI/25)⌋`. Como descarta o resto de `timeGetTime()`, a 60 FPS cada passo leva **~33,3 ms** (`6` passos a cada `200 ms`).
  - No emulador, cada passo de `200 ms` soma `6 × step` (após `OpeningDelay = 540 ms` / `ResumeDelay = 200 ms`), mantendo L1 a L160 dentro de `0..+100 ms` da barra visual do cliente.
  - O campo `off296` do bloco de status usa a versão contínua `⌊min(50, (4 − 2,5/L)·4 + AGI/25) · (200/30)⌋` (**26/26 amostras de L1 a L160 com 0 erros**): L1 (AGI 10) → 42, L2 (AGI 12) → 76, L3 (AGI 14) → 88, L10 (AGI 32) → 108, L11 (AGI 60) → 116, L20 (AGI 76) → 123, L160 (AGI 95) → 131.
  - Ao atingir `500`, o servidor envia `0x6B(..., 500)` **uma única vez** e para de enviar para aquele combatente até a ação ocorrer.
- ✅ **Selvagem:**
  - Pelo BR: ⌊15,98·BR^0,2 − 30,29⌋. Valores: BR 85 → 8, 165 → 14, 1040 → 33. Ficam 13 de 16 exatas e 2 a ±1.
  - ❓ O Yuramon L2 com BR 65 enche 8.

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
- Skill (`digimon_skill_us.dat`): ícones `0x0B`/`0x0C` no `0x35`, só aprendida e com VP para o custo (`20 + nível exigido`); o id vai no fim do `0x67`.
  - **Coluna 4 (`Type`):** `1` corpo-a-corpo (offset 36 = `1`), `2` à distância (offset 36 = `2`, ex.: `701` *Lullaby Bubble*, `1078` *Sun of Sorrows*).
  - **Coluna 5 (`TargetCount`):** `1` alvo único (695 skills), `2` dois alvos (13 skills, ex.: `9` *Double Fire*), `5` área / todos os inimigos vivos (353 skills, ex.: `1078` *Sun of Sorrows*). Em skills multi-alvo, cada alvo vivo rola acerto/dano independentemente e cada inimigo derrubado gera seu próprio `0x6E` antes do `0x67`; inimigos já nocauteados ficam com `offset 37 = 0`.
  - **Selvagens:** usam skills aprendidas em ~31 % dos turnos (22 de 70 ações capturadas, ex.: Otamamon com `701`/`702`) quando têm VP.
  - ❓ Multiplicador ajustado `1 + 0,0075·(nível exigido + 4) + 0,01·(nível − 1)` em 4 medidas.
- Captura comum: `0x35` ícone 1 (alvo = selvagem) → `0x68` → `0xB3` → `0x35` 3, até 3 por batalha. Ver [captura](captura.md).
- Invocação: `0x35` ícone 2 (alvo = serial do digimon do time) → `0x35` 1; com a barra cheia `0x68` → `0x64` (720: `u8 2`, `u8 posição`, `u16 0`, `u32 0`, bloco, 16 zeros) → `0x35` 3. Entra com a barra zerada.
- Inimigo derrubado: cada digimon do jogador que está de pé ganha a **EXP inteira** (um `0x6E` por digimon, quem derrubou primeiro); os **bits vêm uma vez**, no `0x6E` de quem derrubou.
- Fim com vários: `0xB1`, `0x84` e `0x6D` um por digimon; o `0xB4` de cada um na saída do resultado.

## ❓ Em aberto
- Fator de atributo no dano, itens em batalha, precisão do multiplicador de skill.
- Chance de acerto real e a chance de fuga (as 2 fugas capturadas deram certo).
