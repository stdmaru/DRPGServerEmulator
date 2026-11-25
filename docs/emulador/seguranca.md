# Emulador: segurança

Modelo de ameaça e controles do servidor. Vale desde já, mesmo com o jogo incompleto: **feature faltando não é motivo para controle faltando**. Visão geral em [arquitetura](arquitetura.md). O caminho de um pacote está em [handlers](handlers.md).

> Peças marcadas com **(PR #41: Xn)** entram no item `Xn` do checklist do PR de refatoração da fundação.

## Modelo de ameaça
- O cliente é **hostil**: qualquer byte pode ser forjado, repetido, truncado, enviado fora de ordem ou em volume.
- O atacante conhece o protocolo (o cliente é público), conhece nomes de conta e de personagem (aparecem no jogo) e pode abrir conexões direto em qualquer uma das 4 portas.
- O alvo principal é a **economia**: duplicar item, moeda ou digimon, ou obter algo sem pagar. Depois vêm roubo de conta e derrubada do servidor.
- O banco pode vazar: nada que permita entrar numa conta fica em claro.

## Autenticação entre servidores (**PR #41: S1**)
- O channel autentica (usuário + `auth_key`) e emite um **ticket** da conta com: IP da conexão do channel, validade (`Network:AccountTicketSeconds`) e uma vaga por servidor (mapa e global).
- Mapa e global só aceitam a conta se houver ticket válido, **do mesmo IP**, e se a vaga daquele servidor estiver livre. O username do pacote só serve para achar o ticket, nunca para autenticar.
- A vaga é liberada quando a sessão daquele servidor fecha. Enquanto a conta tiver alguma sessão aberta, o ticket é renovado. Depois que a última sessão fecha, ele expira na validade.
- Um segundo login no channel invalida o ticket anterior e derruba as sessões dele.
- `auth_key` é guardada como hash PBKDF2-SHA256 com sal, e a comparação é em tempo constante (**PR #41: S8**).

## Entrada e saída do mundo (**PR #41: S2**)
Cada conta tem um estado no `PlayerSystem`: `Offline → Loading → Online → Saving → Offline`.
- **Entrar:** só a partir de `Offline`. Uma entrada com a conta em outro estado tira do mundo o jogador antigo, espera o save dele chegar ao banco e só então carrega. Duas entradas concorrentes da mesma conta: a segunda é recusada.
- **Sair:** o jogador sai do mundo, o snapshot vai para a fila e a conta fica em `Saving` até a gravação confirmar. Uma transferência pendente adia o snapshot final até ela terminar.
- Nunca se carrega do banco um personagem cujo último estado ainda está só em memória.

## Transferências entre conta e personagem (**PR #41: S3**)
Estado da conta (Cash, Cash/Shop Depository) é gravado direto no banco. Estado do personagem é gravado por snapshot. Mover algo entre os dois é uma **transferência**:
1. Na thread do mundo, o serviço valida tudo, aplica a mudança no personagem, coloca o jogador em `PlayerState.Transacting` (fora de `Idle`, então nenhuma outra ação de economia é aceita) e tira o snapshot.
2. A `PersistenceQueue` recebe um job **não coalescível** que, numa transação só, aplica a mudança da conta (com `FOR UPDATE` e recheck de capacidade ou existência) e grava o snapshot. Saves pendentes do mesmo personagem que estavam antes dele são descartados, porque o snapshot do job é mais novo.
3. Se der certo, o mundo confirma e manda os pacotes. Se falhar, o mundo desfaz a mudança no personagem, grava de novo e avisa o jogador. Nos dois casos, o jogador volta para `Idle`.
- A API é do `ItemService` (`SendToAccountStorage`, `TakeFromAccountStorage`) e de `Wallet`. Loja NPC, Cash Depository, correio e troca usam a mesma API.
- Operação só da conta (comprar no Cash Shop: debita Cash e põe no depósito) é uma transação de banco simples e não passa pelo personagem.

## Interação e alcance (**PR #41: S4, S5**)
- Toda ação com alvo no mundo passa pelo `InteractionGuard`: o alvo existe, está no mesmo mapa e está dentro da distância daquela interação.
  - NPC (loja, Depository, escolhas): a loja ou escolha pertence a um NPC, e o jogador está perto dele.
  - Selvagem (batalha, Taming Net): perto do grupo e o grupo pode batalhar.
  - Drop: perto e, durante a janela de exclusividade, só o dono coleta.
- As distâncias saem das capturas (✅). Enquanto não houver evidência, o valor é configurável e marcado ❓.

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
