# extract_digimon_titles.py

`tools/extract_digimon_titles.py` (no repositório): gera `src/DRPGServer/Resources/Digimon/DigimonTitles.xml` a partir de `table\digimon_title_us.dat` do cliente v322. O servidor só carrega o XML (regra em [dados](../emulador/dados.md)).

## Uso
De `work/`, com o cliente desempacotado em `client/` e o `drpg_pack.py` em `tools/`:
```bash
python3 -I server/tools/extract_digimon_titles.py client/table.hf client/table.pf server/src/DRPGServer/Resources/Digimon/DigimonTitles.xml --pack tools
```
Depois revise o `git diff` do XML e rode os testes (`DigimonTitleTests`).

## O que sai de onde
- **Id e nome:** colunas 0 e 1.
- **Bônus:** as linhas `※ STR +1`, `※ All stats +15`, `※ DEF +2%`… das colunas 3..13 viram `<Modifier>`. `All stats` vira os quatro atributos. ❓ `HP +N%` sai como plano (`Value`), como o servidor sempre tratou.
- **Desbloqueio automático:** a tabela não tem. As regras ficam em `UNLOCKS` no script, com a evidência de captura, e saem como `<Unlock Condition Value>`. Regra nova entra ali, nunca à mão no XML (a próxima geração apagaria).
