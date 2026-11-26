# Enciclopédia (Global `0x7B` e `0x7C`)

A enciclopédia (janela `0xA8` no cliente, tabela `dex_us.txt` + recompensas `collection_us.txt`) é alimentada **exclusivamente** pelos pacotes `0x7B` e `0x7C` do **GlobalServer**, cifrados em **AES-128-ECB** com uma chave derivada do `0x0E` (Hello) da conexão global.

---

## 1. Cifra de sessão (AES-128-ECB) ✅

Evidência: `DigimonRPG.bin` (`0x00869CC0` key setup, `0x00869810` encrypt, `0x008697E0` decrypt, `0x00ACF629` handler do `0x0E` global) + verificação 31/31 conexões em `captures.db`.

1. Ao conectar ao GlobalServer, o servidor envia `0x0E` com 16 bytes de semente (`hello`).
2. O cliente deriva a chave AES-128 da sessão:

```
K = AES-128-ECB_Encrypt(key = C, block = hello XOR W)
```

Constantes fixas no binário do cliente:
- `W` (`0x010078A8`): `47 95 F1 C8 01 02 18 49 77 D6 26 E1 3E 4B C6 D2`
- `C` (`0x010078B8`): `1A 05 71 CD C1 92 88 F9 77 FF 2D 11 3D 4B CA DD`

3. Cada bloco de 16 bytes do payload de `0x7B` e `0x7C` (C2S e S2C) é cifrado/decifrado com `AES-128-ECB(K)`.

---

## 2. Pacote Global `0x7B` — Coleção permanente / flags por espécie ✅

Evidência: `DigimonRPG.bin` (`0x00AE3E10` C2S, `0x00ACF794` → `0x00C2B160` S2C).

- **C2S:** 1120 bytes (1104 bytes cifrados + 16 bytes de bloco extra do framing).
- **S2C:** **1104 bytes** (`0x450` = 69 blocos AES de 16 bytes).

| Offset | Tamanho | Tipo | Descrição |
|---|---|---|---|
| `0` | 4 | `u32` | `digimonUid` — UID do parceiro/digimon inicial enviado pelo cliente no C2S (ecoado pelo servidor). |
| `4` | 1100 | `u8[1100]` | `owned[speciesId]` (`0` ou `1`) para `speciesId` em `0..1099` (`0x44C` bytes). |

Comportamento no cliente (`0x00C2B160`):
- Percorre `speciesId = 0..1099`. Quando `owned[speciesId] == 1`, marca `Window(0xA8)->owned[speciesId] = 1` (`this + 0x1B64`) e também acende entradas relacionadas em `dex_us` (aliases/formas anteriores da linha evolutiva).
- Chama `0x00C313B0` (`Recalc`), que atualiza o flag `isOwned` de cada entrada da lista visual, a contagem total (`X/522`) e o percentual.
- **Não** é limpo por `0x00C31320` (`Reset`) — representa o histórico/coleção permanente.

---

## 3. Pacote Global `0x7C` — Time atual e suas evoluções ✅

Evidência: `DigimonRPG.bin` (`0x00AE3F50` C2S, `0x00ACF7CC` → `0x00C2B550` S2C) + sessões 56 e 58 de `captures.db`.

- **C2S:** 80 bytes (64 bytes cifrados de zeros + 16 bytes extras). Antes de enviar, o cliente chama `0x00C31320` (`Reset`), desmarcando apenas as espécies adicionadas pelo `0x7C` anterior (vetor `this + 0x448`).
- **S2C:** **64 bytes** (`0x40` = 4 blocos AES: struct de 60 bytes `0x3C` + 4 bytes zero de padding).

| Offset | Tamanho | Tipo | Descrição |
|---|---|---|---|
| `0` | 20 | `u32[5]` | UIDs dos 5 slots do time (`0` se slot vazio). |
| `20` | 10 | `i16[5]` | `baseSpecies[0..4]` — espécie base (In-Training/Rookie) de cada slot. |
| `30` | 10 | `i16[5]` | `championSpecies[0..4]` — espécie Champion desbloqueada de cada slot (`0` se não houver). |
| `40` | 10 | `i16[5]` | `ultimateSpecies[0..4]` — espécie Ultimate desbloqueada de cada slot (`0` se não houver). |
| `50` | 10 | `i16[5]` | `megaSpecies[0..4]` — espécie Mega desbloqueada de cada slot (`0` se não houver). |
| `60` | 4 | `u8[4]` | Zeros (padding para múltiplo de 16 bytes). |

Comportamento no cliente (`0x00C2B550`):
- Copia os 60 bytes para `Window(0xA8) + 0x1FB0` e `Player + 0xFF84`.
- Para cada slot `0..4`, marca todas as espécies presentes (`base`, `champion`, `ultimate`, `mega`) e seus aliases em `Window(0xA8)->owned[speciesId] = 1`, registrando-as no vetor `this + 0x448`.
- Chama `0x00C313B0` (`Recalc`).

---

## 4. Atualização durante o gameplay ✅

Evidência: `DigimonRPG.bin` — 46 chamadores em par de `0x00AE3E10` (C2S `0x7B`) e `0x00AE3F50` (C2S `0x7C`).

- O **próprio cliente** dispara o par `C2S 0x7B` + `C2S 0x7C` automaticamente:
  - Logo após o `0x0E` (Hello) do GlobalServer na entrada do mapa (`0x00ACF672` / `0x00ACF67F`).
  - Ao abrir a janela da enciclopédia (`0x00C2D4DA` / `0x00C2D4F3`).
  - Após captura, evolução, remoção/troca de Digimon e outras mutações de time no mapa.
- O servidor não precisa empurrar `0x7B`/`0x7C` de forma não solicitada; basta responder com o estado atualizado do personagem sempre que receber `C2S 0x7B` e `C2S 0x7C`.
- ✅ No emulador, não há tabela própria de enciclopédia: `0x7C` é derivado diretamente dos 5 slots atuais do time e `0x7B` ecoa o `uid` com `owned[speciesId] = 1` para as espécies (base e formas desbloqueadas) guardadas no [Digimon Depository](depository.md).
- O global chega no personagem por `PlayerSystem.FindByAccount` (na thread do mundo), não pelas sessões da conta. Sem personagem no mapa, `0x7B` vai sem espécies e `0x7C` com o time vazio.
