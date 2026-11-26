# Arquitetura do cliente

❓ **Ainda não existe código.** Esta página é o contrato que o cliente segue desde a primeira linha ([plano](../planos/cliente-novo.md)). Visão geral em [arquitetura](README.md), regras de código em [padrões](padroes-de-codigo.md).

## Princípio
O cliente **mostra** o que o servidor decidiu e **pede** o que o jogador quer. Não tem regra de jogo: não calcula dano, não decide se um item cabe, não muda inventário por conta própria. Uma checagem local (ex.: botão desabilitado sem espaço) é só conforto de interface e nunca substitui a resposta do servidor.

## Camadas
Cada camada só usa as de baixo.

| Camada | Pasta | Faz | Não faz |
|---|---|---|---|
| Plataforma | `Platform/` | **único** lugar que chama SDL3: janela, eventos de input, dispositivo GPU, áudio, DPI | saber o que é jogo |
| Renderização | `Rendering/` | interface `IRenderer` (texturas, quads, blend, alvo de render) com uma implementação por backend (no launcher: `SdlRenderer`, renderizador 2D da SDL3 com driver `gpu`; no cliente: `SdlGpuRenderer`); `SpriteBatch`, `Font` (TTF), `Camera` acima dela | saber o que é digimon ou janela; código fora de `Rendering/Backends/` nunca vê SDL_GPU |
| Assets | `Assets/` | `AssetStore`: lê do `resource.pf` com os leitores do `DRPG.Shared`, carrega fora da thread principal, envia para a GPU na principal, cache | regra, desenho |
| Rede | `Net/` | uma `Connection` por servidor (login, channel, map, global), framing, parse em pacote tipado, fila para a thread principal, envio de requests | mudar estado |
| UI | `Ui/` | `Window`, widgets (`Button`, `Label`, `ScrollList`, `TextInput`), âncora e escala, foco, roteamento de input | mandar pacote, guardar estado de jogo |
| Mundo | `World/` | mapa (camadas, tiles, objetos), entidades, y-sort, câmera com viewport livre, interpolação de movimento | decidir movimento válido |
| Features | `Features/<Domínio>/` | estado, handlers, ações e janelas de um domínio (abaixo) | tocar em SDL ou bytes |
| Cenas | `Scenes/` | `Login → ServerSelect → CharacterSelect → Loading → Field ↔ Battle`; cada cena liga as features e janelas que usa | regra de feature |

## A fatia de feature
Toda funcionalidade tem a mesma forma, em `Features/<Domínio>/`. Exemplo para o inventário:

| Arquivo | Papel | Regra |
|---|---|---|
| `InventoryState.cs` | espelho do que o servidor mandou | só os handlers mudam; a UI só lê |
| `InventoryHandlers.cs` | `[PacketHandler]` de cada S2C do domínio → atualiza o estado | um método por pacote, sem desenhar nada |
| `InventoryActions.cs` | o que o jogador pode pedir: `MoveItem`, `UseItem` → monta e envia o request | **único** lugar da feature que envia pacote |
| `InventoryWindow.cs` | desenha o estado e chama as ações | não envia pacote nem muda estado |

- Quer saber o que o cliente manda? Procure em `*Actions`. O que ele recebe? Em `*Handlers`. Nunca é preciso caçar `Send` pela UI.
- Estado é dado simples (records, listas somente leitura para fora). A janela lê o estado a cada frame; não há evento "inventário mudou" com assinantes espalhados.
- Feature que precisa de outra lê o **estado** dela (ex.: a batalha lê `PartyState`). Nunca chama a janela de outra feature.
- **Previsão local** (agir antes da resposta) só onde o original faz, com evidência. Exemplo: o movimento anda antes do `0x20` voltar. Cada previsão é documentada na página da feature.

## Threads e o frame
- **Thread principal** dona de todo o estado, da UI e da GPU.
- **Rede:** uma tarefa de recepção por conexão corta e faz o parse do pacote, e põe o pacote **já tipado** na fila da thread principal. Pacote inválido fecha a conexão e mostra a mensagem.
- **Assets:** leitura e decodificação no pool de threads; o envio para a GPU e o registro no cache acontecem na principal.

Ordem fixa de cada frame:
1. eventos da plataforma → input;
2. esvazia a fila de rede → handlers atualizam o estado;
3. esvazia a fila de assets prontos;
4. atualiza cena, mundo (animação, interpolação) e UI;
5. desenha mundo, depois UI, depois cursor.

Sem `async`/`await` dentro do frame. Sem alocação por frame no caminho quente ([regras de desempenho](../planos/cliente-novo.md#regras-de-desempenho-para-nunca-precisar-trocar-de-linguagem)).

## Registro automático, compatível com NativeAOT
Handlers (`[PacketHandler]`) e janelas (`[Window]`) são achados por um **gerador de código** do projeto, que monta a tabela em tempo de compilação. Não é reflexão em runtime, que o NativeAOT corta. Criar o arquivo basta. Testes de contrato conferem que todo S2C do `DRPG.Shared` tem handler no cliente ou está na lista explícita de ignorados, com o motivo.

## Tela e resolução
- O mundo é desenhado em coordenadas de mapa, e a câmera tem viewport livre e zoom. Tela maior mostra mais mapa.
- Cada janela guarda a **posição original em 800×600** (constante com a evidência: classe `CWindow_*` e endereço) e uma **âncora** (canto, borda ou centro). A posição final = âncora + posição original × escala da UI (1×, 1,5×, 2×; padrão 1×).
- Arte de tela cheia de 800×600 e telas fixas em 4:3 (login, seleção de personagem, loading) são **centralizadas** num quadro de 800×600, com bordas pretas, por um único utilitário (`FixedScreen`). Nada de esticar.
- Texto sempre por `Font` (TTF), com tamanho em pontos multiplicado pela escala da UI.
- **Modo padrão = original.** Modernidades (resolução, escala, zoom, fontes) ficam em `ClientOptions`, validadas na partida.

## Instalação original (drop-in)
- O executável roda **na pasta da instalação oficial** e acha os arquivos pelo diretório dele (ou `--game-dir`). `GameInstall` valida a pasta na partida: falta de arquivo obrigatório para com mensagem clara.
- Os arquivos do jogo são **somente leitura**: o cliente nunca grava em `resource.pf`, `table.pf` nem nos `.ini` do original. Opções novas ficam em arquivo próprio (`drpg-client.ini`).
- Linha de comando compatível com o `DigimonRPG.bin` ([servidores](../protocolo/servidores.md)).

## Erros no cliente
- Pacote inválido ou inesperado do servidor: desconecta, mostra a mensagem e registra o pacote (cortado) no log.
- Asset ausente ou inválido: textura de erro visível + log **uma vez** por arquivo. Nunca crash, nunca leitura fora do buffer.
- Exceção num handler ou janela: capturada **só** na borda do frame, registrada; em build de desenvolvimento, para o jogo com a pilha na tela.

## Testes
- Handlers e estado: alimentados com pacotes do `DRPG.Shared` (inclusive os dourados), sem janela e sem GPU.
- Layout de janela: a árvore de UI é calculada sem GPU e as posições são comparadas com as do original.
- Leitores de formato: no `DRPG.Shared.Tests`. O visualizador (`DRPG.Tools`) gera PNG para comparar com screenshot do original.
