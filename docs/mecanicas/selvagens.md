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
| 44 | u8 | ✅ grau do grupo: `2` = Beginner (comum), `3` = Veteran (exclusivo, mais forte) — mesmo valor do byte 300 do bloco de status |
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
- **Batalha:** ✅ o grupo comum continua andando (a batalha é instanciada); só o grupo exclusivo fica em 11. Ver [batalha](batalha.md).
- **Derrota** (exclusivo): 8 por ~10,4 s, depois 12, e o serial some. Volta como grupo novo (serial novo). ❓ Só 2 amostras.
- 98 % das posições dos selvagens são andáveis; o resto fica encostado em área andável.

## Emulador (`Game/World/Zone.cs`, `Game/Entities/WildDigimon.cs`)
- Tick de 344 ms, chance de 5 %, destino sorteado na caixa da casa **e andável** (até 8 tentativas).
- Casa: `<Point>` no `DigimonSpawns.xml` (capturado) ou célula andável sorteada no retângulo.
- Grupo comum não sai do mapa na batalha; `Exclusive="true"` no `<Spawn>`: 11 na batalha e, se derrotado, 8 por 10,4 s, 12, e um grupo novo na mesma casa depois do `RespawnTime`.

## ✅ Status e recompensas
- Os status só dependem do nível (o `0x63` manda os atributos zerados), exatos em 21 combinações espécie × nível de L1 a L26:
  **HP ⌊(13L + 220)/2⌋, VP 25L + 25, ATK 6L + 74, DEF 3L + 60, BR 5L + 80** (`Game/Stats/WildFormulas.cs`).
- Exceções vão no `DigimonSpawns.xml` (HP, VP, ATK, DEF, BR juntos): o Yuramon L2 do tutorial tem 96/50/60/43/65. ❓ Chefes (byte 300 = 3) fogem da fórmula.
- Bits: ✅ o oficial sorteia um inteiro de `⌊¾·máx⌋` até o máximo, onde `máx = 2·L + 3` nos Beginner (verificado de L1 a L26: L1 3–5, L2 5–7, L3 6–8, L4 8–11, L7 12–16, L10 17–23, L16 27–35, L17 31–37, L26 41–55).
- EXP: ✅ todas as amostras v322 sem teto de nível são múltiplos exatos de **13** (evento +1200% = 13×), revelando a EXP base oficial por nível (L1 = 10, L3 = 12, L4 = 14, L6 = 18, L7 = 20, L10 = 28, L11 = 32, L12 = 36, L15 = 50, L16 = 56, L17 = 62, L26 = 159), com redução pela metade quando o digimon do jogador está vários níveis acima. EXP do Yuramon do tutorial: 0.
- Drop: cada item do grupo é sorteado à parte (o Yuramon do tutorial dá duas Yuramon's Seeds).

## Mapas populados (`Resources/Map/DigimonSpawns.xml`)
- ✅ **Mapa 79 (`Village of Beginning`)**: 3 grupos (Yuramon L2 do tutorial).
- ✅ **Mapa 2 (`West of Administration Bureau`, Beginner Lv. 1–6)**: 54 grupos (8 espécies, sessões 54, 56, 61 e 62).
- ✅ **Mapa 4 (`North of Theme Park`, Beginner Lv. 7–10)**: 93 grupos (10 espécies, sessões 56, 58 e 62).
- ✅ **Mapa 46 (`House of Gennai`)**: 8 grupos (7 Goburimon L11 + 1 V-mon(Tamed) L11 exclusivo, sessão 56).

## ❓ Em aberto
- Demais mapas (ex.: Mapas 3, 6, 7, 8, 9, 10–12, 40, 44 e regiões avançadas): faltam travessias completas em `captures.db` (e/ou batalhas para validar fórmula de status dos monstros `Veteran`/`Elite`/`Legend`, byte 44 ≥ 3).
- Taxas reais de drop dos monstros fora do tutorial.
- Se o oficial manda o 0x11 apenas para selvagens próximos (ao atravessar um mapa grande, só chegam os grupos ao redor do jogador).
- Respawn: tempo e se a casa é sempre a mesma.
