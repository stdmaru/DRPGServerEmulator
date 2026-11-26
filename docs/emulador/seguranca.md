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

### `auth_key`
Código: `Game/Accounts/AuthKeyHash.cs` e `AccountAuthenticator.cs`. Testes: `AuthKeyTests`.
- Guardada em `account.auth_key_hash` (migração `0013`) no formato `pbkdf2-sha256$<iterações>$<sal base64>$<hash base64>`: sal de 16 bytes, hash de 32, 100 000 iterações. A chave é um token aleatório emitido a cada login, não uma senha escolhida; mais iterações só encareceriam cada tentativa de login.
- O launcher continua mandando a chave em claro no `0x04`. A comparação é sempre em tempo constante, e usuário inexistente, chave vazia ou conta sem chave gastam o mesmo tempo de um hash conferido.
- **Compatibilidade com o emissor:** quem emite a chave (site, launcher, `INSERT` de teste) pode continuar gravando em `account.auth_key` em texto puro. Essa chave é a emissão mais nova e vale no lugar do hash antigo; no primeiro login com ela, ou na próxima partida do servidor, ela vira hash e a coluna `auth_key` fica `NULL`. A conversão só grava se a chave ainda for a mesma (o emissor pode ter trocado nesse meio-tempo).
- O emissor também pode gravar o hash direto, por exemplo em Python: `hashlib.pbkdf2_hmac('sha256', chave.encode(), sal, 100000, 32)`, com o resultado montado no formato acima.
- Nenhuma chave nem hash vai para o log.
- ❓ A coluna `account.password` não é usada pelo servidor e continua como o emissor a grava.

## Entrada e saída do mundo
Código: `PlayerSystem` (estado da conta, thread do mundo) e `WorldEntry` (o fluxo do `0x01`). Testes: `WorldEntryTests`.

