# Área inicial (tutorial) e time

Evidência: `captures.db`, sessões 53 (tutorial completo) e 56 (time de 5, trocas de líder).

## NPCs (`0x56`) ✅
C2S `u32 escolha` + 14 zeros; S2C eco `u32 escolha`. Cada escolha é feita uma vez (o emulador guarda a escolha como flag em `character_flag`).

| Escolha | O que o oficial manda depois do eco |
|---|---|
| 1–4 | `0xC9` com um manual (22004, 22005, 22006, 22007) + `0xFC` |
| 5 (Tunomon) | tira 5 Yuramon's Seeds (41) e dá a carta 22008: `0xC9` com as duas posições (sementes que sobraram, carta) + `0xFC` |
| 8 (fim) | **antes do eco**, `0xB0` para o mapa 1 (58, 82); depois `0xC8` (Woodmon's Leaf × 50 na bolsa de 76), `0xC9` (24171, 24172), `0xFC`, os 7 `0x7E` e 7 `0x7F` da entrada |
| 10 (sessão 56) | ❓ eco `0x63` (99), talvez "indisponível" |

As sementes vêm dos Yuramon do mapa 79 (duas por vitória). ❓ O que o oficial faz numa escolha repetida ou sem as sementes.

## Time ✅
- 5 posições (os 5 blocos do `0x02`); pode haver buraco. Posição 0 = líder.
- `0xF1` C2S `u32 0, u32 personagem, u32 digimon` → S2C `u32 1, u32 personagem, u32 digimon`: o digimon troca de lugar com o líder.
- `0x173` C2S `u16 0, u16 botão k` (0–2) → S2C `u16 1, u16 k, u16 k, u16 k + 1`: troca as posições k + 1 e k + 2, mesmo com uma vazia.
- Recuperação no mapa vale para o time inteiro: um `0xB2` por digimon que mudou (rajadas de até 5).

## No emulador
`Game/World/NpcScripts.cs`, `Game/Entities/Character.cs` (`Party`, `SetLeader`, `SwapSlots`, flags), handlers em
`Network/Handlers/MapHandlers.cs`. Banco: `digimon.slot` (migração `0003`). Testes: `TutorialTests`.
