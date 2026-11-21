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

## Tabelas relevantes para o servidor
| Tabela | Uso |
|---|---|
| `digimon_us` | ID, nome, estágio, skills, tipo. **Não tem atributos base nem crescimento.** |
| `digimon_skill_us`, `digimon_title_us`, `tamer_title_us` | skills e títulos |
| `item_info_us`, `item_change_us`, `item_composite_us`, `item_equipment_point_us` | itens |
| `make_item_us`, `makeitem_us`, `makeitem_material_item_us` | crafting (❓ sem as chances de sucesso) |
| `monsterregen_us` | ❓ spawns por mapa (comparar com `Resources/Map/DigimonSpawns.xml`) |
| `map_us`, `npc_info_us`, `npc_function_us`, `npcdialogwindow_us`, `questdialog_us` | mapas, NPCs, diálogos, texto de quest |
| `eventshop_us`, `p_shop_us`, `shopdialog_us`, `ingameshop_*` | lojas e cash shop |
| `digixros_*`, `evo_card_info_us`, `specialevolreturn_us` | evolução |

## O que **não** está no cliente (precisa vir de captura)
Atributos base e crescimento, fórmulas, tabelas de drop, EXP e bits por monstro, condições e recompensas de quest, chances de crafting e evolução, preços (❓ conferir `eventshop`).
