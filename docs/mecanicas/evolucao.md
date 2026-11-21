# Evolução: status por forma

Evidência: `captures.db`, sessão 56 (v322), blocos de status em `0x02`, `0x70`, `0x71`, `0x80`, `0x84`, `0xB2`, `0xB4`, `0xEF`, seguidos pelo **uid** do digimon. Script de análise: acompanhar `uid` × espécie ao longo do tempo (offsets em [status de digimon](../protocolo/structs/status-digimon.md)).

## O que a captura mostra ✅
Cada digimon tem **um uid** e cada forma (espécie da linha evolutiva) tem **status próprios**. Ao evoluir, o bloco troca de espécie, mas o uid não muda.

| Campo | Por forma | Compartilhado | Evidência |
|---|---|---|---|
| espécie (20), estágio (160) | ✅ | | uid 0x32610: 635 rookie ↔ 735 champion |
| STR/AGI/CON/INT (48) | ✅ | | 0x32610: rookie (91,76,70,69), champion (114,98,91,91); ao voltar, o rookie mantém os seus |
| pontos de atributo (272) | ✅ | | 0x32610: rookie 6, champion 0. 0x38a85 (L160): mega 238, rookie 40; cada um gastou os seus |
| ATK/DEF/BR, MaxHP/MaxVP | ✅ (derivados dos atributos da forma) | | |
| skills (276…) | ✅ | | rookie skill 144, champion 146 (nível 1) |
| nível (46), EXP (224/256) | | ✅ | 0x38a85: L160 e EXP iguais em 674 e 921 |
| MaxEVP (244) | | ✅ | 25440 nas duas formas (= L × (L−1)) |
| pontos de skill (274) | | ✅ ❓ | 159 nas duas formas; 87 nas duas depois de subir skills |
| nome (25) | | ✅ | o rename vale para todas as formas |

- Ao evoluir, o oficial manda `0x80` com o bloco da forma nova. No fim da batalha (sem item), volta para o rookie (`0x84` com o bloco do rookie, inalterado).
- Primeira evolução de 0x38a82 (L11): o champion já começa com atributos próprios (60,50,50,50), ❓ que parecem uma base da forma por nível, não derivados do rookie.
- Byte 160 (estágio): 01 in-training ❓, 02 rookie, 03 champion, 05 mega. 161 e 162 variam por espécie.

## Impacto no emulador
O protótipo (e a reescrita) guarda **um** conjunto de status por digimon. É preciso separar:
- **Digimon**: uid, nome, nível, EXP, pontos de skill ❓, vitórias/batalhas ❓, forma atual.
- **Forma** (digimon × espécie): atributos, pontos de atributo, skills e níveis de skill.

## ❓ Em aberto
- Como nascem os atributos de uma forma na primeira evolução (base fixa por espécie/nível?).
- Se vitórias/batalhas são por forma ou do digimon.
- Se uma forma ganha pontos de atributo nos níveis em que não estava ativa.
