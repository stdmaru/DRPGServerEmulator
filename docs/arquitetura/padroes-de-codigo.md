# Padrões de código

Como se escreve C# neste projeto, no servidor, no cliente, no `DRPG.Shared` e nas ferramentas. A meta é que o código pareça escrito por **uma pessoa só**. Visão geral em [arquitetura](README.md).

## Idioma e nomes
- **Tudo em inglês dentro do código** (decisão de 2026-11-26): identificadores (`ItemService`, `MoveRequest`), comentários, logs e mensagens de exceção. Vale para todo código novo (cliente, `DRPG.Shared`, ferramentas, servidor). Comentário em português que já existe no servidor é traduzido quando o arquivo for tocado. A **wiki** continua em português. Logs curtos e estruturados.
- Sufixos fixos. Cada um diz o papel do tipo:

| Sufixo | Papel | Lado |
|---|---|---|
| `…Packet` | S2C (servidor → cliente) | Shared |
| `…Request` | C2S (cliente → servidor) | Shared |
| `…Handler` | recebe um pacote tipado | os dois |
| `…Service` | regras e orquestração de um domínio | servidor |
| `…Formulas` | funções puras de cálculo | servidor |
| `…Repository` | acesso ao banco | servidor |
| `…Reader` / `…Writer` | formato de arquivo ou de bytes | Shared |
| `…State` | estado espelhado de uma feature | cliente |
| `…Window` / `…Widget` | tela e componente de UI | cliente |
| `…Result` | enum ou record de resultado esperado | os dois |
| `…Options` | configuração validada na partida | os dois |

- Nada de `Manager`, `Helper`, `Utils`, `Misc`, `Common`: são nomes que aceitam qualquer coisa e viram depósito. O nome diz o domínio.
- Nome por extenso: `targetSlot`, não `ts`. Abreviação só as do jogo (`Hp`, `Vp`, `Evp`, `Exp`, `Br`, `Uid`).
- Coluna de tabela do cliente tem nome de domínio. A desconhecida é `UnknownColNN`.

## Tamanho e forma
- **Um tipo principal por arquivo**, e o arquivo tem o nome dele. Exceções: os pacotes de um domínio (`ItemPackets.cs`) e os records/enums pequenos que só existem para o tipo principal.
- **Sem limite de linhas por arquivo; o critério é uma responsabilidade por arquivo.** Um arquivo pode ser longo se tudo nele serve a uma coisa só. Quando aparece uma segunda responsabilidade, ela sai para o seu próprio tipo (ex.: `Battle` → fluxo, alvos, recompensa). Método até ~40 linhas.
- **Herança no máximo um nível** abaixo de uma base do projeto (`PlayerHandler<T>`, `ServerPacket`, `Window`). Para reaproveitar código, prefira composição e funções.
- **Fluxo visível.** Quem lê um método vê o que acontece: sem eventos encadeados escondendo quem chama quem, sem interceptadores, sem "mágica" de framework. Reflexão só nos **registros** documentados (handlers, efeitos, janelas), e todos são conferidos por teste na partida.
- Clareza acima de esperteza: `switch` explícito com cada caso escrito, nada de ternário aninhado nem fórmula inventada para poucos casos fixos ([exemplos no AGENTS.md](../../AGENTS.md)).
- `var` quando o tipo aparece à direita; tipo explícito quando não aparece.
- Sem número mágico: constante de protocolo ✅ é `const` com nome e evidência; número de jogo vem de tabela ou XML.

## Tipos e erros
- `Nullable` ligado e `TreatWarningsAsErrors`. `!` (supressão de nulo) só com comentário do porquê.
- **Tipos com nome** no lugar de tuplas e valores especiais: `ItemDuration.Permanent`, não `-1`.
- `record` para dado imutável, `sealed class` por padrão, `struct` só para tipos pequenos de caminho quente.
- **Recusa esperada é resultado, não exceção:** `ItemMoveResult.NoSpace`. O chamador trata cada caso com `switch`.
- **Exceção é bug ou entrada malformada:** `PacketFormatException` fecha a sessão; dado de jogo inválido derruba a partida. Nunca `catch (Exception)` para esconder erro: só nas bordas (sessão, `WorldIo`, frame do cliente), registrando no log.
- Validar **antes** de converter (`int` → `uint`, índice → posição) e antes de mudar estado. Nunca deixar o estado pela metade: valida tudo, depois aplica.

