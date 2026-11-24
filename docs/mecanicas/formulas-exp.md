# Fórmulas de EXP (digimon e tamer)

Evidência: `captures.db`, v242 (sessões 1–48) e v322 (52–56). Digimon: offset 256 do [bloco de status](../protocolo/structs/status-digimon.md) (`0x02`, `0x70`, `0x71`, `0x84`, `0xB2`, `0xB4`…). Tamer: `0x02`, offsets 48 (fama), 52 (EXP), 56 (EXP para o próximo nível).

## Digimon ✅
**EXP para passar do nível L = ⌊(float)(20·L) × powf(1,2f, L)⌋**, calculado em **float32**.

| L | 1 | 2 | 3 | 4 | 5 | 10 | 13 | 14 | 20 | 21 | 160 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| captura | 24 | 57 | 103 | 165 | 248 | 1238 | 2781 | 3594 | 15335 | 19322 | 1925209472 |

- Exata nos níveis 1–21 (sem nenhuma exceção) e no 160.
- Em float64 a conta diverge do nível 29 em diante, por isso tem de ser float32.
- O nível 160 recebe **o valor do nível 77** (1.925.209.472). Do 78 em diante o resultado passa de 2³¹. ❓ Hipótese: o oficial trava no 77 do 78 em diante (só há captura do 160).
- A tabela do protótipo (`Resources/Digimon/ExpTable.xml`) estava certa até o 13 e errada do 14 em diante (2947 em vez de 3594). Foi substituída pela fórmula.

## Tamer ✅
**EXP para passar do nível L = 5·(L + 1)**. Capturas: L1 10, L5 30, L14 75, L15 80, L201 1010. A tabela antiga tinha só os níveis 1–8 dessa mesma conta.

## Recompensa de batalha (0x6E S2C, 32 bytes)
`16 B serial`, `u32 EXP` em 16, `f64 bits` em 24. ✅ Com o evento de EXP ativo, a recompensa parou exatamente no que faltava para subir (sessão 54: 24, 57, 103 = EXP dos níveis 1, 2, 3), descartando o excedente e deixando a EXP em 0. Configurável via `GameOptions.AllowMultiLevelUp` (`false` por padrão = comportamento oficial; `true` permite subir vários níveis de uma vez preservando o excedente).

## Evento de EXP (`Map S2C 0xE6`) ✅
Confirmado em `DigimonRPG.bin` (`0x00AC0093` → `0x00AEF8B0`, renderização em `0x00B8AF52..0x00B8B116`) e decifrado em 100% das capturas v322 (`captures.db`):
- **Criptografia**: **32 bytes** cifrados em **AES-128-ECB** usando a mesma cifra de sessão do Global (`GlobalSessionCipher`: `K = AES_C(hello_0x0E XOR W)`), com os 16 bytes do `Map S2C 0x0E`.
- **Plaintext (32 bytes)**:
  - `+0` (`u8`): `spriteIndex` (`0` desliga o badge; `k > 0` seleciona o frame de `Sprite\Exp_Bonus.spr` / `Exp_Bonus_Hot.spr` em passos de **+25%**: `20` = `+500%` na sessão 62, `48` = `+1200%` nas sessões 53–61).
  - `+4`, `+8`, `+12` (`u32 × 3`): `year`, `month`, `day` de expiração (formatados com `TextAttach_US` `3683`: `Expiration date is %d-%d-%d. (Year-Month-Day)`; o cliente ignora o pacote se `year == 2038`).
  - `+16` (`u8`): `isHotTime` (`0` = `Exp_Bonus.spr` com data no tooltip; `1` = `Exp_Bonus_Hot.spr`).
  - `+17` (`u8`): `mode` (`0` = data `0xE63`, `1` = texto fixo `0xE62` expirado).
  - `+18..31`: zeros.
- **No emulador**: tabela `exp_event` (`sql/upgrade/0006_exp_events.sql`), `ExpEventService` (polling periódico configurável via `Game:EventPollSeconds`, padrão 60 s), broadcast automático de `0xE6` ao mudar, aplicação de `1 + bonus_percent / 100` em `BattleSystem.GrantRewards`, e comando admin `/reloadevents`.

## No emulador
`Game/Stats/ExpFormulas.cs` (`DigimonExpToNext`, `TamerExpToNext`). O `0x02` manda a EXP do próximo nível do tamer pela fórmula. Testes: `ExpFormulaTests`.
