# Cliente: fidelidade e resolução

**Leitura obrigatória** antes de escrever ou portar qualquer coisa que desenhe, posicione ou teste clique no cliente. Vale para mundo, UI, cursor e telas fixas. Visão geral do cliente em [cliente](cliente.md). Decisão em [decisões](../projeto/decisoes.md) (2026-11-27). Escopo da implementação em [plano de widescreen](../planos/widescreen.md).

## O princípio
O cliente original é a referência de **todo** comportamento e de toda apresentação. A **única diferença de apresentação** intencional é rodar em resoluções e proporções além de 800×600 (4:3). Resolução nunca justifica redesenhar, simplificar ou reinterpretar o original.

Dois espaços de coordenadas, e só dois, existem no código de feature:

| Espaço | Onde | Tamanho | Quem usa |
|---|---|---|---|
| **Espaço original** | UI e telas fixas | sempre **800×600** (`OriginalScreen`) | todo código de UI portado do original |
| **Espaço do mundo** | campo, entidades, câmera | pixels do mapa; a área visível é o `Viewport` | código do mundo |

Nenhuma feature conhece o tamanho físico da janela, a escala ou a densidade dos assets. Isso é da camada de apresentação (`DRPG.Engine/Rendering`).

## As camadas
1. **Lógica original:** estado, regras de UI, tempo, animação. Não sabe que existe resolução.
2. **Layout:** grupos de UI ancorados (`UiGroup` + `Anchor`) traduzem o espaço original para a área lógica da UI.
3. **Escala:** área lógica para pixels físicos (`PresentationLayout`).
4. **Densidade de assets:** escolhe o arquivo 1×, 2×, 3× ou 4× sem mudar o tamanho lógico.
5. **Render:** GPU e recorte.

Cada camada só fala com a vizinha. Feature mexe só na 1 e declara a âncora na 2.

## Área de apresentação
A janela física é recortada para uma proporção entre **4:3** e **2,39** (3440×1440; cobre também 2560×1080). Fora disso, barras pretas: dos lados acima de 2,39, em cima e embaixo abaixo de 4:3. O resto desta página fala dessa área recortada.

### UI: escala fracionária, altura lógica sempre 600
- `uiScale = alturaDaÁrea / 600` (opção `UiScale = auto`, padrão). Opções inteiras `1`, `2` e `3` existem para quem quiser pixel exato, desde que a área lógica resultante tenha pelo menos 800×600.
- Área lógica da UI: `largura / uiScale` × **600**, ou seja, de 800 a ≈1434 de largura.
- Em 800×600 tudo é identidade.

| Saída | Escala UI | Área lógica UI | Densidade usada | Mundo visível (1×) |
|---|---:|---:|---|---:|
| 800×600 | 1,0 | 800×600 | original | 800×600 |
| 1280×720 | 1,2 | 1067×600 | 2× reduzido | 1280×720 |
| 1280×1024 (5:4) | 1,6 (área 1280×960) | 800×600 | 2× reduzido | 1280×960 |
| 1920×1080 | 1,8 | 1067×600 | 2× reduzido | 1920×1080 |
| 2560×1080 | 1,8 | 1422×600 | 2× reduzido | 2560×1080 |
| 2560×1440 | 2,4 | 1067×600 | 3× (ou 4×) reduzido | 2560×1440 |
| 3840×2160 | 3,6 | 1067×600 | 4× reduzido | 3840×2160 |

Por que não inteira: em 1080p, 2× daria altura lógica 540, e as janelas do original (feitas para 600) não caberiam sem refazer o layout. 1× fica pequeno demais. 90 % da UI são janelas desenhadas, que reduzem bem a partir de asset HD.

### Mundo: escala inteira, separada da UI
- `worldScale` inteiro, padrão **1×**: o mundo ganha visão na horizontal e na vertical. `Viewport = área / worldScale`.
- O zoom para áreas com altura acima de 600 (ex.: 2× no 4K mostra 1920×1080 do mapa) é a mesma escala inteira, como opção futura. ❓ Ainda não implementado.
- Sprites do mundo em **nearest inteiro** ("upscale bruto"). Sprite de mundo em HD fica para o futuro.
- O mundo é desenhado num alvo do tamanho do `Viewport` e ampliado por inteiro. A UI vem por cima, direto em pixels físicos.

