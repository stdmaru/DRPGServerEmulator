# Evolução: status por forma

Evidência: `captures.db`, sessão 56 (v322), blocos de status em `0x02`, `0x70`, `0x71`, `0x80`, `0x84`, `0xB2`, `0xB4`, `0xEF`, seguidos pelo **uid** do digimon. Script de análise: acompanhar `uid` × espécie ao longo do tempo (offsets em [status de digimon](../protocolo/structs/status-digimon.md)).

## Regras do jogo (dono do projeto, 2026-11-21)
- O digimon nasce **in-training**. No **nível 11** vira **rookie**, que passa a ser a forma-base: não volta mais para in-training.
- O rookie desbloqueia as evoluções por nível: **champion 21**, **ultimate 31**, **mega 41**.
- Ao atingir o nível de uma evolução **durante a batalha**, evolui automaticamente para ela e volta para rookie no fim da batalha.
- Depois disso o fluxo é: rookie → batalha → digievolui pelo menu → batalha → rookie.
- Evoluído, o **EVP é drenado periodicamente**. Com EVP zero, o digimon volta à força para rookie. *(A reproduzir.)*
- O item **Aura** mantém o digimon evoluído de forma permanente, inclusive fora da batalha. ❓ Comportamento exato a pesquisar (cliente, engenharia reversa, internet).

Batem com a sessão 56: uid 0x38a82 foi de Hopmon (31, in-training) a Monodramon (81, rookie) no nível 11, em batalha; uid 0x32610 foi de V-mon (rookie) a XV-mon (champion) no nível 21 e voltou no fim da batalha. ❓ O valor 20030, que aparece nos líderes de estágio 5 (`0x10` +100, bloco de status +320), pode ter relação com a Aura.

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

## Pacotes ✅ (sessão 56)
- **L11, in-training → rookie:** `0x6E` → `0x70` (bloco da forma antiga) → `0x80` (bloco do rookie). A forma in-training some.
- **L21/31/41 em batalha:** `0x6E` → `0x70` → `0x80` com a forma nova; no fim volta à forma-base (`0x84` com o rookie). Fora da batalha o estágio só é liberado (byte 24 do bloco).
- **Evolução pelo menu (em batalha):** `0x9E` C2S `u32 uid` → S2C `u8 1, u8 0, u16 × 3` (espécies de champion a mega liberadas, 0 = nenhuma); ícones `0x14`–`0x16` no `0x35`; com a barra cheia `0x68` → `0x81` (`u64 0`, bloco da forma nova, 16 zeros, 720 bytes) → `0x35` 3.
- **Janela de evolução:** `0x82` C2S `u32 estágio` (0 = atual), `u32 0`, bloco ecoado → S2C 720 com o bloco da forma daquele estágio.
- **`0x71` e `0x72`:** o pedido é o bloco ecoado + `u8 estágio da forma` + `u8 atributo (1–4)` ou `u8 skill (1–2)`. A resposta (704) traz o **bloco dessa forma**, não o da atual: dá para distribuir pontos e subir skills em formas liberadas pela janela.

## Pontos ✅
- Atributo: 2 por nível dentro da janela do estágio da forma (rookie do 1 ao 21, champion do 21 ao 31…; mega do 41 em diante).
- Skill: compartilhados, `(L − 1) − Σ(nível − 1)` sobre as skills de todas as formas; cada nível custa 1, até 10.
- Skill: bloco de 8 bytes por skill (`u32 id`, `u16 nível exigido` = coluna 1 do `digimon_skill_us`, `i16 nível`, 0 = não aprendida); passa a 1 ao chegar no nível exigido. Custo em batalha: VP 20 + nível exigido.
- Linha evolutiva: `tree_us`, colunas 6/10/14/18/22 (in-training a mega); com mais de um sucessor vale o mais frequente.

## No emulador
`Game/Entities/Digimon.cs`: `DigimonForm` (atributos e níveis de skill), `BaseForm`/`Current`, `UnlockedStage`, `BecomeRookie`,
`UnlockStage`, `EvolveTo`, `Devolve`, `FormAtStage`. `Game/Digimons/Progression.cs` manda `0x70`/`0x80` depois do level-up.
Banco: `digimon.digimon_id` = forma-base, `digimon.unlocked_stage`, tabela `digimon_form` (migração `0003`).

## ❓ Em aberto
- Atributos de champion em diante (STR/AGI não seguem a fórmula do rookie).
- HP/VP ao voltar à forma-base: o emulador mantém a mesma proporção do máximo (V-mon 285/319 depois de lutar como XV-mon).
- Taxa e intervalo da drenagem de EVP; item Aura.
- Se vitórias/batalhas são por forma ou do digimon.
