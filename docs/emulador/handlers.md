# Emulador: handlers de pacote

Como um pacote do cliente entra no servidor. Visão geral em [arquitetura](arquitetura.md). Os controles de segurança citados estão em [segurança](seguranca.md).

> Peças marcadas com **(PR #41: Xn)** entram no item `Xn` do checklist do PR de refatoração da fundação.

## Caminho de um pacote
1. `Session` corta o fluxo em quadros (`PacketFramer`: assinatura e tamanho máximo). Quadro inválido fecha a conexão.
2. Limite de frequência da sessão (token bucket). Estourou: a conexão fecha (**PR #41: S6**).
3. `PacketDispatcher` acha o handler do (servidor, opcode).
4. `ClientPacket.Parse<T>` confere o **tamanho declarado** do request, lê o tipo (`IClientPacket<T>.Read`) e exige que o `Read` consuma o payload inteiro. Tamanho errado, byte faltando ou sobrando: `PacketFormatException`, e a sessão **fecha** (pacote malformado não vem de cliente legítimo).
5. Handler de sessão roda no laço da sessão. Handler de mundo faz o parse no laço da sessão (`IWorldPacketHandler.Prepare`) e só a ação já tipada é postada na thread do mundo; pacote malformado nunca chega ao mundo. A ação conta no limite de trabalhos pendentes da sessão (**PR #41: S6**).
6. A base do handler aplica as pré-condições declaradas (autenticado, jogador no mundo, estado) antes de chamar o método do handler.
7. O handler chama **um** serviço e envia a resposta.

## Request tipado (C2S)
Todo C2S tratado tem um tipo em `Network/Packets/<Servidor>/`, ao lado dos S2C do mesmo domínio. O tipo descreve o layout e só ele lê bytes:

```csharp
/// <summary>✅ 0x91 (C2S 250 B, v322): mover item.</summary>
public sealed record ItemMoveRequest(ItemMoveHeader Header, uint Uid, uint ItemId) : IClientPacket<ItemMoveRequest>
{
    // payload sem o cabeçalho de 14 B: ItemMoveHeader (20) + ItemRecord (108) + ❓ segundo registro (108) + 14 finais
    public static PacketLength Length => PacketLength.Exact(ItemMoveHeader.Length + 2 * ItemRecord.Length + 14);

    public static ItemMoveRequest Read(PacketReader r)
    {
        var header = ItemMoveHeader.Read(r);
        var uid = r.ReadU32();
        var itemId = r.ReadU32();
        r.Skip(ItemRecord.Length - 8);                              // resto do registro, não usado
        r.Skip(ItemRecord.Length);                                  // ❓ segundo registro, não usado
        r.Skip(14);
        return new(header, uid, itemId);
    }
}
```

- `PacketLength.Exact(n)` quando o cliente sempre manda o mesmo tamanho (✅ nas capturas ou no binário), e `PacketLength.AtLeast(n)` só quando o tamanho varia de verdade ou ainda não foi capturado (❓ no comentário).
- Campos não usados são pulados **dentro do `Read`**, com um comentário do que são. O handler nunca vê offset.
- Payload sem campo útil ainda tem request (lê/pula tudo); `IgnoredPayload` serve aos opcodes que só são aceitos.
- Enums vindos do cliente entram como cast direto no request e são conferidos pelo handler ou serviço (`switch` fechado, `default` ignora ou responde erro). Valor desconhecido **não** é pacote malformado: alguns conjuntos (ex.: `BattleIcon`) ainda estão incompletos e derrubar a sessão por isso puniria cliente legítimo.
- `ToString` do request esconde segredo (chave de autenticação, hash de senha).

## Handlers
| Base | Roda em | Uso |
|---|---|---|
| `SessionHandler<TRequest>` | laço da sessão (async) | antes da autenticação (handshake, `0x02` do channel, `0x49`/`0x01` do mapa) e o que não depende de conta |
| `AccountHandler<TRequest>` | laço da sessão (async) | precisa de conta autenticada (lista/criação de personagem, enciclopédia); sem conta, fecha a sessão |
| `PlayerHandler<TRequest>` | thread do mundo (síncrono) | **todo** pacote de jogo no mapa |

```csharp
[PacketHandler(MapOpcode.ItemDropGet)]
[RequiresState(PlayerStates.Idle)]
public sealed class ItemMoveHandler(ItemService items) : PlayerHandler<ItemMoveRequest>
{
    protected override void Handle(Player player, ItemMoveRequest request) => items.Move(player, request);
}
```

- Descobertos por reflexão e registrados no DI. `HandlerContractTests` garante que todo handler usa uma base tipada, que todo `PlayerHandler` declara `[RequiresState]` e que todo request lê exatamente o tamanho declarado.
- **Pré-condições declarativas**, aplicadas pela base `PlayerHandler<T>`:
  - `[RequiresState]` é obrigatório: sem ele o handler falha ao ser construído (na partida do servidor).
  - `PlayerStates.Idle`: fora de batalha. É o padrão de quase todo pacote de mapa. A transação pendente entra como estado próprio com as transferências (**PR #41: S3**).
  - `PlayerStates.InBattle`: ações de batalha. `PlayerStates.Any`: consultas que valem nos dois (janelas de informação, renomear, Cash Shop).
  - A distância até o NPC, selvagem ou drop é conferida pelo serviço via `InteractionGuard` (**PR #41: S4**), porque o alvo só é conhecido depois de ler o request.
- Ganchos da base, para os casos com resposta ✅ conhecida:
  - `OnRejected(player, request)`: estado errado. Padrão: não responde nada (o cliente oficial não espera resposta). Ex.: `ItemUseHandler` responde `0xC3` em batalha.
  - `OnNotInWorld(session, request)`: pacote de jogo antes de o personagem entrar no mundo. Padrão: fecha a sessão. Ex.: `RenameHandler` responde "indisponível", porque o cliente manda o `0xF0` tipo 1 logo na entrada.
- Opcode sem handler: log `Info` na primeira vez e `Debug` depois. Admin recebe um eco de debug no chat.

## I/O a partir de um handler de mundo
Handler de mundo é síncrono e **nunca espera** o banco: o serviço valida na thread do mundo, agenda o I/O no
`WorldIo` e retorna na hora. O tick segue enquanto o banco responde.

```csharp
io.Run(player, "cash-shop.open",
    work: ct => store.GetBalanceAsync(accountUid, ct),            // pool de threads, fora do mundo
    then: balance => player.Send(new CashShopOpenPacket(...)),    // de volta na thread do mundo, só se online
    failed: _ => player.Send(new CashShopOpenPacket(error: 1)));  // erro já registrado no log
```

- `work` roda no pool de threads (`Task.Run`), inclusive a parte síncrona do driver. Ele não toca em estado de jogo: copie para variáveis locais o que precisar (uid da conta, quantidade) antes de agendar.
- `then` e `failed` rodam na thread do mundo e só se o jogador continuar online.
- `discarded` recebe o resultado quando o jogador saiu antes de a continuação rodar. É onde se desfaz o que já foi feito na conta (ex.: `CashShopService.WithdrawDepository` devolve o item ao Cash Depository).
- `io.Run(operation, work)` sem jogador serve para compensações e tarefas do servidor. A falha é registrada como erro, então o nome da operação diz o que ficou por fazer.
- O `WorldIo` registra a exceção com o contexto (operação, jogador), inclusive a da continuação. O serviço não escreve `try/catch` para isso.
- Na parada, o `WorldIo` para antes do mundo e espera o I/O pendente e as continuações dele (inclusive o I/O que elas iniciarem). Só depois o mundo salva os jogadores. Estourou o tempo de parada: o token do `work` é cancelado.
- Nos testes, `WorldHarness` (`TestSupport`) sobe o mundo e o `WorldIo`. `RunAsync` roda na thread do mundo e espera o I/O.
- Mudança de estado do personagem que depende do banco segue o fluxo de [transferência](seguranca.md#transferências-entre-conta-e-personagem) (**PR #41: S3**), nunca um callback solto.

## Respostas (S2C)
- Um `ServerPacket` por S2C, em `Network/Packets/<Servidor>/`. O pacote só serializa o que recebe, sem regra.
- Sequências que o oficial manda juntas (entrada no mapa, efeitos de equipamento) ficam num método só do serviço do domínio e são chamadas de todos os lugares que precisam delas (**PR #41: G5, G7**).
- `Templates/*.hex` guarda payloads capturados ainda não decodificados.

## Checklist de handler novo
- [ ] Request tipado com tamanho declarado e ✅/❓ da evidência.
- [ ] Pré-condição de estado declarada.
- [ ] Ids do cliente conferidos contra o estado do servidor (dono, bolsa, mapa, batalha).
- [ ] Distância/contexto pelo `InteractionGuard` quando há alvo no mundo.
- [ ] Regra no serviço do domínio, reaproveitando `ItemService`, `Wallet` e `EquipmentService`.
- [ ] Movimento de moeda ou item registrado no `IAuditLog`.
- [ ] Testes com entrada inválida: tamanho errado, id trocado, índice fora do intervalo, estado errado, longe do alvo.
  Nos testes, `handler.Dispatch(session, payload)` (ou `DispatchAsync`) segue o mesmo caminho do dispatcher e completa com zeros o payload curto.