### Densidade de assets (UI)
- Arquivos HD numa pasta do cliente, nunca nos arquivos do jogo: `hd/<n>x/<caminho no pack>` (n = 2, 3, 4). Sem o HD, usa o original.
- **O asset HD tem exatamente o tamanho original × n** (frame a frame). Diferente disso, ele é recusado com log uma vez e o original é usado. Assim, o HD nunca muda o tamanho lógico de nada, e recortes e âncoras de frame continuam os do original.
- Seleção: a **menor densidade disponível que seja ≥ `uiScale`**, reduzida com filtro linear. Sem nenhuma ≥, usa a maior. O original (1×) ampliado por fração passa por sharp-bilinear (nearest inteiro, depois linear para baixo).
- Texto TTF rasterizado no tamanho físico (pontos × `uiScale`).

### Arredondamento e clique
- Retângulo físico pelas **bordas**: `round(x0 · s)` e `round(x1 · s)`, nunca `round(x · s) + round(w · s)`. Isso evita fresta entre as peças de uma janela em 1,8×.
- O mouse vira coordenada do espaço original pela **mesma** transformação, invertida, num lugar só (`PresentationLayout` + `UiGroup`). Feature nunca converte coordenada de mouse.
- Teste obrigatório da infraestrutura: todo pixel físico dentro do retângulo desenhado de um elemento acerta o elemento no hit-test, em todas as saídas da tabela.

## Guia: do decompilado para o nosso código
O decompilado está cheio de números de tela (há 134 imediatos `0x320` e 115 `0x258` no binário, fora 400/300 e as posições de layout; ver [qualidade de vida](../cliente/qualidade-de-vida.md)). A regra de ouro:

> **Copie o número do original como está. Nunca faça a conta da adaptação você mesmo.** A âncora do grupo faz a adaptação.

