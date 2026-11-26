# Formato `.spr` (sprites)

Lido pelo carregador da v322 em `0x00871e80` (chamado pelo cache de sprites `0x00b21be0` → `0x00b23550`). Leitor: `src/DRPG.Shared/Formats/Sprite/SpriteFile.cs`. Conferido contra os 654 sprites `gui\dr*` do `resource.pf` da v322: todos decodificam sem sobrar byte.

## Arquivo
Inteiros little-endian.

| Campo | Tipo |
|---|---|
| `frameCount` | `u32` |
| `wordCount` | `u32`, palavras de 16 bits da imagem |
| offsets | `u32[frameCount]`, em palavras, dentro dos dados da imagem |
| larguras | `u32[frameCount]` |
| alturas | `u32[frameCount]` |
| imagem | `u16[wordCount]` |
| sombra | `u32 n`; se `n > 0`: `u32[frameCount]` offsets + `u16[n]` |
| camada extra | igual à sombra. ❓ Nenhum sprite de GUI usa; papel desconhecido |

Frames diferentes podem apontar para o mesmo offset (frame repetido). Frame com tamanho 0 não tem pixels.

## Frame
Codificado por linha:

```
u16 linhas (= altura)
por linha: u16 trechos
  por trecho: u16 pular, u16 tamanho, u16 pixel[tamanho]
```

`pular` avança o x a partir do fim do trecho anterior. Pixel pulado é **transparente**; não há colorkey. Os pixels são **RGB565**. Quando a tela é 555, o cliente converte para RGB555 na carga (`0x00874fa0`, desligado se `DAT_0132d1d0` ≠ 0).

A sombra usa o mesmo formato, com pixels escuros (ex.: `gui\dr2_npc_003_002.spr`). ❓ Como o cliente a mistura (alpha fixo?) ainda não foi visto.

❓ Não há âncora/ponto de origem no arquivo: a posição vem de quem desenha.
