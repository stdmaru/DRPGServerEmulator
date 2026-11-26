# extract_equipment_effects.py

`tools/extract_equipment_effects.py` (no repositório): gera `src/DRPG.Server/Resources/Item/EquipmentEffects.xml` a partir de `table\item_info_us.dat` do cliente v322. É o único lugar que lê o texto de bônus da coluna 28 (`STR +6 / ATK 3%`, `Card Slash 23 times`, `HP Recovery Amount 8`…). O servidor só carrega o XML (regra em [dados](../emulador/dados.md)).

## Uso
De `work/`, com o cliente desempacotado em `client/` e o `drpg_pack.py` em `tools/`:
```bash
python3 -I server/tools/extract_equipment_effects.py client/table.hf client/table.pf server/src/DRPG.Server/Resources/Item/EquipmentEffects.xml --pack tools
```
Depois revise o `git diff` do XML e rode os testes (`EquipmentTests.EquipmentEffects_xml_loads_all_structured_bonuses_and_auras` confere itens com bônus confirmados em captura).

## Saída
```xml
<Equipment ItemID="107" Name="Crest of Courage +5 (for Beginners)">
  <Modifier Kind="Atk" Value="75" />
  <Modifier Kind="Def" Value="40" />
  <Modifier Kind="Br" Value="350" />
</Equipment>
```
- Só entram itens dos tipos equipáveis (`EQUIP_TYPES`) com pelo menos um bônus; a v322 dá 9.152.
- `Kind` é o nome de `StatModifierKind`; `Value` é plano e `Percent` é percentual. `MaintainEvolution` sai como `Value="1"` (Aura of Evolution #555 entra por id, porque o texto dela não diz).
- A ordem dos modificadores é fixa (`MODIFIERS` no script), para o diff entre versões do cliente mostrar só o que mudou.

## Quando o cliente mudar
Texto novo que o script não reconhece fica fora do XML sem aviso. Ao atualizar o cliente, compare a contagem de itens e procure no diff itens que perderam bônus. Tipo de bônus novo precisa de valor novo em `StatModifierKind` (ver [dados](../emulador/dados.md#modificadores-de-status-gamestatsstatmodifierscs)) e de uma linha em `MODIFIERS`.
