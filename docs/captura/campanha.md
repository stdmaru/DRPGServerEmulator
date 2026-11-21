# Campanha de captura

Prioridade: tudo o que **só o servidor sabe**. Cada sessão de captura passa pelo [proxy](../ferramentas/proxy.md) com `--label` descritivo e um **marcador** a cada passo (`drpg_proxy.py mark "..."`).
Depois da Etapa 2, este checklist passa a ser gerado pela matriz de cobertura em [opcodes](../protocolo/opcodes.md).

## Regras gerais
- Uma sessão por experimento, com rótulo claro: `--label "status: Yuramon STR 1 a 1"`.
- Marque **antes** de cada ação (`mark "add STR #3"`). A análise cruza os pacotes com os marcadores pelo timestamp.
- Para estatística (dano, drop), repita a mesma ação nas mesmas condições muitas vezes.
- Anote as condições fixas no rótulo ou num marcador inicial: equipamento, buffs, título.

## 0. Fluxo de entrada na v322 ⬅ primeiro
Objetivo: corrigir o que o emulador já faz, do começo para o fim (ver [status](../projeto/status.md)). Cada item é uma sessão separada pelo proxy.
- [ ] **Lista de personagens** com 2+ personagens na conta (conteúdo do channel `0x12`).
- [ ] **Criar personagem**: um com cada digimon inicial; nome já usado; nome inválido. **Apagar personagem.**
- [ ] **Entrar no mapa com 2+ digimons no grupo**: preenche os 5 blocos do `0x02` e mostra onde entram os 8 bytes novos do bloco de status.
- [ ] Entrar no mapa com **itens no inventário e equipamento** (`0x03` hoje é todo zero no emulador).
- [ ] Entrar no mapa em **outro mapa e outra posição** (`0x10` +4 e os campos de `0x02` depois dos blocos).
- [ ] **Ficar parado ~2 min** no mapa (timers do oficial: `0xC4`, `0xB2`, `0x11`).
- [ ] **Andar e trocar de mapa** por portal (`0x20`, `0xB0`).
- [ ] **Sair do jogo** pelo menu, 2–3 vezes (`0x2B` e o `0xC4` que às vezes vem depois).

## 1. Experimento de status
Objetivo: fechar as [fórmulas de status](../mecanicas/formulas-status.md).
- [ ] Sem equipamento, título nem buff: colocar **1 ponto de cada vez** em STR, capturando cada resposta 0x71. Repetir para AGI, CON e INT.
- [ ] Repetir em 2–3 espécies de estágios diferentes (in-training, rookie, champion).
- [ ] Repetir a mesma espécie em dois níveis distintos.
- [ ] Equipar **um item por vez** (marcador com o nome do item) para medir o bônus de cada equipamento.
- [ ] Vários level-ups seguidos da mesma espécie, para medir o crescimento por nível.
- [ ] Trocar o título do digimon e do tamer, para ver se mexe nos atributos.

## 2. Batalha
- [ ] 30+ batalhas contra o **mesmo** monstro selvagem, com o mesmo digimon e o mesmo equipamento (dano, acerto, crítico, EXP, bits, drop).
- [ ] O mesmo monstro com outro digimon e outro nível (escala da EXP).
- [ ] Cada skill usada várias vezes (dano por skill, custo de VP).
- [ ] Fuga, derrota, evolução no meio da batalha, uso de item, captura (net).

## 3. Progressão e economia
- [ ] Quests do início ao fim: aceitar, progresso, entrega, recompensa.
- [ ] Crafting repetido (chance de sucesso).
- [ ] Evolução e digixros (requisitos, falhas).
- [ ] Compra e venda em cada loja (preços), depósito, banco de bits.

## 4. Social (multi-conta)
- [ ] Ver outro jogador andar, entrar e sair do mapa (broadcast).
- [ ] Party: convite, aceite, batalha em grupo, divisão de EXP.
- [ ] Trade, loja pessoal, chat (normal, sussurro, megafone), amigos, guild.
- [ ] PvP.

## 5. Erros
- [ ] Inventário cheio, sem bits, nível insuficiente, item inválido, nome proibido, sessão expirada.

## 6. Conteúdo avançado
- [ ] Dungeons, Spiral Mountain, Infinite Mountain, eventos ativos, cash shop. Pedir ajuda à comunidade, com logs pelo proxy de outros jogadores.

## Fora do jogo
- [ ] Mirror da CDN e do patch server (URLs em [servidores](../protocolo/servidores.md)).
