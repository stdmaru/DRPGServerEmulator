# Captura de digimons e renomeação

Há duas formas de capturar. Nas duas o jogador precisa de uma posição livre no time (5 posições). O digimon novo:
- ✅ se chama `InputName` e tem **uma renomeação grátis**;
- ✅ começa no nível de entrada do estágio: in-training L1, rookie L11 (mesmo com o selvagem em L31);
- ✅ chega com `0xEF` (696 bytes, bloco de status do digimon novo; o byte 24 do bloco é o estágio).

Evidência: amostras em `work/packets/item_packet/` (Taming Net) e sessão 56 (captura comum, 21 tentativas, todas falhas).

## Taming Net (item, no mapa)
- O jogador clica com o botão direito na rede e depois no selvagem do mapa. Só funciona **fora de batalha**.
- Cada rede só captura o próprio estágio. A v322 só tem redes genéricas de in-training e rookie:

| Item | Nome | Estágio |
|---|---|---|
| 557 | Taming Net : In-Training | in-training |
| 10022 | Taming Net : In-Training X | in-training ✅ amostra |
| 10025 | Taming Net : Rookie | rookie |
| 10026 | Taming Net : Rookie X | rookie ✅ amostra |
| 10027 | Taming Net : Rookie S | rookie |
| 23730 | Taming Net : Rookie S (X) | rookie |

- ❓ Chance: são itens de cash e tratadas como garantidas (100 %). A tabela fica em `Resources/Item/TamingNets.xml`
  (`ItemID`, `Stage`, `Chance`). Para um item novo, basta uma linha nesse arquivo.

### Pacotes
- C2S `0x41` (822): `u32 3`, registro do item (108 bytes, uid em +4 do payload), bloco do alvo (696 bytes em +112). No
  selvagem, o uid do bloco é 0 e o serial do grupo fica em +116.
- ✅ Sucesso: `0xEF` com o bloco do novo e depois o S2C `0x41` (808 bytes): `u32 3`, o registro depois do uso (quantidade 0
  quando acaba) e o bloco do novo.
- Recusa (em batalha, estágio errado, time cheio, sem selvagem): `0xC3` vazio. ❓ O código de recusa do oficial não foi
  capturado.
- ✅ O grupo selvagem continua no mapa depois da captura.

## Captura comum (na batalha)
- Ícone `0x01` no `0x35`, com o alvo igual ao serial do selvagem.
- ✅ Até **3 tentativas por batalha** (o cliente não pede a quarta).
- Só captura **in-training**. O jogador precisa ter um digimon **rookie ou acima** no time.
- ❓ A chance real é muito baixa. No emulador, `Game:CaptureChance` vale 0,10 por padrão (para testes).
- ✅ Na execução o oficial manda `0x68`, `0xB3` (744 bytes) e `0x35` 3, e depois a animação.
- `0xB3`:

| Offset | Campo |
|---|---|
| 0 | `u32` resultado: ✅ `1` = sucesso, `2` = falhou (`DigimonRPG.bin @ 0x00AF9A89`) |
| 4 | serial do tamer (16 bytes, o do `0x02`) |
| 20 | `u32 0` |
| 24 | bloco de 696. Na falha, só o serial do alvo em +4 (offset 28); ✅ no sucesso, o bloco do digimon capturado **mantendo o mesmo serial do alvo em +4** (`0x00AF9B7F`) |
| 720 | `u32 0` |
| 724 | serial do digimon que tentou |
| 740 | `u32 0` |

- ✅ Sucesso (`DigimonRPG.bin @ 0x00AF9B37..0x00AF9C5E`): o cliente localiza o inimigo na batalha pelo serial em `0xB3+28` (`0x00AF9B7F`), toca a animação de captura com sucesso (`0x7A` + `0x79` com param `1`), faz o inimigo sumir da batalha (`[esi+0x210] = 8`), adiciona o bloco de 696 bytes ao time do jogador (`0x00B5A7D0`) — **não** usa `0xEF` — e zera a barra do atacante. A batalha termina em vitória se não sobrar inimigo.

## Renomeação (`0xF0`)
- C2S (42): `u8 0`, `u8 tipo` (1 = tamer, 2 = digimon), `u16 0`, `u32 uid`, nome (20 bytes).
- S2C (28): `u8 resultado`, tipo, `u16 0`, uid, nome.
- Resultado: 1 = renomeação grátis disponível (resposta à consulta sem nome), 2 = indisponível, 3 = renomeado (eco do nome).
- ✅ O cliente consulta sem nome e depois manda o nome. Na entrada do mapa chega uma consulta de tipo 1 (tamer), e a
  resposta é `02 01`.
- No emulador: só o digimon com a renomeação grátis, fora de batalha, nome `^[a-zA-Z0-9]{1,19}$`. ❓ Regras de nome do
  oficial não capturadas. A renomeação paga (item) ainda não existe.

## No emulador
- `Game/World/Taming.cs` (regras e criação), `Battle.Capture` (captura comum), handlers `ItemUseHandler` (`0x41`) e
  `RenameHandler` (`0xF0`).
- O uid do digimon novo vem de `Game/DigimonUids.cs`: um contador em memória, iniciado no maior uid do banco na partida.
  O snapshot faz upsert dos digimons.
- Persistido em `digimon.free_rename` (migração `0004`).
- Testes: `CaptureTests`.
