# Fórmulas de status

Dados: 54 amostras (19 únicas) extraídas por [extract_status.py](../ferramentas/extract-status.md) dos 52 pcaps. Espécies 10, 14, 51, 56, 619 e 635, níveis 1–13. Layout em [status de digimon](../protocolo/structs/status-digimon.md).

## ✅ Confirmadas
- **MaxEVP = L × (L − 1)**: bate nas 19 amostras (L2→2, L6→30, L13→156).
- **MaxActionGauge = 500** em todas as amostras (❓ pode variar em espécies não vistas).

## ❌ Fórmulas atuais do emulador (`Game/Stats/StatFormulas.cs`)
São lineares, com coeficientes quebrados. Erro máximo contra as amostras: ATK −24…+109, DEF −8…+42, BR −5…+57, MaxHP −8…+27, MaxVP −10…+31. **Não usar como referência.**

## ❓ Hipóteses em aberto (ATK, DEF, BR, MaxHP, MaxVP)
- Um ajuste linear em STR, AGI, CON, INT, nível e constante ainda deixa resíduo de 3–4, com coeficientes negativos sem sentido. Não é uma soma simples.
- **Ganho por ponto muda:**
  - Yuramon (14) L2, INT 13→14: DEF +1, BR +5, VP +4.
  - V-mon (635) L11, INT 40→60: DEF +10, BR +45 (2,25/pt), VP +37 (1,85/pt).
  - A explicação pode ser retorno decrescente, peso por espécie ou estágio, ou peso por nível. Os dados atuais não separam essas hipóteses.
- **Há bônus fora dos 4 atributos:** Guilmon (51) L13 com os mesmos atributos aparece com ATK 384→386 e HP 284→285. Candidatos: equipamento, título, buff, status do tamer.
- A tabela `digimon_us` do cliente **não tem** atributos base nem crescimento (só nome, skills e tipo). Esses dados só existem no servidor.

## Amostras-chave
| ID | L | STR AGI CON INT | ATK DEF BR HP VP |
|---|---|---|---|
| 14 | 1 | 10 10 10 10 | 145 87 131 115 115 |
| 14 | 2 | 12 12 12 13 | 159 96 148 126 130 |
| 14 | 2 | 12 12 12 14 | 159 97 153 126 134 |
| 14 | 6 | 20 20 20 22 | 205 125 192 163 169 |
| 14 | 7 | 22 22 22 24 | 215 131 201 171 177 |
| 14 | 8 | 24 24 24 26 | 225 137 210 179 184 |
| 14 | 10 | 28 28 28 30 | 243 147 225 193 198 |
| 635 | 11 | 60 50 50 40 | 347 190 270 266 240 |
| 635 | 11 | 60 50 50 60 | 347 200 315 266 277 |
| 635 | 12 | 63 52 52 61 | 355 204 319 272 280 |

Crescimento por nível observado no Yuramon: +2 em cada atributo por nível (INT às vezes +3).

## Próximo passo
O experimento controlado está na [campanha](../captura/campanha.md), item 1. Com 30–50 respostas de 0x71 deve dar para fechar fórmulas inteiras.
