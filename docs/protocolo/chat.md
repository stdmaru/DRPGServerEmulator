# Chat, Avisos e Mensagens de Sistema

Evidência: engenharia reversa de `DigimonRPG.bin` (v322) — dispatcher do Map Server (`0x00ABC438`), dispatcher do Global Server (`0x00ACDC80`) e método da janela de chat (`0x00B86B20` / `0x00B869A0` sobre o singleton `0x01158508`).

---

## 1. Map Server (`ServerRole.Map`) ✅

| Opcode | Nome | Payload S2C | Layout | Comportamento no cliente (`DigimonRPG.bin`) |
|---:|---|---:|---|---|
| `0x21` | `ChatNormal` | **277 B** | `char[21]` remetente + `char[256]` mensagem | `0x00ABE221 → 0x00AEDFF0`. Formata `[%s] %s`. Se o remetente existir no mapa, abre balão de fala; caso contrário (`0x00AEE69D`), imprime direto no chat em **branco** (`245, 245, 245`) sem balão. |
| `0x22` | `ChatNotice` | **277 B** | `char[21]` remetente + `char[256]` mensagem | `0x00ABE247 → 0x00AF18C0`. Mesmo layout de 277 B. Quando o remetente não existe no mapa (`0x00AF19D7 → 0x00B869A0`), imprime `[%s] %s` direto na janela de chat em **cor de destaque** (`0, 255, 255` — ciano/amarelo), sem balão de fala. |

> [!TIP]
> Como tanto `0x21` quanto `0x22` são enviados no canal TCP do Map Server, basta chamar `player.Send(...)` para entregar a mensagem **exclusivamente para um único jogador** (sem broadcast para a zona).

---

## 2. Global Server (`ServerRole.Global`) ✅

| Opcode | Nome | Handler (`DigimonRPG.bin`) | Descrição |
|---:|---|---|---|
| `0xC8` | `ChatWhisper` | `0x00ACECE9 → 0x00ACEE11 / 0x00ACEF00` | Whisper privado entre jogadores. Imprime no chat via `0x00B86B20` em rosa/lilás (`255, 159, 236` / `210, 102, 255`). |
| `0xCA` | `ChatMegaphone` | `0x00ACF039 → 0x00ACF1BC` | Megafone global no chat via `0x00B86B20`. |
| `0xCC` | `PlayerEvent` | `0x00ACDE87` | Notificações de eventos de jogador (switch de 6 casos em `0x00ACF95C`). |
| `0xE6` | `ChatNotice` | `0x00ACF3A4 → 0x00ACF400..0x00ACF511` | Aviso/anúncio global de sistema/GM. Possui um byte de tipo (`4..7`) que escolhe a cor e escreve tanto na janela de chat (`0x00B86B20`) quanto no banner superior (`0x00B86A60`):<br>• `4`: vermelho (`255, 0, 0`)<br>• `5`: laranja (`255, 115, 47`)<br>• `6`: azul-ciano (`20, 128, 255`)<br>• `7`: magenta (`200, 0, 255`) |

---

## 3. Echo de Debug para Administradores no Emulador

Em `Game/Entities/Player.cs`:
- `player.IsAdmin` → `Account.Authority >= Authority.Administrator`.
- `player.SendMessage(sender, message)` → envia `ChatNormalPacket` (`0x21`, 277 B) apenas para o jogador.
- `player.SendNotice(sender, message)` → envia `ChatNoticePacket` (`0x22`, 277 B) apenas para o jogador.
- `player.SendDebug(message)` → envia `0x22` com remetente `"[DEBUG]"` **apenas se `player.IsAdmin` for `true`**.

### Pontos instrumentados com `player.SendDebug(...)`
- **`NpcScriptRegistry.Choose`:** avisa quando um `choiceId` de `0x56` não está registrado ou foi chamado de um mapa diferente do esperado.
- **`PacketDispatcher.DispatchAsync`:** avisa quando o cliente envia um opcode de Map sem handler registrado.
- **`ItemUseHandler` (`0x41`):** avisa quando um item usado não possui handler implementado.
- **`Battle.RequestAction` (`0x35`):** avisa quando um `BattleIcon` não está tratado.
