# Títulos de Digimon (`0x97` / `0x99`)

Evidências: `captures.db` (sessão 56, marca 72 — `conn=55, 57, 76, 112, 141, 162`), `DigimonRPG.bin` (`0x00AC47DA` e `0x00AC48C4`) e `table\digimon_title_us.dat` (236 títulos).

---

## 1. Pacotes do Protocolo ✅

| Opcode | C2S | S2C | Layout e Comportamento |
|---:|---:|---:|---|
| `0x4F` (`SystemMessage`) | — | **257 B** | **Aviso cinza de título obtido no chat (18 capturas v322: `conn=92/100/137/145/205/408/480`).**<br>• S2C: `u8 1`, `char[256]` `"You gained the Title of [<Nome>]."` |
| `0x97` (`DigimonTitleList`) | 1622 B | **1608 B** | **Abrir janela de títulos do Digimon.**<br>• C2S: `u32 2`, `u32 digimonUid`, zeros.<br>• S2C (`0x00AC47DA`): `u32 result` (`1` ok / `0` erro), `u32 digimonUid`, seguido de **`200 × 8 B`** `(u32 titleId, u32 1)` para cada título desbloqueado (restante zerado). Abre as janelas `0x69` e `0x12A` no cliente. |
| `0x99` (`DigimonTitleEquip`) | 26 B | **12 B** | **Equipar / desequipar título do Digimon.**<br>• C2S: `u32 2`, `u32 digimonUid`, `u32 titleId` (`0` = desequipar).<br>• Servidor responde na ordem (`conn=76/141`): `0xB4` (bloco atualizado), `0x10` (broadcast na zona se for o líder) e `0x99` (`u32 1`, `u32 digimonUid`, `u32 titleId`). |

---

## 2. Campos nos Blocos de Status e Resumo ✅

- **`DigimonStatusBlock` (`0x02`, `0x63`, `0x64`, `0x70`, `0x84`, `0xB2`, `0xB4`):**
  - `+320` (`u32`): `TitleId` equipado (`0` se nenhum).
  - `+324` (`u32`): `1` quando há título equipado, `0` caso contrário.
  - `+328..343` (`i32 × 4`): bônus de `STR`, `AGI`, `CON`, `INT` concedidos pelo título (confirmado em `pid=80571` com `20030` *Endless Journey* → `(15, 15, 15, 15)` e em `pid=3388` com `20100` *First Win* → `(1, 0, 0, 0)`).
- **`RefreshCharacterInfo` (`0x10`):**
  - `+100` (`u32`): `Leader.TitleId` (exibe o título do líder no mapa para todos os jogadores).

---

## 3. Desbloqueio Automático e Persistência

- **Catálogo:** `Resources/Digimon/DigimonTitles.xml` (235 títulos, gerado por [`tools/extract_digimon_titles.py`](../ferramentas/extract-digimon-titles.md)): nome, bônus em `<Modifier>` e regra em `<Unlock>` ([dados](../emulador/dados.md#regras-de-desbloqueio-gameworldtitleunlockscs)).
- **Marcos automáticos (`<Unlock>`, avaliados por `Game/World/DigimonTitles.CheckUnlocks` em ordem de id):**
  - **Vitórias (`TotalWins`):** `20100` (1), `20110` (10), `20120` (100), `20130` (1000), `20140` (10000).
  - **Derrotas (`TotalBattles - TotalWins`, incluindo fuga):** `20200` (1), `20210` (100), `20220` (1000), `20230` (10000).
  - **Nível / Mega:** `20000` (Mega / Lv. 41), `20010` (Lv. 100), `20020` (Lv. 110), `20030` (Lv. 120), `20040` (Lv. 130), `20050` (Lv. 140), `30402` (Lv. 150 — *Best Partner*), `30444` (Lv. 160 — *Beyond Evolution*).
- **Comando GM:** `/givetitle <titleId>` desbloqueia qualquer título de `digimon_title_us.dat` no líder.
- **Banco de Dados (`sql/upgrade/0007_digimon_titles.sql`):** coluna `digimon.title_id` + tabela `digimon_title (digimon_uid, title_id)`.
