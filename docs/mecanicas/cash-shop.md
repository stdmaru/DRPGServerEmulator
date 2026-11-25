# Cash Shop e Cash Depository

Especificação técnica levantada a partir das capturas oficiais v322 (`captures.db`, sessões 53, 56 e 62), das tabelas do cliente (`table.hf` / `table.pf`) e da engenharia reversa de `DigimonRPG.bin`.

---

## 1. Visão Geral do Fluxo

1. **Abrir Cash Shop (`0x0159`)**: o cliente envia `C2S 0x0159` pedindo abertura da loja e saldo de Cash; o servidor responde `S2C 0x0159` com `accountNo`, `cash1` (cash real) e `cash2` (cash bônus). O cliente soma `cash1 + cash2` e abre a janela `0x12C`.
2. **Histórico de Compras (`0x0161`)**: o cliente pode consultar o histórico de compras (`C2S/S2C 0x0161`, até 20 registros de 48 bytes).
3. **Comprar Produto (`0x0160`)**: o cliente envia `C2S 0x0160` com `productId` (da `ingameshop_itempage_us.dat`) e `quantity`. O servidor valida preço/saldo na tabela autoritativa, debita o Cash da conta, entrega os itens/cards do produto no **Cash Depository** da conta e responde `S2C 0x0160` com o novo saldo (`cash1After` e `cash2After`).
4. **Cash Depository (`0x00CA`, `0x00DE`, `0x00DD`)**:
   - `0x00CA` autentica/abre a janela `0x46` do depósito de Cash.
   - `0x00DE` lista os 24 slots de Cards + 24 slots de Items guardados no Cash Depository da conta.
   - `0x00DD` retira um Card (`kind=1`) ou Item (`kind=2`) do Cash Depository para a mochila do personagem, seguido de `S2C 0x00C8` (card) ou `S2C 0x00C9` (item).

---

## 2. Tabelas do Cliente (`table.hf` / `table.pf`)

- ✅ `table\ingameshop_itempage_us.dat` (887 linhas): catálogo de produtos exibidos na loja.
  - Col. `0`: `ProductId` (ex.: `100001`, `100002`, ...)
  - Col. `1`: `Name`
  - Col. `2`: `ItemDataId` (chave estrangeira para `ingameshop_item_data_us.dat` col. `0`)
  - Col. `5`: `Price` (preço unitário em Cash)
  - Col. `8`: categoria/aba (`1` = 689 itens, `2` = 87 pacotes, `3` = 111 especiais)
- ✅ `table\ingameshop_item_data_us.dat` (1.408 linhas): conteúdo de cada `ItemDataId` (um produto pode ter 1 ou vários itens/cards).
  - Col. `0`: `ItemDataId`
  - Col. `1`: `Kind` (`1` = Card em `card_info_us.dat`; `2` = Item em `item_info_us.dat`)
  - Col. `3`: `ItemId` / `CardId`
  - Col. `4`: `Amount` (quantidade entregue por unidade comprada)

---

## 3. Protocolo — Cash Shop

### `Map 0x0159` — Abrir Cash Shop / Consultar Saldo ✅
- **Evidência**: `captures.db` (sessão 53 mark 11, conn 63/92/137/230/234) + `DigimonRPG.bin @ 0x00ACA318`, `0x0097C610`, `0x00AD57DF`.
- **Tamanho do payload**: `64` bytes (`0x40`; `C2S` envia 78 B com trailer de 14 B).