### Passo a passo
1. **Classifique cada número** pela tabela abaixo antes de escrever qualquer linha.
2. **Coordenada de UI:** constante com o valor original (decimal ou hex, como estiver mais legível) e a evidência ✅ (função ou endereço), dentro de um `UiGroup` com a `Anchor` do conjunto. Ela continua em coordenada absoluta da tela 800×600, como no original. Não a torne relativa ao grupo, nem subtraia de 600.
3. **Conta com o tamanho da tela no layout** (ex.: `(800 − w) / 2`): reescreva com `OriginalScreen.Width`/`Height` e deixe a conta igual. Ela roda no espaço original, e o grupo leva o resultado para o lugar certo.
4. **Clique e hover:** escreva o teste com os números do original (`y >= 495`). O grupo entrega o mouse já no espaço original.
5. **Tamanho de tela no mundo** (centralizar a câmera, células visíveis): troque por `Viewport.Width`/`Height`, com o comentário `✅ <endereço>: original 800`.
6. **Coincidência** (800 ms, 600 de HP): constante com nome do que ela é, como qualquer outra.
7. **Teste:** layout em 800×600 igual às constantes do original, mais 1920×1080 e 1280×1024 ([testes](#testes)).

### Classificação
| O que o decompilado tem | Exemplo | O que escrever |
|---|---|---|
| Posição de elemento de UI | `(450, 515)` botão do menu em `0x00b8a850` | `const` com o valor original, no `UiGroup` da barra (`Anchor.BottomCenter`) |
| Tamanho de elemento, frame, margem | `ChatWidth = 409`, frame de 13×13 | `const` com o valor original, em unidade lógica |
| Conta com a largura ou altura da tela, na UI | `(800 − width) / 2` em `0x00b55560` | `(OriginalScreen.Width − width) / 2`, no grupo com a âncora certa |
| Teste de clique em coordenada de tela | `y >= 495` em `0x00b91ec0` | o mesmo número, contra o mouse do grupo |
| Tamanho de tela no mundo | `x − 400`, `800 / 64 + 2` | `Viewport.Width / 2`, `Viewport.Width / CellWidth + 2` |
| Limite da câmera | `0 .. largura · 64 − 800` | `0 .. largura · 64 − Viewport.Width` (mapa menor: ver [mundo](#mundo-detalhes)) |
| 800 ou 600 que não é tela | `800 < GetTickCount() − last` | `const` com o nome do que é (`CooldownMs`) |
| Arte de tela cheia ou tela fixa 4:3 | login, seleção, loading | cena inteira num `UiGroup` `Anchor.Center` (`FixedScreen`), fundo preto |

### Proibido
- Número físico ou derivado da resolução em feature: `1920`, `1067`, `533`, `uiScale`, `Window.Width`.
- Coordenada original "ajustada" à mão (`600 − 85`, `x + 133`) ou convertida para relativa sem existir no original.
- `if` de resolução, proporção ou escala em lógica de jogo ou de UI.
- Sistema de posição ou de escala próprio de uma feature.
- Âncora por elemento quando o original posiciona um conjunto: a âncora é do **grupo** que o original move junto (barra de baixo, painel do tamer, janela), para preservar as relações entre as peças.
- `800`/`600` literal no cliente fora de `OriginalScreen` e das constantes de coincidência com nome.
- Mudar comportamento original para resolver problema de layout. Conflito se resolve na apresentação.

### Exemplo: a barra de baixo do HUD
O original (`0x00b8a850`) desenha os botões do menu em `(450, 515)`, `(482, 515)`… e ignora o clique no chão quando `y >= 495` (`0x00b91ec0`).

```csharp
// ✅ 0x00b8a850: the bottom bar moves as one piece, centred on the screen.
private static readonly UiGroup Bar = new(Anchor.BottomCenter);

// ✅ the buttons of the menu, original screen coordinates
private static readonly (int Sprite, int X, int Y)[] MenuButtons = [(10, 450, 515), (11, 482, 515), /* … */];

private const int BarY = 495;                                   // ✅ 0x00b91ec0

public bool Covers(UiPoint mouse) => Bar.ToOriginal(mouse).Y >= BarY;   // same number as the original
```

Em 800×600 o grupo não desloca nada. Em 1067×600 ele desloca tudo em `(1067 − 800) / 2` na horizontal, e quem calcula isso é o grupo, nunca a feature.

### Quando não está claro
Se o original não deixa claro qual âncora vale (o elemento fica no canto ou no centro?), investigue no decompilado onde ele é reposicionado e em que grupo é desenhado. Se ainda assim não ficar claro, marque ❓ no comentário e na página da tela e escolha o grupo do elemento vizinho mais próximo. Nunca invente um comportamento.

## Mundo: detalhes
- Câmera: centra o tamer em `Viewport / 2`, presa em `0 .. tamanhoDoMapa − Viewport` (✅ `0x00afe310`, original com 800×600).
- **Mapa menor que o viewport:** o mapa é centralizado naquele eixo e o resto fica preto. ❓ O original nunca chega nesse caso. É apresentação, não regra.
- Células visíveis e margens de objetos (`FieldMap.ObjectMargin*`): as margens ficam as do original, e a janela de células vem do `Viewport`.
- ❓ **Alcance de visibilidade do servidor:** com mais mundo na tela (inclusive na vertical), entidades podem aparecer e sumir dentro da área visível. O raio que o oficial usa precisa ser confirmado nas capturas antes de mudar o emulador. Dependência registrada, nunca resolvida em silêncio.
- Movimento, velocidade, tempo de animação, colisão e protocolo não mudam com a resolução.

## Testes
Toda tela ou janela nova tem teste de layout sem GPU em:
- **800×600:** as posições resolvidas são **iguais** às constantes do original (identidade);
- **1920×1080** (área 1067×600): as âncoras dão as posições esperadas;
- **1280×1024** (5:4, barras em cima e embaixo): continua idêntico ao 800×600 dentro da área.

A infraestrutura (`PresentationLayout`, `UiGroup`, seleção de densidade) testa as saídas da tabela, o teto de 2,39 e o arredondamento pelas bordas: desenho e hit-test em ida e volta.

## Prioridade de decisão
1. Fidelidade ao comportamento do original.
2. 800×600 idêntico ao original.
3. Arquitetura de UI independente da resolução.
4. Adaptação de apresentação para widescreen.
5. Melhorias visuais opcionais.
