# Mapas do cliente (`.map`)

Cada mapa do `map_us` aponta, na coluna 4, para `map\<nome>.map` no `resource.pf` (ex.: `R079` → `map\r079.map`;
o nome não diferencia maiúsculas). A entrada é só zlib, sem o codec das tabelas.

## Formato
| Offset | Tipo | Campo |
|---|---|---|
| 0 | u32 | largura em células (✅ = limite de x no protocolo; mapa 2 = 176) |
| 4 | u32 | altura em células (✅ = limite de y; mapa 2 = 263) |
| 8 | u32, u32 | 64, 32: tamanho do tile em pixels (isométrico) |
| 16 | 24 B × largura × altura | células, linha a linha (`y · largura + x`) |
| … | | lista de arquivos `.til`/`.obj` usados (nomes de 21 bytes) |

Célula (6 × u32):
| Campo | Conteúdo |
|---|---|
| 0–2 | ❓ (`-1` na maioria; índices de objeto?) |
| 3 | ❓ índice de tile |
| 4 | ❓ |
| 5 | **bit 0 = andável** ✅; bit 8 ❓ (256/257 em ~3 % das células) |

- O `r001.map` (e outros antigos) tem lixo `0xBAADF0xx` nos bits altos dos campos; o bit 0 continua valendo.
- A ordem `y · largura + x` foi escolhida porque bate melhor com as posições capturadas que `x · altura + y`.

## Validação nas capturas
- 1696 posições capturadas (0x20 do jogador e 0x11 dos selvagens, 11 mapas).
- 95 % caem numa célula andável. Todas caem numa célula andável **ou vizinha** (8 direções) de uma andável.
- As que caem fora ficam na borda de obstáculos (árvores, pedras), por isso o servidor aceita "andável ou vizinha".

## Extração
O servidor não lê o `resource.pf`: `extract-client` gera o `grids.bin` (só o bit de andável). Ver
[arquivos de dados](arquivos-de-dados.md). 128 dos 140 mapas do `map_us` têm `.map`. Faltam a VIP Dungeon (111)
e os mapas de evento 157–168 (R153–R156).