| Offset | Tipo | C2S | S2C |
|---:|---|---|---|
| `+0x00` | `u32` | ponteiro/vtable do cliente | ecoado ou constante |
| `+0x04` | `i32` | `0` | `errorCode` (`0` = sucesso; `!= 0` exibe mensagem de erro e não abre a loja) |
| `+0x08` | `u8` | `1` (request) | `2` (response) |
| `+0x10` | `i32` | `0` | `accountNo` (armazenado pelo cliente em `[InGameShop+0x1180]` e reenviado em `0x0160`/`0x0161`) |
| `+0x18` | `i32` | `0` | `cash1` (Cash principal) |
| `+0x20` | `i32` | `0` | `cash2` (Cash bônus; o cliente exibe `cash1 + cash2` em `[InGameShop+0x1548]`) |
| `+0x28` | `char[20]` | `username` (NUL-terminated) | `username` (ecoado) |

### `Map 0x0160` — Comprar Produto no Cash Shop ✅
- **Evidência**: `DigimonRPG.bin @ 0x0096C330` (builder C2S), `0x00ADFCC0` (send C2S), `0x00ACA380` (handler S2C).
- **Tamanho do payload**: `56` bytes (`0x38`).

| Offset | Tipo | C2S | S2C |
|---:|---|---|---|
| `+0x00` | `u32` | vtable (`0x0100FDBC`) | ecoado / `0` |
| `+0x04` | `i32` | `0` | `errorCode` (`0` = sucesso) |
| `+0x08` | `u8` | `1` (buy) | `2` (response) |
| `+0x10` | `i64` | `accountNo` | `accountNo` |
| `+0x18` | `u32` | `productId` (`ingameshop_itempage_us.dat` col. `0`) | `productId` |
| `+0x1C` | `u32` | `quantity` (unidades compradas) | `quantity` |
| `+0x20` | `i64` | `unitPrice` (apenas informativo — **ignorar** e recalcular no servidor) | `unitPrice` |
| `+0x28` | `i64` | `0` | `cash1After` (saldo principal restante) |
| `+0x30` | `i64` | `0` | `cash2After` (saldo bônus restante; cliente atualiza `[InGameShop+0x1548] = cash1After + cash2After` e abre confirmação `0x12E` modo `3`) |

### `Map 0x0161` — Histórico de Compras do Cash Shop ✅
- **Evidência**: `captures.db` (conn 137/399) + `DigimonRPG.bin @ 0x00ACA3E9`, `0x0097C6E0`, `0x00ADFD50`.
- **Tamanho do payload**: `992` bytes (`0x3E0` = `32` B cabeçalho + `20 × 48` B registros).
- `+0x04` (`i32`): `errorCode` (`0` = sucesso).
- `+0x08` (`u8`): `1` (C2S) / `2` (S2C).
- `+0x0C` (`u16`): ano; `+0x0E` (`u16`): mês; `+0x10` (`u16`): página solicitada; `+0x12` (`u16`): total de páginas.
- `+0x18` (`i64`): `accountNo`.
- `+0x20..+0x3DF` (`960` B): até 20 registros de compra de 48 bytes (`0x30`) cada (`❓` layout interno do registro ainda a detalhar se necessário).

---

## 4. Protocolo — Cash Depository (Depósito de Itens de Cash)

### `Map 0x00CA` — Abrir / Autenticar Cash Depository ✅
- **Evidência**: `captures.db` (sessão 56 marks 61/75, conn 121/137/141) + `DigimonRPG.bin @ 0x00AC0DDD`.
- **Tamanho do payload**: `103` bytes (`C2S` envia 117 B com trailer de 14 B).

| Offset | Tipo | Descrição |
|---:|---|---|
| `+0x00` | `char[21]` | `username` (NUL-terminated) |
| `+0x15` | `char[80]` | MD5 da senha secundária (vazio = `d41d8cd98f00b204e9800998ecf8427e`) |
| `+0x65` | `u8` | `result` (`1` = sucesso; no `C2S` vem `0`) |
| `+0x66` | `u8` | `action` (`1` = abrir janela `0x46`; ao receber `action=1, result=1`, o cliente solicita `C2S 0x00DE`) |

