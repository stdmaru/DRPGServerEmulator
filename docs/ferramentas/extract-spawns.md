# extract_spawns.py

`work/tools/extract_spawns.py`: lê as sessões v322 do `captures.db` do [proxy](proxy.md) junto com o `grids.bin` do cliente para medir a consistência das amostras de cada mapa e gerar/atualizar o `src/DRPGServer/Resources/Map/DigimonSpawns.xml`.

Quanto mais sessões, passagens e batalhas houver no `captures.db`, mais fiéis ficam as casas (`<Point>`), composições de grupo (`AppearanceRate`), EXP base, Bits e stats especiais (`Veteran` / tutorial).

## Uso

### 1. Dry-run (nível de consistência por mapa, sem gravar nada)
```bash
python3 /media/games/DigimonRPG/work/tools/extract_spawns.py --dry-run
```
Imprime uma tabela com todos os mapas visitados nas capturas v322, mostrando:
- **Cob**: porcentagem da área andável real (`grids.bin`) coberta pelo raio de visão do jogador;
- **Casas**: porcentagem dos grupos cuja casa `7 × 7` já convergiu (`≥ 2` visitas ou caixa de movimento `≥ 4` em ambos os eixos);
- **Spots (B/V)**: grupos `Beginner` (`byte 44 = 2`) e `Veteran` (`byte 44 = 3`);
- **Bat**: número de batalhas (`0x63`) capturadas no mapa;
- **Score (0–100%) e Nível**:
  - `COMPLETO` (`≥ 90%`, sem Veteran pendente): não precisa de mais amostras;
  - `BOM` (`75–89%`, sem Veteran pendente): seguro para gerar spawns;
  - `PARCIAL` (`40–74%`): faltam setores ou batalhas Veteran;
  - `INSUFICIENTE` (`< 40%` ou cobertura `< 25%`): poucas amostras;
- **O que falta**: indica exatamente quais quadrantes (`NO`, `NL`, `SO`, `SL`) explorar ou quais espécies `Veteran` enfrentar em batalha.

### 2. Gerar XML (visualizar no terminal ou gravar no `DigimonSpawns.xml`)
```bash
# Imprime o XML dos mapas escolhidos no terminal
python3 /media/games/DigimonRPG/work/tools/extract_spawns.py --maps 79 2 4 46

# Grava direto em src/DRPGServer/Resources/Map/DigimonSpawns.xml
python3 /media/games/DigimonRPG/work/tools/extract_spawns.py --maps 79 2 4 46 --write

# Seleciona automaticamente todos os mapas com score >= 75% (dry-run primeiro)
python3 /media/games/DigimonRPG/work/tools/extract_spawns.py --dry-run --min-score 75
python3 /media/games/DigimonRPG/work/tools/extract_spawns.py --min-score 75 --write
```

### Opções úteis
- `--include-veteran`: inclui grupos `Veteran` (`byte 44 = 3`) que já possuam batalha `0x63` capturada com stats reais (grupos sem batalha são omitidos por segurança).
- `--exp-divisor N`: divisor do bônus de evento de EXP ativo nas capturas (padrão `13`, correspondente a `+1200%`).
- `--db`, `--grids`, `--table`: caminhos alternativos para `captures.db`, `grids.bin` e `table.hf`.

## Como o refinamento funciona
- **MapId**: resolvido sem ambiguidade cruzando `S2C 0x12` do Channel Server (offset `+113`) com `S2C 0xB0` do Map Server; pacotes `0x11` enviados entre `0xB0` e `C2S 0x04` são descartados.
- **Casas (`<Point>`)**: cada visita faz casamento 1-para-1 sem fundir dois seriais simultâneos da mesma visita, acumulando os pontos `(x, y)` de cada spot entre sessões; quando a caixa atinge largura/altura 6, o centro converge para a casa exata. Todo ponto é validado contra `grids.bin`.
- **Recompensas e stats**: compara `0x63` contra `WildFormulas`; divergências viram `HP`/`VP`/`ATK`/`DEF`/`BR` explícitos. Bits usam `2·L + 3` (ou máximo observado com `≥ 8` amostras) e EXP usa a EXP base observada sem teto de nível (`÷ 13`).
- **Exclusividade**: `Exclusive="true"` é marcado automaticamente se o grupo entrou nos estados `8`, `11` ou `12` no `0x11` (ou se `byte 44 ≠ 2`).
