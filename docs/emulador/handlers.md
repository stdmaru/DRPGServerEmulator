# Emulador: handlers de pacote

Como um pacote do cliente entra no servidor. Visão geral em [arquitetura](arquitetura.md). Os controles de segurança citados estão em [segurança](seguranca.md).

> Peças marcadas com **(PR #41: Xn)** entram no item `Xn` do checklist do PR de refatoração da fundação.

## Caminho de um pacote
1. `Session` corta o fluxo em quadros (`PacketFramer`: assinatura e tamanho máximo). Quadro inválido fecha a conexão.
2. Limite de frequência da sessão (token bucket). Estourou: a conexão fecha (**PR #41: S6**).
3. `PacketDispatcher` acha o handler do (servidor, opcode) e confere o **tamanho declarado** do request. Tamanho errado conta como pacote malformado (**PR #41: N1**).
4. O request é lido para um tipo (`IClientPacket<T>.Read`). Faltou byte: `PacketFormatException`, com log de aviso.
5. Handler de sessão roda no laço da sessão. Handler de mundo é postado na thread do mundo, contando no limite de trabalhos pendentes da sessão (**PR #41: S6**).
6. A base do handler aplica as pré-condições declaradas (jogador no mundo, estado, interação) antes de chamar o método do handler (**PR #41: N2**).
7. O handler chama **um** serviço e envia a resposta.

## Request tipado (C2S) (**PR #41: N1**)
Todo C2S tratado tem um tipo em `Network/Packets/<Servidor>/`, ao lado dos S2C do mesmo domínio. O tipo descreve o layout e só ele lê bytes:

```csharp
/// <summary>✅ 0x91 (C2S 250 B): mover item. conn=437.</summary>
public sealed record ItemMoveRequest(ItemMoveHeader Header, uint Uid, uint ItemId) : IClientPacket<ItemMoveRequest>
{
    // payload sem o cabeçalho de 14 B: ItemMoveHeader (20) + ItemRecord (108) + 108
    public static PacketLength Length => PacketLength.Exact(ItemMoveHeader.Length + 2 * ItemRecord.Length);

    public static ItemMoveRequest Read(PacketReader r)
    {
        var header = ItemMoveHeader.Read(r);
        var uid = r.ReadU32();
        var itemId = r.ReadU32();
        r.Skip(ItemRecord.Length - 8);                              // resto do registro, não usado
        r.Skip(ItemRecord.Length);                                  // ❓ segundo registro, não usado
        return new(header, uid, itemId);
    }
}
```

- `PacketLength.Exact(n)` quando o cliente sempre manda o mesmo tamanho (✅ nas capturas), e `PacketLength.AtLeast(n)` só quando o tamanho varia de verdade.
- Campos não usados são pulados **dentro do `Read`**, com um comentário do que são. O handler nunca vê offset.
- Enums vindos do cliente são validados com `Enum.IsDefined` ou um `switch` fechado. Valor desconhecido = request inválido.

## Handlers
| Base | Servidor | Roda em | Uso |
|---|---|---|---|
| `SessionHandler<TRequest>` | login, channel, global | laço da sessão (async) | autenticação, lista de personagens, consultas que não tocam o mundo |
| `PlayerHandler<TRequest>` | mapa | thread do mundo (síncrono) | **todo** pacote de jogo no mapa; fecha a sessão se o jogador ainda não entrou |
| `MapEntryHandler<TRequest>` | mapa | laço da sessão | só o que vem antes de o jogador entrar no mundo (`0x49`, `0x01`) |

```csharp
[PacketHandler(MapOpcode.ItemDropGet)]
[RequiresState(PlayerState.Idle)]
public sealed class ItemMoveHandler(ItemService items) : PlayerHandler<ItemMoveRequest>
{
    protected override void Handle(Player player, ItemMoveRequest request) => items.Move(player, request);
}
```

- Descobertos por reflexão e registrados no DI. Um teste garante que não há opcode duplicado e que todo handler declara o tamanho do request.
- **Pré-condições declarativas** (**PR #41: N2**), aplicadas pela base:
  - `[RequiresState(PlayerState.Idle)]`: fora de batalha e sem transação pendente. É o padrão de quase todo pacote de mapa.
  - `[RequiresState(PlayerState.InBattle)]`: ações de batalha.
  - A distância até o NPC, selvagem ou drop é conferida pelo serviço via `InteractionGuard` (**PR #41: S4**), porque o alvo só é conhecido depois de ler o request.
- Pré-condição que falha não responde nada (o cliente oficial não espera resposta), a não ser que haja um código de erro ✅ capturado para aquele caso.
- Opcode sem handler: log `Info` na primeira vez e `Debug` depois. Admin recebe um eco de debug no chat.

## I/O a partir de um handler de mundo (**PR #41: N3**)
Handler de mundo é síncrono. Quando o serviço precisa do banco, ele usa `WorldIo`:

```csharp
io.Run(player,
    work: ct => store.GetBalanceAsync(player.Account.Uid, ct),     // fora da thread do mundo
    then: balance => player.Send(new CashShopOpenPacket(...)),     // de volta na thread do mundo, só se online
    failed: _ => player.Send(new CashShopOpenPacket(error: 1)));   // erro já registrado no log
```

- `then` e `failed` rodam na thread do mundo e só se o jogador continuar online.
- O `WorldIo` registra a exceção com o contexto (jogador, operação). O serviço não escreve `try/catch` para isso.
- Mudança de estado do personagem que depende do banco segue o fluxo de [transferência](seguranca.md#transferências-entre-conta-e-personagem), nunca um callback solto.

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
