# Mapas do cliente (`.map`)

Cada mapa do `map_us` aponta, na coluna 4, para `map\<nome>.map` no `resource.pf` (ex.: `R079` → `map\r079.map`;
o nome não diferencia maiúsculas). A entrada é só zlib, sem o codec das tabelas.

## Formato
✅ Carregador da v322 `0x00b35fb0` (objeto de mapa `DAT_011584f8`, classe `0x0102e3f8`, base `0x00b35950`), conferido no `r079.map`.

| Offset | Tipo | Campo |
|---|---|---|
| 0 | u32 | largura em células (✅ = limite de x no protocolo; mapa 2 = 176) |
| 4 | u32 | altura em células (✅ = limite de y; mapa 2 = 263) |
| 8 | u32, u32 | 64, 32: tamanho da célula em pixels |
| 16 | 24 B × largura × altura | **uma** camada de células, linha a linha (✅ `0x00b35e60`: `(largura · y + x) · 24`) |
| … | u16 n + n × 20 B | nomes dos `.til` (`Map\%s`, até 200; erro `LoadTile Error : %s`) |
| … | u16 m + m × 20 B | nomes dos `.obj` (`Map\%s`, até 160; erro `Map : %s LoadObject Error : %s`) |

Célula (24 B):
| Offset | Tipo | Conteúdo |
|---|---|---|
| +0 | u32 | ❓ (0 no `r079`) |
| +4 | i32 | **objeto** ancorado nesta célula: índice na lista de `.obj` (−1 = nenhum; < 9999; ≥ 160 → aviso `0x5d1`) |
| +8 | i32 | **tipo da célula**: 1..998 áreas registradas na carga (`0x00b392a0`); 1000..1005, 9001..9021 efeitos de chão (`0x00b380d0`); 8999 aviso de tempo; −1 nada |
| +0xC | i16 | **tileset**: índice na lista de `.til` (−1 = sem chão; ≥ 200 → aviso `0x5d0`) |
| +0x10 | u32 | **quadro** do tile dentro do tileset (0..199) |
| +0x14 | u32 | **bit 0 = andável** ✅; o byte `+0x15` (bit 8) muda o modo de desenho do chão (`0x00b56160(600, b)`, ❓ qual) |

- O `r001.map` (e outros antigos) tem lixo `0xBAADF0xx` nos bits altos dos campos; o bit 0 continua valendo.
- Depois da carga: tamanho do mapa em pixels `largura · 64` × `altura · 32`, janela visível `800 / 64 + 2` × `600 / 32 + 2` células (`[+0x511c90/94]`), câmera em pixels `[+0x511c80/84]`. O mapa `0x66/0x67` carrega extras (`0x00b385e0`).

## `.til` (tileset) ✅ `0x00b5dbd0`
| Campo | Tipo |
|---|---|
| assinatura | 10 B `DCTILE0.03` |
| largura, altura | u32, u32 (640, 640 no `001.til`; ❓ uso) |
| `n` | u32 quadros (200) |
| larguras | u32[n] (64) |
| alturas | u32[n] (32). ✅ Conferido nos 128 mapas da instalação: a tabela **não é a altura do quadro** (alguns arquivos dizem 1, 16 ou 24 para quadros de 32 linhas); o cliente novo lê a altura das **linhas do quadro** e ignora a tabela. Alguns quadros no fim de um `.til` são lixo: a decodificação é por quadro, só quando o chão o pede, e um quadro ruim vira "nada desenhado" com log |
| offsets | u32[n], em palavras |
| `wordCount` | u32 |
| imagem | u16[wordCount]: cada quadro no **mesmo formato de linha do `.spr`** (RGB565, trechos com transparência; convertido para 555 em `0x00874fa0` como o `.spr`) |
| atributos | 2000 B, só se sobrarem ≥ 2000 B (o `001.til` tem 200 B e não lê) ❓ |

## `.obj` (objeto do mapa) ✅ `0x00b415b0`
| Campo | Tipo |
|---|---|
| nome | 20 B (vazio no `079_0101.obj`) |
| largura, altura | u16, u16 (1000, 900) |
| âncora x, y | u16, u16 (590, 333); no desenho entra `âncora mod 64` / `mod 32` |
| máscara | 400 B (❓ células cobertas) |
| imagem | u32 n + u16[n]: um quadro no formato de linha do `.spr` |
| segunda imagem | u32 m + u16[m] (❓ sombra ou topo) |

## Desenho (campo) ✅
Grade **retangular** de 64 × 32 px (os tiles já trazem a arte isométrica): a célula (cx, cy) fica em `(cx · 64 − câmeraX, cy · 32 − câmeraY)`.
1. **Chão** `0x00b36900`: para cada célula visível com tileset ≠ −1, desenha o quadro `+0x10` do `.til` `+0xC` direto na tela (`0x00b5db30`). Depois, nos mapas `< 0x6e`, um véu de cor `0x88bcf8` (`0x00b55110`, ❓ alpha).
2. **Objetos** `0x00b36630`: varre a janela com margem (10 células à esquerda, 15 acima, +20/+30) e põe cada objeto `+4` na **fila de desenho** (`0x00a0cd80(tipo 7, obj, x + âncoraX mod 64, y + âncoraY mod 32)`; tipo 0x1f no mapa `h` para `GR_CHT01.obj`).
3. **Efeitos de chão** (vtable `+8`, `0x00b380d0`) pelo tipo da célula, também na fila (`0x00a0cdf0`).
4. **Fila** (`0x00a0d340`): entradas `{tipo, y, x, ponteiro, ordem}`; ordenada por **y crescente e, no empate, pela ordem de entrada** (`0x00a0afc0`/`0x00a0a450`). Tamers, digimons e NPCs entram na mesma fila (y-sort com os objetos).

## Validação nas capturas
- 1696 posições capturadas (0x20 do jogador e 0x11 dos selvagens, 11 mapas).
- 95 % caem numa célula andável. Todas caem numa célula andável **ou vizinha** (8 direções) de uma andável.
- As que caem fora ficam na borda de obstáculos (árvores, pedras), por isso o servidor aceita "andável ou vizinha".

## Extração
O servidor não lê o `resource.pf`: `extract-client` gera o `grids.bin` (só o bit de andável). Ver
[arquivos de dados](arquivos-de-dados.md). 128 dos 140 mapas do `map_us` têm `.map`. Faltam a VIP Dungeon (111)
e os mapas de evento 157–168 (R153–R156).

## Nomes dentro dos arquivos ✅
Os nomes de tileset e de objeto dentro do `.map` (20 B, terminados em zero) estão em **CP949** (página de código coreana), como as strings do original: por exemplo `074_자갈.obj` ao lado de `074_01.obj`. O índice do pack guarda os mesmos nomes em UTF-16. O leitor (`LegacyText`) decodifica em 949; sem a página de código disponível cai em Latin-1 e só nomes ASCII funcionam. Teste: `MapFormatTests`. ❓ Sob NativeAOT a página 949 ainda não foi conferida com a instalação real: um objeto coreano que não carrega aparece no log como `LoadObject Error : map\… (not in the pack)`.