Cada conta tem um estado no `PlayerSystem`: `Offline → Loading → Online → Saving → Offline`.
- **Entrar** (`WorldEntry.EnterAsync`): `BeginEntry` na thread do mundo põe a conta em `Loading`. Se a conta está `Online`, o jogador antigo sai do mundo (o save dele vai para a fila) e a sessão dele fecha. A entrada espera o último save da conta chegar ao banco, carrega o personagem fora da thread do mundo e `CompleteEntry` o coloca no mundo (`Online`).
- Duas entradas concorrentes da mesma conta: a segunda é recusada e a sessão dela fecha. Se a sessão da primeira já fechou, a segunda assume, e a carga da primeira é descartada.
- **Sair** (`Leave`): o jogador sai do mundo, o snapshot vai para a fila e a conta fica em `Saving` até o flush depois dele. Uma entrada que fecha no meio da carga também deixa a conta em `Saving` até o save que ela esperava.
- O estado é por **conta**, não por personagem: o depósito da conta é compartilhado.
- Nunca se carrega do banco um personagem cujo último estado ainda está só em memória ou na fila.
- Uma transferência pendente adia o snapshot final até ela terminar. Um save que falha de vez ainda libera a conta: o snapshot vai para o [dead-letter](#persistência), e o personagem fica bloqueado até ele ser gravado.

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

## Limites de recurso
Código em `Network/Traffic/`; opções na seção `Network`, validadas na partida. Toda conexão recusada e toda sessão cortada vai para o log em `Warning` (conexão recusada: um log por IP por minuto, para quem insiste não encher o log).

| Limite | Padrão | Passou | Evidência ✅ |
|---|---|---|---|
| Conexões abertas, total (`MaxConnections`) | 4000 | conexão recusada | n/a |
| Conexões abertas por IP, somando os 4 servidores (`MaxConnectionsPerIp`) | 16 | conexão recusada | um cliente em jogo mantém 3 (channel, mapa, global); o login fecha logo |
| Prazo para autenticar (`AuthTimeoutSeconds`) | 30 s | sessão fecha | o primeiro pacote chega em até 0,3 s. No login, que não autentica a sessão, é o tempo máximo de conexão |
| Inatividade no login, channel e global (`IdleTimeoutSeconds`, mín. 200) | 600 s | sessão fecha | keepalive `0xC4` do cliente a cada 165–168 s (42 intervalos) |
| Inatividade no mapa (`MapIdleTimeoutSeconds`, mín. 15) | 60 s | sessão fecha | o servidor manda `0xC4` a cada ~5 s e o cliente responde: 3,7–6,6 s entre respostas (5 083 intervalos); maior silêncio no mapa 5,7 s, incluindo a carga |
| Pacotes por sessão (`Packets`, balde de fichas) | 20/s, rajada 60 | sessão fecha | pico de 7 em 1 s, 40 em 10 s e 195 em 60 s |
| Por opcode (`OpcodeLimits:<Servidor>:<Opcode>`) | `Map:ChatNormal` 1/s, rajada 5; `Map:MoveCharacter` 10/s, rajada 20 | **pacote ignorado**, sessão continua | movimento: pico de 2 em 1 s e 12 em 10 s; chat: só 2 amostras |
| Trabalhos da sessão esperando a thread do mundo (`MaxPendingWorldWork`) | 64 | sessão fecha | n/a: um cliente não consegue atrasar o tick de todos |
| Fila de envio por sessão | 4096 quadros | sessão fecha | n/a |

- Ordem no `PacketDispatcher`: o quadro passa primeiro pelos limites (`SessionTraffic.Admit`, que também renova o prazo de inatividade) e só depois vai ao handler. Pacote de opcode sem handler também gasta ficha.
- O prazo de autenticar conta da conexão: mandar pacotes não o adia. "Autenticada" é a sessão com `Account` (channel `0x04`, mapa `0x01`, global `0x64`).
- `NetworkHost` confere os prazos de todas as sessões a cada segundo.
- Opcode do `OpcodeLimits` é o **nome** do enum do servidor (`MapOpcode.ChatNormal`); nome desconhecido, número ou opcode de outro servidor derruba a partida. A configuração troca um opcode e mantém os outros padrões.
- ❓ O teto do chat é escolha de projeto (há só 2 amostras de chat nas capturas).

## Persistência
Código em `Data/Persistence/` e `Game/World/DeadLetterReplay.cs`; opções na seção `Persistence`. Nada é descartado em silêncio.

- **Repetição:** cada trabalho da fila (`PersistenceJob`) tem até `MaxAttempts` (5) tentativas, com espera dobrando de `RetryBaseMilliseconds` (1 s) até `RetryMaxMilliseconds` (30 s).
- **Queda:** depois de uma desistência, cada trabalho tem **uma** tentativa até uma gravação dar certo. Com o banco fora, a fila não fica parada minutos por trabalho, e os snapshots vão logo para o dead-letter. Na parada do servidor também é uma tentativa só.
- **Dead-letter:** esgotadas as tentativas, o snapshot vai para `DeadLetterPath/character-<uid>.json` (arquivo, porque a causa comum é o banco fora), com data, trabalho e erro. Log crítico. Um arquivo por personagem, com o snapshot mais novo que falhou: cada snapshot é o estado inteiro. Se nem o arquivo puder ser gravado, o snapshot inteiro vai para o log crítico. `DeadLetterPath` é relativo à pasta de trabalho do processo, não à do executável, para sobreviver a um novo build ou deploy.
- **Bloqueio:** personagem com arquivo no dead-letter não entra no mapa (`WorldEntry`). Quem já está no mundo continua: o estado em memória é o mais novo, e o próximo save que der certo resolve.
- **Resolução:** qualquer gravação mais nova do snapshot do personagem que chega ao banco apaga o arquivo e desbloqueia: save comum, transferência conta ↔ personagem confirmada (o snapshot dela é mais novo; reaplicar o antigo depois desfaria a parte do personagem) ou a reaplicação.
- **Reaplicação:** na partida e a cada `ReplaySeconds` (60 s), o `DeadLetterReplay` enfileira os personagens bloqueados e **offline**. Passa pela fila, atrás de qualquer save mais novo, e lê o arquivo só na hora de gravar.
- **Partida:** a pasta precisa ser gravável, e todo arquivo precisa ser lido (versão, JSON, nome = uid do snapshot). Senão o servidor não sobe: arquivo ilegível é corrigido ou movido à mão.
- Transferência conta ↔ personagem tem uma tentativa só e nunca vai para o dead-letter: a falha é desfeita no personagem, e o rollback é gravado como save comum.
- ❓ Apagar um arquivo à mão desbloqueia o personagem com o estado do banco, perdendo o que estava no arquivo. Só faça isso de propósito.

## Auditoria
Código em `Game/Economy/` e `Data/Repositories/EconomyLogRepository.cs`; tabela `economy_log` (`sql/upgrade/0014_economy_log.sql`).

- **O que gera linha:** moedas (Bits, Medals, Coins, Ladder), Cash e bônus, e itens e cards que entram ou saem por uma fonte: loja NPC (compra, venda, reembolso), Shop Depository, Cash Shop, Cash Depository, comando de GM, drop coletado, escolha de NPC (prêmio e troca), recompensa de batalha e taxa do depósito de Digimon.
- **Linha:** id UUIDv7, data, conta, personagem (nulo quando o movimento é do depósito da conta), fonte, ação, ativo, id do item, quantidade com sinal (positivo entrou, negativo saiu), saldo depois (moedas e Cash) e detalhe curto (até 255). Uma transferência tem as duas pontas: retirar do Cash Depository gera −1 na conta e +1 no personagem.
- **Quantidade real:** registra o que mudou de fato. Crédito cortado pelo teto registra só o que entrou; zero e gasto recusado não registram.
- **Mesma transação:** o personagem acumula as linhas (`Character.Economy`) e o snapshot as leva. `CharacterRepository.SaveAsync` grava as linhas na transação do snapshot, então não existe movimento gravado sem linha, nem linha sem movimento. Depois do commit, a thread do mundo tira do personagem as linhas que foram gravadas.
- **Sem duplicar nem perder:** o insert é `INSERT IGNORE` pelo id. Um snapshot coalescido, uma confirmação perdida ou um dead-letter reaplicado gravam as mesmas linhas de novo sem duplicar. Save que falha mantém as linhas para o próximo snapshot e para o arquivo do dead-letter.
- **Transferência:** as linhas da conta vão só no snapshot da transferência. Se ela for recusada (depósito cheio), elas somem, e o gasto e o reembolso do personagem ficam para o próximo save.
- **Só conta:** compra no Cash Shop e `/cash` gravam as linhas na transação do `CashRepository`.
- **GM:** `/cash` e `/currency` registram o autor no detalhe (`/cash por <conta>`).
- ❓ Consumo (usar item, card em batalha, item que expira) não é auditado: não troca de dono, e o volume seria alto. Fica para depois, se for preciso investigar.
- ❓ A ordem das linhas é a coluna `at` (milissegundo). Dentro do mesmo milissegundo, o id não segue a ordem de inserção.

## Regras para código novo
- Ids do cliente (`Uid`, `Serial`, `ItemId`, slot, página) são conferidos contra o estado do servidor: pertence ao jogador, está na bolsa, mapa ou batalha esperada, e o tipo confere.
- Limites (`slot`, `row`, `col`, `amount`, página) são validados **antes** de cast para tipo sem sinal ou de uso como índice.
- Quantidade e existência são reconferidas **no momento do consumo**, não só na seleção.
- Multiplicações de preço por quantidade usam tipo largo e conferem o teto.
- Nenhum dado do cliente vai para o log sem limite de tamanho, e nenhum segredo (`auth_key`, hash) vai para o log.
- Todo controle acima tem teste com entrada hostil.