## Recursos permitidos e proibidos
| Use | Evite / proibido |
|---|---|
| `record`, `switch` de padrões, `required`, construtor primário | `dynamic`; `unsafe` sem medição revisada |
| `Span<T>`, `ArrayPool`, `stackalloc` **em caminho quente medido** | LINQ e `async` dentro do frame do cliente e do tick do mundo |
| `FrozenDictionary` para dados carregados | coleções mutáveis expostas por propriedade pública |
| `async`/`await` em I/O (sessão, banco, carga de asset) | `_ = AlgoAsync()`, `Task` solta, `.Result`/`.Wait()`, `async void` |
| injeção pelo construtor, dependência obrigatória | parâmetro `= null` para "funcionar sem", service locator, singleton estático com estado |
| `IGameClock` / `GameWorld.Now` / relógio do frame | `DateTime.Now`, `DateTimeOffset.UtcNow`, `Environment.TickCount` em código de jogo |
| métodos de extensão para conversão e formatação | métodos de extensão com regra de jogo |
| geradores de código **só** do projeto, versionados | bibliotecas "mágicas" (AutoMapper, MediatR, ORMs com tracking) |

Biblioteca nova (NuGet) exige uma entrada no [registro de decisões](../projeto/decisoes.md): o que resolve, se é mantida, se roda em NativeAOT e em arm64.

## Comentários e logs
- O comentário diz o **porquê** e a **evidência**, não repete o código: `// ✅ 0x91 v322: o cliente manda o segundo registro zerado (sessão 56)`.
- `<summary>` em todo tipo e membro público do `DRPG.Shared` e dos serviços.
- `TODO` só com o item do plano ou PR que o resolve (`// TODO(PR #41 S7): …`). TODO solto não passa na revisão.
- Log estruturado (`logger.LogInformation("Item {ItemId} moved by {Character}", …)`), sem segredo, com dado do cliente sempre cortado em tamanho.

## Testes
- Um arquivo de teste por domínio, espelhando a pasta do código.
- Todo handler de servidor tem teste com **entrada hostil**: tamanho errado, id trocado, índice fora do intervalo, estado errado, longe do alvo.
- Todo pacote do `DRPG.Shared` tem **ida e volta** (`Write` → `Read` igual) e, quando houver captura, **teste dourado** contra o payload capturado ([shared](shared.md)).
- Todo leitor de formato tem teste com arquivo truncado e campos absurdos (contagem enorme, offset fora do arquivo).
- Cliente: o que não é desenho (estado, handlers, layout de UI) é testado sem GPU.

## Como as regras são garantidas
Uma regra que depende de lembrar acaba quebrando. Sempre que possível, ela vira verificação automática:

| Regra | Garantia |
|---|---|
| Estilo e formatação | `.editorconfig` + `dotnet format --verify-no-changes` no CI |
| Avisos de qualidade | `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`, avisos viram erro |
| APIs proibidas (`DateTime.Now`, `Task.Wait`, `BinaryFormatter`, `Console.Write`, `Thread.Sleep`) | `BannedApiAnalyzers` com `BannedSymbols.txt` por projeto |
| Dependência entre projetos e camadas | `DRPG.Architecture.Tests` (ex.: `Game.Entities` não usa `Network` nem `Data`; `Shared` não usa nada do projeto) |
| Contrato de handlers, pacotes, efeitos e janelas | testes de contrato na partida (`HandlerContractTests` e equivalentes) |
| Tamanho de método | relatório no CI. Não bloqueia, mas aparece no PR |
| Revisão | checklist do PR no [AGENTS.md](../../AGENTS.md) |

❓ **Hoje** o servidor já tem `Nullable`, `TreatWarningsAsErrors` e `HandlerContractTests`. Os demais itens da tabela entram no marco 0.
