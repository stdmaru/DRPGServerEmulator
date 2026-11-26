# Biblioteca compartilhada (`DRPG.Shared`)

O que servidor, cliente e ferramentas têm em comum: **protocolo**, **pacotes**, **formatos de arquivo** e **tabelas do cliente**. Não tem regra de jogo, rede, banco nem gráficos: só bytes ↔ tipos. Visão geral em [arquitetura](README.md).

**Hoje** o Shared tem `Protocol/` (framing, CRC, leitor/escritor, `Launcher/`, `LoginChannelOpcodes`), `Packets/Login` e `Packets/Channel` (de mão dupla, `…Request`/`…Reply`, com teste de ida e volta e dos layouts das capturas), `Formats/Pack` (`.hf/.pf`) e `Formats/Sprite` (`.spr`). Os pacotes do mapa e do global, e as tabelas, continuam no servidor até o cliente precisar deles. Só entra aqui o que mais de um projeto usa ([regra](README.md#repositório)).

## Pastas
```
DRPG.Shared/
  Protocol/        PacketHeader, Crc16, PacketFramer, PacketReader/Writer, PacketLength, Opcodes por servidor
  Packets/<Servidor>/<Domínio>Packets.cs    S2C e C2S do domínio, lado a lado
  Formats/         PackFile (.hf/.pf), Spr, Ani, Til, Obj, Map, Tables (.dat/.bin)
  Tables/          ClientTables: linhas tipadas com colunas nomeadas
```

## Pacotes de mão dupla
Cada pacote é definido **uma vez** e sabe ler e escrever. O servidor escreve S2C e lê C2S, o cliente faz o contrário, e o teste de ida e volta garante que os dois lados concordam.

```csharp
/// <summary>✅ 0x91 (C2S, 250 B, v322): mover item. Evidência: sessões 53 e 56.</summary>
public sealed record ItemMoveRequest(ItemMoveHeader Header, uint Uid, uint ItemId) : IPacket<ItemMoveRequest>
{
    public static ushort Opcode => MapOpcode.ItemMove;
    public static PacketLength Length => PacketLength.Exact(ItemMoveHeader.Length + 2 * ItemRecord.Length + 14);

    public static ItemMoveRequest Read(PacketReader r)
    {
        var header = ItemMoveHeader.Read(r);
        var uid = r.ReadU32();
        var itemId = r.ReadU32();
        r.Skip(ItemRecord.Length - 8);   // resto do registro, não usado
        r.Skip(ItemRecord.Length);       // ❓ segundo registro, zerado nas capturas
        r.Skip(14);
        return new(header, uid, itemId);
    }

    public void Write(PacketWriter w)
    {
        Header.Write(w);
        w.WriteU32(Uid);
        w.WriteU32(ItemId);
        w.Zero(ItemRecord.Length - 8);
        w.Zero(ItemRecord.Length);
        w.Zero(14);
    }
}
```

Regras:
- **Opcode, tamanho e evidência** ficam no próprio tipo. Não existe tabela separada "opcode → tamanho" para manter.
- `Read` consome o payload **inteiro**. Sobrou ou faltou byte: `PacketFormatException`.
- **Contagens e tamanhos lidos do pacote** (listas, strings) são limitados no `Read` (`r.ReadCount(max: 24)`), antes de alocar. É isso que protege o cliente de um servidor malicioso e o servidor de um cliente malicioso.
- Strings de tamanho fixo com `\0` usam `ReadFixedString(length)` / `WriteFixedString(value, length)`, com codificação declarada.
- O pacote é um **record imutável** com campos de protocolo. Ele não recebe `Character` nem `Digimon`: o servidor monta o pacote a partir das entidades num método de fábrica do lado dele (`ItemPackets.From(inventory)`), e o cliente converte o pacote no seu estado.
- Campo desconhecido não some: vira `UnknownNN` e é escrito de volta como foi lido, para o teste de ida e volta não perder nada.

## Testes obrigatórios por pacote
1. **Ida e volta:** `Read(Write(x)) == x`. Um teste genérico percorre todos os tipos `IPacket<T>` por reflexão com valores de exemplo de cada um.
2. **Dourado:** quando há captura, o payload capturado fica em `tests/DRPG.Shared.Tests/Golden/<servidor>/<opcode>_<nome>.hex` (gerado por ferramenta a partir do `captures.db`) e o teste exige `Write(Read(captura)) == captura`.
3. **Hostil:** truncado, contagem acima do máximo, string sem `\0`.

## Formatos de arquivo
- Um leitor por formato em `Formats/`, que devolve tipos simples (`SpriteSheet`, `Animation`, `MapLayers`) **sem GPU**. O cliente transforma isso em textura; as ferramentas, em PNG.
- O leitor recebe `ReadOnlySpan<byte>` ou `Stream` com tamanho conhecido e confere **todo** offset e contagem contra o tamanho do arquivo antes de usar.
- Cada formato tem página na wiki (seção Cliente) com o layout e os ✅/❓, como os pacotes.

## Versões do oficial
O alvo é **um** protocolo por vez (hoje v322). Não há `if (version >= 323)` espalhado pelo código.
- Quando o oficial atualizar: o proxy detecta as mudanças ([proxy](../ferramentas/proxy.md)) → os testes dourados com as capturas novas mostram exatamente quais pacotes mudaram → muda-se o layout no tipo do pacote → servidor e cliente compilam ou quebram juntos.
- A versão anterior fica no histórico do git (tag `protocol-v322`), não em código paralelo.
- Receita completa em [como estender](extensao.md#patch-novo-do-oficial).
