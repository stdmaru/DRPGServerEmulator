# Plano: widescreen e resoluções maiores

> **Escopo definido, não iniciado.** Começa **depois do merge do PR #51** (`feat/client-walk`, UI básica do campo). É a parte "resolução, tela cheia e escala de UI" do [marco 4](cliente-novo.md#marcos-cada-um-jogável-contra-o-emulador). As regras estão em [fidelidade e resolução](../arquitetura/resolucao.md), e a decisão em [decisões](../projeto/decisoes.md) (2026-11-27).

## Objetivo
O cliente roda em qualquer janela ou tela cheia entre 4:3 e 2,39, com 800×600 idêntico ao original e mais área nas resoluções maiores, sem mudar nenhum comportamento original e sem que nenhuma feature conheça a resolução.

## Dentro do escopo
1. **Apresentação (`DRPG.Engine/Rendering`)**
   - `PresentationLayout` no lugar de `FrameLayout`: recorte entre 4:3 e 2,39 com barras pretas, `uiScale` fracionária (`altura / 600`, opções `auto | 1 | 2 | 3`) e `worldScale` inteiro (padrão 1×).
   - `OriginalScreen` (800×600, o espaço da UI) no lugar de `IRenderer.FrameWidth`/`FrameHeight`.
   - Retângulos físicos arredondados pelas bordas. A transformação inversa para o mouse fica no mesmo tipo.
   - Mundo desenhado num alvo do tamanho do `Viewport` e ampliado em nearest inteiro. UI desenhada por cima, em pixels físicos.
2. **Layout de UI:** `Anchor` (9 posições) e `UiGroup` (âncora + conversão do espaço original para a área lógica, nos dois sentidos).
3. **Densidade de assets:** pasta `hd/<n>x/` (n = 2, 3, 4), conferência de tamanho exato (original × n, frame a frame), seleção da menor densidade ≥ escala, redução linear e sharp-bilinear para o original.
4. **Texto:** TTF rasterizado no tamanho físico (pontos × `uiScale`).
5. **Janela:** redimensionável e tela cheia (`SdlPlatform` deixa de ser fixa em 800×600). Opções em `ClientOptions`/`drpg-client.ini`, validadas na partida.
6. **Migrar o que já existe para as regras** (lista de hoje, conferir de novo depois do #51):
   - `World/FieldMap.cs`: `ViewCellsX/Y` e `Camera` passam a usar o `Viewport`. Mapa menor que o viewport fica centralizado, com preto em volta.
   - `Scenes/GameScene.cs`: grupos ancorados do HUD e o mouse convertido para o espaço original.
   - `Ui/FieldHud.cs`: barra de baixo, painel do tamer e chat em `UiGroup`, com as coordenadas originais sem mudança.
   - Telas fixas em 4:3 (`ServerScene`, `CharacterSelectScene`, `LoadingScreen`, `DeleteCharacterDialog`, `SecondaryPasswordDialog`, `ReturnLoadingScene`): `UiGroup(Anchor.Center)` (`FixedScreen`).
   - `Ui/UiText.cs`, `Engine/Scenes/CursorView.cs`: escala da UI e densidade.
   - Toda janela e todo elemento que o #51 trouxer.
7. **Testes:** os de layout em 800×600, 1920×1080 e 1280×1024 para cada tela migrada, e os da infraestrutura (todas as saídas da [tabela](../arquitetura/resolucao.md#ui-escala-fracionária-altura-lógica-sempre-600), teto de 2,39, ida e volta de desenho e hit-test).
8. **Asset HD de teste:** pelo menos a barra de baixo do HUD em 2× e 4×, para validar a densidade de ponta a ponta. Os demais assets HD não são deste PR.

## Fora do escopo
- Zoom do mundo (escala inteira > 1 como opção do jogador): fica para quando houver avaliação em telas grandes. O `worldScale` já existe e o padrão é 1×.
- Sprites do mundo em HD (tamer, digimons, animações): por ora só o upscale bruto.
- Produção dos assets HD da UI, além do asset de teste.
- Mudar o alcance de visibilidade do emulador (ver dependências).
- Launcher (`DRPG.Launcher`): continua com o quadro fixo de 800×600.
- Rework das telas fixas em 4:3: continuam centralizadas.

## Dependências e riscos
- ❓ **Alcance de visibilidade do servidor:** com 1× numa tela grande, o mundo visível passa de 800×600 nos dois eixos. Antes de mudar o emulador, levantar nas capturas (`captures.db`) a distância em que o oficial cria e remove entidades. Até lá, o problema fica registrado em [resolução](../arquitetura/resolucao.md#mundo-detalhes) e não é resolvido em silêncio.
- **4K em 1×** mostra 3840×2160 de mapa, e isso aumenta o desenho por quadro. Medir o p99 do quadro no 4K (o orçamento é < 16,6 ms).
- **Compositores tiling (Hyprland)** já redimensionam a janela. Testar as barras e a troca de tamanho em tempo real.

## Pronto quando
- [ ] 800×600 é igual pixel a pixel ao estado anterior ao PR (comparar screenshots de login, seleção e campo).
- [ ] 1920×1080, 2560×1080, 3440×1440, 1280×1024 e uma janela redimensionada à mão funcionam: barras corretas, HUD ancorado, clique certo em tudo.
- [ ] Nenhum `800`/`600` de tela fora de `OriginalScreen` e `Viewport` no `DRPG.Client` (busca por `800`, `600`, `400`, `300`, `0x320` e `0x258` revisada).
- [ ] Asset HD de teste aparece nítido em 1080p e no 4K, sem mudar de tamanho lógico. Um HD de tamanho errado é recusado com log.
- [ ] `dotnet test` 100 %. Testes de layout das telas migradas nas três resoluções.
- [ ] A página de [resolução](../arquitetura/resolucao.md) é atualizada se algum nome ou regra mudou na implementação.

## Até lá (PR #51 e seguintes)
O código de UI que entrar antes deste PR já segue a [regra de coordenadas](../arquitetura/resolucao.md#guia-do-decompilado-para-o-nosso-código): número do original copiado como está, com a evidência, e sem conta de adaptação. Assim, a migração só embrulha os grupos num `UiGroup` e não reescreve posições.