### `Map 0x00DE` — Listar Cash Depository ✅
- **Evidência**: `captures.db` (conn 121/137/141) + `DigimonRPG.bin @ 0x00AC010D`, `0x00AFCD30`.
- **Tamanho do payload**: `4420` bytes (`0x1144`; `C2S` envia 4434 B zerados).

| Offset | Tamanho | Descrição |
|---:|---:|---|
| `+0x0000` | `1824` B (`24 × 76`) | 24 slots de **Cards** (`CardRecord`) |
| `+0x0720` | `2592` B (`24 × 108`) | 24 slots de **Items** (`ItemRecord`) |
| `+0x1140` | `4` B (`u32`) | `result` (`1` = sucesso) |

### `Map 0x00DD` — Retirar Card ou Item do Cash Depository ✅
- **Evidência**: `captures.db` (conn 121/141) + `DigimonRPG.bin @ 0x00AC014A`, `0x00AFD060`.
- **Tamanho do payload**: `192` bytes (`0xC0`; `C2S` envia 206 B).

| Offset | Tamanho | Descrição |
|---:|---:|---|
| `+0x00` | `4` B (`u32`) | `kind`: `1` = Card, `2` = Item |
| `+0x04` | `76` B | `CardRecord` (preenchido quando `kind == 1`, zeros caso contrário) |
| `+0x50` | `108` B | `ItemRecord` (preenchido quando `kind == 2`, zeros caso contrário) |
| `+0xBC` | `4` B (`u32`) | `result`: `0` = falha (`0xE69`), `1` = sucesso (`0xE6A`), `2` = inventário cheio (`0xE6B`), `3` = erro/não encontrado (`0xE6C`) |

Em caso de sucesso (`result == 1`), o servidor envia `S2C 0x00DD` (com o registro retirado para o cliente limpar o slot correspondente por `Uid`) seguido imediatamente por `S2C 0x00C8` (se card) ou `S2C 0x00C9` (se item) com o item já alocado na mochila do personagem.

---

## 5. Plano de Implementação

1. **Banco de Dados (`sql/upgrade/0010_cash_shop.sql`)**:
   - Adicionar coluna `cash` (`INT UNSIGNED NOT NULL DEFAULT 0`) em `account` (ou `cash` + `bonus_cash`).
   - Criar tabela `cash_depository_item` (`account_uid`, `kind` [1=card, 2=item], `slot` [0..23], `uid`, `item_id`, `amount`, `expires_at`, PK `(account_uid, kind, slot)`).
2. **Catálogo (`GameData` / `ClientTables`)**:
   - Carregar `ingameshop_itempage_us.dat` e `ingameshop_item_data_us.dat` em `GameData.CashShopProducts`.
3. **Repositório e Estado (`CashRepository` / `CashShopSystem`)**:
   - Consulta e débito atômico de Cash por conta + leitura/inserção/remoção no `cash_depository_item` (fora da thread do mundo via banco ou com cache sincronizado na conta).
4. **Pacotes e Handlers (`Map`)**:
   - `CashShopPackets.cs` / `CashShopHandlers.cs`: `0x0159` (abrir/saldo), `0x0160` (comprar), `0x0161` (histórico).
   - `CashDepositoryPackets.cs` / `CashDepositoryHandlers.cs`: `0x00CA` (abrir), `0x00DE` (listar), `0x00DD` (retirar).
   - Guards server-side: bloquear compra/retirada em batalha, validar `productId` no catálogo, `quantity >= 1` sem overflow, saldo suficiente, espaço suficiente no Cash Depository (24 cards / 24 items) e na mochila do personagem, além de comando GM `/cash <valor>` para testes.
5. **Testes Unitários (`CashShopTests.cs`)**:
   - Serialização/tamanhos de todos os pacotes (`0x0159`, `0x0160`, `0x0161`, `0x00CA`, `0x00DE`, `0x00DD`), fluxo de compra → depósito → retirada, e guards de segurança (saldo insuficiente, depósito cheio, inventário cheio, em batalha, produto inexistente, quantidade inválida).
