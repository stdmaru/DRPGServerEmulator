# Emulador: segurança

Modelo de ameaça e controles do servidor. Vale desde já, mesmo com o jogo incompleto: **feature faltando não é motivo para controle faltando**. Visão geral em [arquitetura](arquitetura.md). O caminho de um pacote está em [handlers](handlers.md).

> Peças marcadas com **(PR #41: Xn)** entram no item `Xn` do checklist do PR de refatoração da fundação.

## Modelo de ameaça
- O cliente é **hostil**: qualquer byte pode ser forjado, repetido, truncado, enviado fora de ordem ou em volume.
- O atacante conhece o protocolo (o cliente é público), conhece nomes de conta e de personagem (aparecem no jogo) e pode abrir conexões direto em qualquer uma das 4 portas.
- O alvo principal é a **economia**: duplicar item, moeda ou digimon, ou obter algo sem pagar. Depois vêm roubo de conta e derrubada do servidor.
- O banco pode vazar: nada que permita entrar numa conta fica em claro.

## Autenticação entre servidores
Código: `Network/AccountRegistry.cs`. Testes: `AccountRegistryTests`.
- O channel autentica (usuário + `auth_key`) e emite um **ticket** da conta, amarrado ao IP da conexão do channel (IPv4 mapeado em IPv6 conta como IPv4). Uma segunda autenticação na mesma conexão fecha a conexão.
- Mapa e global só aceitam a conta com ticket válido e **do mesmo IP**. O username do pacote só serve para achar o ticket, nunca para autenticar.
- O ticket vale **uma vez por servidor**: uma segunda conexão de mapa é recusada, mesmo depois que a primeira fecha. Entrar de novo exige autenticar no channel. ✅ O cliente mantém o channel aberto durante o jogo e fecha as três conexões juntas ao sair (`first-access`, `login-into-map`). ❓ Voltar para a seleção de personagem sem sair do jogo não foi capturado; se o cliente fizer isso sem um novo `0x04`, o channel vai precisar reemitir o ticket.
- Enquanto alguma sessão da conta está aberta, o ticket não vence. Depois que a última fecha, ele vale por `Network:AccountTicketSeconds` (padrão 60).
- Um novo login no channel substitui o ticket e derruba as sessões do anterior (channel, mapa e global).
- Recusa vai para o log com o motivo (`ClaimRejection`: sem ticket, vencido, outro IP, já usado) e fecha a conexão.
- `auth_key` é guardada como hash PBKDF2-SHA256 com sal, e a comparação é em tempo constante (**PR #41: S8**).

## Entrada e saída do mundo
Código: `PlayerSystem` (estado da conta, thread do mundo) e `WorldEntry` (o fluxo do `0x01`). Testes: `WorldEntryTests`.

Cada conta tem um estado no `PlayerSystem`: `Offline → Loading → Online → Saving → Offline`.
- **Entrar** (`WorldEntry.EnterAsync`): `BeginEntry` na thread do mundo põe a conta em `Loading`. Se a conta está `Online`, o jogador antigo sai do mundo (o save dele vai para a fila) e a sessão dele fecha. A entrada espera o último save da conta chegar ao banco, carrega o personagem fora da thread do mundo e `CompleteEntry` o coloca no mundo (`Online`).
- Duas entradas concorrentes da mesma conta: a segunda é recusada e a sessão dela fecha. Se a sessão da primeira já fechou, a segunda assume, e a carga da primeira é descartada.
- **Sair** (`Leave`): o jogador sai do mundo, o snapshot vai para a fila e a conta fica em `Saving` até o flush depois dele. Uma entrada que fecha no meio da carga também deixa a conta em `Saving` até o save que ela esperava.
- O estado é por **conta**, não por personagem: o depósito da conta é compartilhado.
- Nunca se carrega do banco um personagem cujo último estado ainda está só em memória ou na fila.
- Uma transferência pendente adia o snapshot final até ela terminar. Um save que falha de vez ainda libera a conta (**PR #41: S7**).

## Transferências entre conta e personagem
Código: `CharacterTransfers` (o fluxo), `ItemService` (a API de itens), `AccountTransferRepository` (a transação). Testes: `TransferTests`, `CashShopTests`, `NpcShopTests` e `AccountTransferDatabaseTests` (contra o MariaDB).

Estado da conta (Cash, Cash/Shop Depository) é gravado direto no banco. Estado do personagem é gravado por snapshot. Mover algo entre os dois é uma **transferência**:
1. Na thread do mundo, o serviço valida tudo e aplica a mudança no personagem. `CharacterTransfers.Run` põe o jogador em `PlayerStates.Transacting` (fora de `Idle`, então nenhum handler de economia é aceito) e tira o snapshot.
2. A `PersistenceQueue` recebe um job **não coalescível** (`EnqueueCharacterTransfer`) que, numa `UnitOfWork`, trava a linha da conta, aplica a mudança da conta com recheck (posição livre; a linha retirada é exatamente a lida) e grava o snapshot. Um save coalescido do mesmo personagem que ainda esperava na fila é descartado, porque o snapshot do job é mais novo.
3. O resultado volta para a thread do mundo **também com o jogador offline**: `Committed` manda os pacotes. `Refused` (depósito cheio, linha já retirada) ou `Failed` (erro de banco) desfaz a mudança no personagem (`undo` da moeda, `Bag.Revert` do que entrou na mochila), pede save imediato e avisa o jogador. Nos dois casos, o jogador volta para `Idle`.
- Enquanto a transferência não termina, o `PlayerSystem` não grava o jogador (um snapshot com a mudança sem a parte da conta seria duplicação). Se ele sai no meio, a conta fica em `Saving` até a transferência terminar e o snapshot final gravar.
- Se o servidor para no meio, o banco fica consistente: ou a transação gravou os dois lados, ou nenhum.
- A API é do `ItemService`: `SendToAccountStorage(player, entries, undo, done)` (quem chama já aplicou o custo e passa como desfazê-lo) e `TakeFromAccountStorage(player, row, done)`. Loja NPC (Shop Depository) e Cash Depository já usam. Correio e troca vão usar a mesma API; `Wallet` entra com os serviços de domínio (**PR #41: G**).
- Operação só da conta (comprar no Cash Shop: debita Cash e põe no depósito) é uma transação de banco simples e não passa pelo personagem.

## Interação e alcance
Código: `InteractionGuard` (thread do mundo). Dados: `GameData.Npcs` (`NpcDirectory`, lido de `npc_info_us`) e `ItemDrop.OwnerCharacterUid`. Testes: `InteractionGuardTests`, e os testes de loja, Depository, tutorial, batalha e coleta posicionam o jogador.

Toda ação com alvo no mundo passa pelo `InteractionGuard`: o alvo existe, está no mapa do jogador e dentro da distância daquela interação. A distância é em casas, no maior eixo (`max(|dx|, |dy|)`). Recusa não muda nada e vai para o log como aviso, porque o cliente legítimo só manda essas ações de perto.

| Interação | Regra | Onde |
|---|---|---|
| Loja de NPC (`0x50`–`0x53`) | a loja pertence a um NPC do mapa (coluna 12 de `npc_info_us`) e ele está a até `Game:NpcInteractionRange` casas | `CanUseShop` nos 4 handlers |
| Digimon Center (`0x89`, `0x94`) | um NPC com janela `14` no mapa está perto | `CanUseDigimonCenter` |
| Escolha de diálogo (`0x56`) | o NPC da escolha (`RegisterChoice(choiceId, npcId, …)`) está perto | `NpcScriptRegistry.Choose` |
| Batalha e Taming Net | o grupo selvagem pode batalhar e está a até `Game:WildInteractionRange` casas | `BattleSystem.StartPve`, `Taming.UseNet` |
| Coleta de drop (`0x90`, `0x91`) | o item está a até `Game:DropPickupRange` casas e, nos primeiros `Zone.DropOwnerWindow` (40 s), só o dono coleta | `CanPickUp` nos 2 handlers |

- Na partida o servidor recusa subir com uma loja do `NpcShop.xml` sem NPC dono, ou com uma escolha cujo NPC não existe no mapa do script.
- Distâncias medidas nas capturas (`captures.db`, posição do jogador no último movimento antes da ação) ✅:
  - NPC, loja e Depository: até 3 casas em 21 amostras; escolhas de diálogo: até 3 em 30 amostras. Padrão `NpcInteractionRange = 5`.
  - Selvagem: 156 de 158 batalhas a até 5 casas e uma a 7 (o grupo também anda, então a margem é maior). Padrão `WildInteractionRange = 8`.
  - Coleta: 26 de 27 a até 1 casa, uma a 3. Padrão `DropPickupRange = 4`. O pedido traz a origem como (y, x) do item; o servidor usa só o `uid`.
- Janela do dono ✅/❓: o drop fica 60 s no chão e o oficial o anuncia de novo aos 40 s (`Zone.DropOwnerWindow`). O emulador libera a coleta para todos nesse momento; que o reenvio é o fim da exclusividade é inferência, sem captura com dois jogadores. Sem grupo (party) ainda, o dono é o personagem que venceu a batalha.
- ❓ Cash Depository (`0xCA`): ainda sem evidência de NPC ou de distância; não tem regra de alcance.

## Limites de recurso (**PR #41: S6**)
Configurados em `Network`, com log de cada conexão cortada:
- Conexões por IP e conexões totais.
- Prazo para autenticar depois de conectar e prazo de inatividade (sem pacote).
- Token bucket de pacotes por sessão, com teto geral e tetos por opcode onde fizer sentido (chat, movimento).
- Trabalhos pendentes na fila do mundo **por sessão**. Passou do limite: a sessão fecha. Um cliente não consegue atrasar o tick de todos.
- Fila de envio por sessão (já existe): cheia fecha a sessão.

## Persistência (**PR #41: S7**)
- Gravação que falha é repetida com backoff. Esgotadas as tentativas, o snapshot vai para um **dead-letter** (tabela ou arquivo), o log é crítico e o personagem fica bloqueado para login até a gravação ser reaplicada.
- Nada é descartado em silêncio.

## Auditoria (**PR #41: S9**)
- Toda movimentação de moeda e de item entre fontes (loja, Cash Shop, depósitos, GM, drop coletado, NPC, recompensa) gera uma linha em `economy_log`: quem, o quê, quanto, de onde, para onde, saldo depois e motivo.
- A linha é gravada **na mesma transação** da operação, para não existir movimento sem registro nem registro sem movimento.
- Comandos de GM sempre registram autor e alvo.

## Regras para código novo
- Ids do cliente (`Uid`, `Serial`, `ItemId`, slot, página) são conferidos contra o estado do servidor: pertence ao jogador, está na bolsa, mapa ou batalha esperada, e o tipo confere.
- Limites (`slot`, `row`, `col`, `amount`, página) são validados **antes** de cast para tipo sem sinal ou de uso como índice.
- Quantidade e existência são reconferidas **no momento do consumo**, não só na seleção.
- Multiplicações de preço por quantidade usam tipo largo e conferem o teto.
- Nenhum dado do cliente vai para o log sem limite de tamanho, e nenhum segredo (`auth_key`, hash) vai para o log.
- Todo controle acima tem teste com entrada hostil.
