# Arquitetura do projeto (fundação)

**Leitura obrigatória** antes de escrever código no servidor, no cliente ou nas ferramentas. Estas regras valem para todo o projeto. Mudar uma delas exige uma entrada no [registro de decisões](../projeto/decisoes.md). Opinião de quem está implementando não basta.

Páginas desta seção:
- **Esta página:** valores, organização do repositório, camadas e threads, comuns aos dois lados.
- [Padrões de código](padroes-de-codigo.md): como o código C# é escrito, o que é permitido e proibido, e como as regras são garantidas automaticamente.
- [Biblioteca compartilhada](shared.md): protocolo, pacotes de mão dupla, formatos de arquivo e versões do oficial.
- [Cliente](cliente.md): camadas, threads e a fatia de feature do cliente.
- [Fidelidade e resolução](resolucao.md): 800×600 como referência, widescreen só na apresentação, e o guia para traduzir coordenadas do decompilado.
- [Como estender](extensao.md): receitas fechadas (item, opcode, janela, efeito, patch do oficial), com a lista exata de arquivos de cada uma.
- O detalhe do servidor continua em [emulador](../emulador/arquitetura.md), [handlers](../emulador/handlers.md), [dados](../emulador/dados.md) e [segurança](../emulador/seguranca.md).

## Valores (em ordem de prioridade)
1. **Segurança.** O servidor é a autoridade. Tudo que chega de fora (pacote, arquivo, configuração) é hostil até ser validado. Sem gambiarra: se o jeito certo é maior, faz-se o jeito certo.
2. **Legibilidade.** Código explícito, curto e óbvio. Classes pequenas com um papel só. Recurso avançado de linguagem só com ganho medido (desempenho) ou quando elimina repetição real.
3. **Expansão.** Coisa nova entra por **dado** ou por **um arquivo novo**, achado automaticamente. Nunca por várias listas espalhadas que precisam ser lembradas.

Quando dois valores brigam, vence o de cima. Exemplo: um controle de segurança que deixa o código mais longo fica.

## Repositório
Um repositório só (monorepo), uma solução, .NET 10. Uma mudança de protocolo muda servidor, cliente e testes no mesmo PR.

```
src/
  DRPG.Shared/    protocolo, pacotes, formatos de arquivo, tabelas do cliente   (sem I/O de rede, sem banco, sem SDL)
  DRPG.Engine/    plataforma SDL3, renderização, fontes, sprites, instalação, cursor, áudio   (sem protocolo, sem cena)
  DRPG.Server/    emulador
  DRPG.Launcher/  login + auto-patcher (marco 1)
  DRPG.Client/    cliente: cenas, rede (login → channel), carga (marco 2)
  DRPG.Tools/     ferramentas de linha de comando (extração, visualizador, geradores de XML)
tests/
  DRPG.Shared.Tests/  DRPG.Engine.Tests/  DRPG.Server.Tests/  DRPG.Launcher.Tests/  DRPG.Client.Tests/  DRPG.Architecture.Tests/
docs/             esta wiki
```
**Hoje** existem `DRPG.Shared`, `DRPG.Engine`, `DRPG.Server`, `DRPG.Launcher` e `DRPG.Client` (com os testes de cada um). `DRPG.Tools` e `DRPG.Architecture.Tests` vêm depois ([plano do cliente](../planos/cliente-novo.md)).

**Regra do Shared:** só entra no `DRPG.Shared` o que **mais de um projeto usa** (ex.: o protocolo do launcher, que o servidor abre e o launcher fecha; o leitor `.hf/.pf`, que o servidor e o cliente leem; os pacotes de login e channel, que o servidor lê e o cliente grava, e o contrário). Código de um projeto só fica nele, mesmo que um dia possa ser útil a outro: muda para o Shared quando o segundo usuário existir. Por isso `PatchConfiguration` e `PatchMerger` ficam no launcher.

Dependências permitidas entre projetos (conferidas por `DRPG.Architecture.Tests`):

| Projeto | Pode usar | Nunca usa |
|---|---|---|
| `DRPG.Shared` | só a BCL | Server, Client, banco, SDL, rede |
| `DRPG.Server` | Shared, Dapper/MySqlConnector, Microsoft.Extensions.* | Client, SDL |
| `DRPG.Engine` | Shared, SDL3, SDL3_ttf, NVorbis | Server, Launcher, Client, banco, protocolo de jogo (pacotes), cenas |
| `DRPG.Launcher` | Shared, Engine | Server, banco |
| `DRPG.Client` | Shared, Engine | Server, banco |
| `DRPG.Tools` | Shared | Server, Client |

## O mesmo desenho nos dois lados
Servidor e cliente seguem o mesmo modelo mental. Quem entende um entende o outro.

| Conceito | Servidor | Cliente |
|---|---|---|
| Dono do estado | **thread do mundo** (`GameWorld`, tick de 50 ms) | **thread principal** (loop de frame) |
| Outras threads | recebem e cortam pacotes, I/O de banco | recebem e cortam pacotes, carregam assets |
| Volta para a thread dona | fila do mundo / `WorldIo` | fila de mensagens do frame |
| Entrada de pacote | request tipado → handler → **um** serviço | pacote tipado → handler → atualiza o **estado** |
| Regra de jogo | serviço do domínio | **não existe**: o cliente mostra, o servidor decide |
| Estado | entidades com invariantes | espelho do que o servidor mandou |
| Feature nova | pasta do domínio em `Game/<Domínio>` | pasta da feature em `Features/<Domínio>` |
| Registro | atributo `[PacketHandler]`, achado na partida | o mesmo atributo, achado na partida |

Regras que valem nos dois:
- **Uma thread dona do estado.** Ninguém mais lê ou escreve estado de jogo. Sem `lock` em código de jogo: se aparecer um, o desenho está errado.
- **Pacote é layout.** Só o tipo do pacote lê e escreve bytes. Handler, serviço e UI nunca veem offset.
- **Uma regra, um lugar.** Fórmula, sequência de pacotes ou validação usada em dois lugares vira método do domínio no mesmo PR.
- **Dado em vez de código.** Item, card, título, loja e drop são dados. Comportamento novo é um tipo de efeito novo, nunca um `case` por id.
- **Falhar cedo.** Dado quebrado derruba a partida com mensagem clara. Pacote malformado fecha a conexão. Nada de "segue com valor padrão".
- **Evidência.** Layout, fórmula e constante de jogo citam a fonte (pcap, opcode, endereço no binário) e marcam ✅/❓.

## Segurança: o que é de cada lado
- **Servidor:** modelo de ameaça e controles em [segurança](../emulador/seguranca.md). O cliente é hostil.
- **Cliente:** o servidor pode ser malicioso (servidor de terceiros) e os arquivos do jogo podem estar adulterados. O cliente nunca confia em tamanho, índice ou contagem vindos de pacote ou arquivo: tudo passa pelos leitores do `DRPG.Shared`, que validam limites. Pacote inválido do servidor desconecta com mensagem; asset inválido vira textura de erro e log, nunca crash nem leitura fora do buffer.
- **Os dois:** sem `unsafe` fora de código de desempenho medido e revisado; sem desserialização genérica (`BinaryFormatter`, JSON polimórfico) de dado externo; segredo nunca vai para o log.

## Documentação junto com o código
Mudou layout, fórmula, regra ou arquitetura: código, teste e página da wiki mudam **no mesmo PR** ([AGENTS.md](../../AGENTS.md)). Página nova entra no [índice](../README.md).
