# Como estender (receitas)

Cada tipo de mudança tem **uma receita fechada**, com a lista exata do que se toca. Se a mudança pede um arquivo fora da receita, ou a receita está incompleta (corrija esta página no mesmo PR) ou o desenho está errado (pare e discuta). Visão geral em [arquitetura](README.md).

## Pontos de registro permitidos
Estes são os **únicos** lugares em que se "liga" algo à mão. Todo o resto é achado sozinho (atributo + gerador de código) e conferido por teste de contrato.

| Ponto | Quando muda |
|---|---|
| `Opcodes` (`MapOpcode`, `ChannelOpcode`…) no `DRPG.Shared` | opcode novo: é o catálogo do protocolo |
| Raiz de composição do servidor: um `services.Add<Domínio>()` por domínio | **domínio** novo (raro). Coisa nova dentro de um domínio não muda nada aqui |
| `Scenes/` do cliente | janela nova que abre sozinha numa cena (as outras abrem por ação de outra janela) |
| `Resources/<Domínio>/*.xml` | dado de jogo |

## Item novo
**Comportamento que já existe** (cura, teleporte, bônus de status…): **zero C#**.
1. O item já vem na tabela do cliente (`item_info_us.dat`). Se a tabela for de patch novo, atualize o `table.pf`.
2. Se ele tem efeito, bônus, preço de loja ou drop, acrescente nos XML de `Resources/` usando os tipos de efeito e modificador existentes ([dados](../emulador/dados.md)).
3. Partida do servidor valida as referências. Teste de carga dos dados passa.

**Comportamento novo:**
1. Uma classe de efeito nova (`Game/Items/Effects/<Nome>Effect.cs`) com parâmetros tipados, `Validate` e `Apply`. Ela é registrada pelo **tipo**, nunca pelo id do item.
2. Teste do efeito (válido, alvo errado, sem espaço, estado errado).
3. O XML do item usa `<Effect Type="<Nome>" …/>`.
4. Se o cliente precisa mostrar algo novo, siga "pacote S2C novo".

Card, título e buff seguem a mesma receita (efeito ou `StatModifierKind` novo).

## Pacote C2S novo (cliente pede algo)
1. **Shared:** `Opcodes` + o request em `Packets/<Servidor>/<Domínio>Packets.cs` (layout, tamanho, evidência ✅/❓). Teste dourado se houver captura.
2. **Servidor:** handler em `Network/Handlers/<Servidor>/<Domínio>Handlers.cs` com a base certa e `[RequiresState]`. A regra vai num método do serviço do domínio. Teste com entrada hostil ([checklist](../emulador/handlers.md#checklist-de-handler-novo)).
3. **Cliente:** método no `<Domínio>Actions` que monta e envia o request.
4. **Wiki:** linha no [catálogo de opcodes](../protocolo/opcodes.md).

## Pacote S2C novo (servidor informa algo)
1. **Shared:** `Opcodes` + o pacote no arquivo do domínio. Ida e volta + dourado.
2. **Servidor:** o serviço do domínio monta e envia (método de fábrica a partir das entidades). Sequência que o oficial manda junta fica num método só.
3. **Cliente:** o `case` do opcode no `MapDecoder` (ou no decodificador do servidor) e um método em `<Domínio>Handlers` que atualiza `<Domínio>State`, com o `case` no `Handle(object)` do mesmo arquivo enquanto o gerador de registro não existe ([cliente](cliente.md#a-fatia-de-feature)). A janela já lê o estado.
4. **Wiki:** catálogo de opcodes.

## Janela nova (cliente)
1. `Features/<Domínio>/<Nome>Window.cs` com `[Window]`, posição original em 800×600 com evidência (`CWindow_*`, endereço) e âncora.
2. O que ela pede vai no `<Domínio>Actions`; o que ela mostra vem do `<Domínio>State`.
3. Teste de layout sem GPU.
4. Se abre sozinha numa cena, uma linha na cena. Se abre por um botão, a janela do botão chama `windows.Open<NomeWindow>()`.

## Domínio novo (ex.: guilda)
Servidor: pasta `Game/<Domínio>/` com o serviço, `services.Add<Domínio>()`, pasta de testes. Cliente: pasta `Features/<Domínio>/` com as quatro peças. Wiki: página do domínio em Mecânicas. É a **única** receita que mexe na raiz de composição.

## Formato de arquivo novo
1. Leitor em `DRPG.Shared/Formats/` (sem GPU, com conferência de limites) e teste com arquivo truncado e campos absurdos.
2. Página do formato na seção Cliente da wiki.
3. Saída no visualizador do `DRPG.Tools`.
4. Cliente: o `AssetStore` converte o tipo do leitor em recurso de GPU.

## Opção de configuração nova
Propriedade no `…Options` da seção, com o limite dela no validador (`ValidateOnStart` no servidor e equivalente no cliente). Nunca ler variável de ambiente ou arquivo de configuração fora das `Options`.

## Patch novo do oficial
1. Capturar sessões com o cliente novo pelo [proxy](../ferramentas/proxy.md); o `drpg_diff.py` lista opcodes novos e tamanhos que mudaram.
2. Gerar os dourados das capturas novas. Os testes que falham são exatamente os pacotes que mudaram.
3. Para cada um: corrigir o layout no `DRPG.Shared` (✅ com a sessão nova). Servidor e cliente quebram na compilação onde o campo mudou de sentido. Corrija os dois no mesmo PR.
4. Atualizar `table.pf` e o `extract-client`. Colunas novas ganham nome ou `UnknownColNN`. Itens novos seguem "item novo".
5. Opcodes novos seguem as receitas de C2S/S2C.
6. Marcar a nova versão alvo no [status](../projeto/status.md), registrar no [registro de decisões](../projeto/decisoes.md) e criar a tag `protocol-v<antigo>` no commit anterior.
