# extract_status.py

`work/tools/extract_status.py`: varre os S2C de uma pasta de pcaps procurando a [struct de status de digimon](../protocolo/structs/status-digimon.md), por heurística de plausibilidade (nível 1–150, atributos 1–20000, HP/VP atuais ≤ máximos, nome imprimível).

```bash
python3 extract_status.py ../packets saida.json
```
Imprime as tuplas únicas `(id, nome, nível, STR, AGI, CON, INT, ATK, DEF, BR, MaxAG, MaxHP, MaxVP, MaxEVP)` e grava todas as amostras em JSON, com arquivo, opcode e offset.

Limitações:
- lê pcaps, não o `captures.db`. ❓ Migrar para ler do banco do [proxy](proxy.md);
- é heurística: pode perder blocos com valores fora das faixas.
