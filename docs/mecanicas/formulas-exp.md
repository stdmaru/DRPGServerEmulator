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

## Evento de EXP (ex.: 1200%) ❓
O cliente mostra o valor no canto superior esquerdo. O valor 1200 **não aparece** nas capturas v322 em nenhum formato simples (u16, u32, f32, f64 de 1200 ou de 12, nem em texto). Foram vistos `0x05`, `0x73`, `0x74`, `0xE6`, `0x152`, `0x121`, `0xCC` e os S2C do global.
- O cliente tem `Sprite\Exp_Bonus_Event.spr`, `Exp_Bonus_Hot.spr`, `Exp_Bonus.spr` e o formato `(+%d%%)`.
- Próximo passo: engenharia reversa (referências a esses sprites até o handler do pacote) ou uma captura com o evento mudando de valor.

## No emulador
`Game/Stats/ExpFormulas.cs` (`DigimonExpToNext`, `TamerExpToNext`). O `0x02` manda a EXP do próximo nível do tamer pela fórmula. Testes: `ExpFormulaTests`.
