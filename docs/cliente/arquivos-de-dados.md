# Arquivos de dados do cliente

## Pacotes
| Arquivo | Conteúdo |
|---|---|
| `table.hf` + `table.pf` | 116 tabelas (`table/*.dat` → texto separado por `|`, e `table/language/*.bin`) |
| `resource.hf` + `resource.pf` | ~11,8 GB de assets (sprites, mapas, UI) |

Ferramenta: `work/tools/drpg_pack.py` (ver `work/tools/how_to_use.txt`):
```bash
python3 drpg_pack.py list client/table.hf
python3 drpg_pack.py unpack client/table.hf out/table     # --no-txt mantém os .dat
python3 drpg_pack.py pack out/table client/table.hf
```

## O servidor lê o pacote na partida
O emulador abre o `table.hf`/`table.pf` com `Data/GameData/ClientPack.cs`, uma porta do `drpg_pack.py` só de
leitura. O pacote fica em `src/DRPGServer/Resources/Client/`, **fora do git** ([legal](../projeto/legal.md)):
copie o `table.hf` e o `table.pf` do cliente (v322) para lá, ou aponte `Game:ClientTablePath` para outro. Os casos de borda são testados com um pacote sintético
em `tests/DRPGServer.Tests/Fixtures/`. `Resources/` guarda só o que o cliente não tem: spawns, drops e os
pontos de chegada dos portais.

| Tabela | Colunas usadas (índice a partir de 0) | Vira |
|---|---|---|
| `digimon_us` | 0 id, 1 nome, 2 estágio (✅ 1 in-training, 2 rookie, 3 champion, 4 ultimate, 5 mega), 3 atributo, 5 skill 1, 9 skill 2 | espécies |
| `dex_us` | 0 id, 1 nome, 15..18 crescimento por nível STR/AGI/CON/INT | `DigimonSpecies.Growth` |
| `item_info_us` | 0 id, 1 nome (15 cartas com id 0 ficam de fora) | itens |
| `map_us` | 0 id, 1 nome, 12/13 posição no mapa-múndi, 16..31 quatro portais (nome do destino, ❓ flag, x, y; `Entire` = vazio) | zonas e [portais](../mecanicas/portais.md) |

Casamento `digimon_us` → `dex_us`: mesmo id e mesmo nome; senão, pelo nome (quando o nome não é ambíguo);
senão, pelo id. 762 espécies; 5 sem dex (sem crescimento): 515 Kazemon, 516 Zephyrmon, 787 Devimon,
796 Sorcerimon, 911 Treasure Chest 11. A 619 tem nome vazio no `digimon_us` e leva o da dex (Bearmon).

## Outras tabelas
| Tabela | Uso |
|---|---|
| `digimon_skill_us`, `digimon_title_us`, `tamer_title_us` | skills e títulos |
| `item_info_us`, `item_change_us`, `item_composite_us`, `item_equipment_point_us` | itens |
| `make_item_us`, `makeitem_us`, `makeitem_material_item_us` | crafting (❓ sem as chances de sucesso) |
| `monsterregen_us` | ❓ id composto, espécie, nome, nível, ?; **não tem mapa nem área**, então os spawns continuam em `Resources/Map/DigimonSpawns.xml` |
| `map_us`, `npc_info_us`, `npc_function_us`, `npcdialogwindow_us`, `questdialog_us` | mapas, NPCs, diálogos, texto de quest |
| `eventshop_us`, `p_shop_us`, `shopdialog_us`, `ingameshop_*` | lojas e cash shop |
| `digixros_*`, `evo_card_info_us`, `specialevolreturn_us` | evolução |

## O que **não** está no cliente (precisa vir de captura)
Fórmulas de ATK/DEF/BR/HP/VP, crescimento de champion em diante, ponto de chegada dos portais, áreas de spawn, tabelas de drop, EXP e bits por monstro, condições e recompensas de quest, chances de crafting e evolução, preços (❓ conferir `eventshop`).
