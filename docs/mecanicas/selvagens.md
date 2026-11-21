# Digimons selvagens no mapa

Dados: 592 grupos e 72 mil pacotes `0x11` nas sessões 53–58 (v322).

## 0x11 (mapa → cliente), 56 bytes
| Offset | Tipo | Campo |
|---|---|---|
| 0 | 16 B | serial do grupo |
| 16 | 21 B | nome do líder |
| 37 | u8 | 0 |
| 38 | u16 | espécie |
| 40 | u16 | ✅ estágio (coluna 2 do `digimon_us`) |
| 42 | u16 | nível |
| 44 | u8 | ✅ 2 em todos (como o byte 300 do bloco de status do selvagem) |
| 45 | u8 | ✅ estado: 1 parado, 4 andando, 11 em batalha, 8 derrotado, 12 saindo do mapa |
| 46 | u16 | direção (enum `Direction`) |
| 48 | i16, i16 | posição |
| 52 | i16, i16 | destino (igual à posição quando parado) |

## Comportamento oficial
- **Tick:** o servidor manda o 0x11 de **todos** os selvagens a cada ~**344 ms**, parados ou não.
- **Casa:** cada grupo tem um ponto fixo e anda só numa caixa de **7 × 7** em volta dele (casa ± 3). As sessões 53 e
  54 mostram as mesmas casas no mapa 79.
- **Andar:** parado, a cada tick há ~**5 %** de chance de começar (3276 de 65 462 ticks), ou ~6,9 s em média
  parado. O destino é uma célula da caixa; o vetor vai até ±6 por eixo.
- **Sequência de um movimento:**
  1. 4 com posição antiga e destino;
  2. no tick seguinte, 4 com a posição já no destino e a direção nova;
  3. no próximo, 1.
- **Direção:** o sinal de (dx, dy): (0,+1)=0, (−,+)=1, (−,0)=2, (−,−)=3, (0,−)=4, (+,−)=5, (+,0)=6, (+,+)=7.
  Ela só muda no pacote de chegada (96 %).
- **Batalha:** 11 enquanto dura.
- **Derrota:** 8 por ~10,4 s, depois 12, e o serial some. Volta como grupo novo (serial novo). ❓ Só 2 amostras.
- 98 % das posições dos selvagens são andáveis; o resto fica encostado em área andável.

## Emulador (`Game/World/Zone.cs`, `Game/Entities/WildDigimon.cs`)
- Tick de 344 ms, chance de 5 %, destino sorteado na caixa da casa **e andável** (até 8 tentativas).
- Casa: `<Point>` no `DigimonSpawns.xml` (capturado) ou célula andável sorteada no retângulo.
- Derrotado: 8 por 10,4 s, 12, e um grupo novo na mesma casa depois do `RespawnTime`.

## ❓ Em aberto
- Casas e espécies de todos os mapas: as capturas têm muitos grupos, mas a conexão nem sempre diz o mapa. Hoje só o
  mapa 79 está no `DigimonSpawns.xml`.
- Se o oficial manda o 0x11 de selvagens fora da tela.
- Respawn: tempo e se a casa é sempre a mesma.
