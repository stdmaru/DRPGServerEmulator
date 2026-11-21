# drpg_replay.py

`work/tools/proxy/drpg_replay.py`: reproduz os C2S de uma sessão do `captures.db` contra o emulador e compara a sequência de `(opcode, tamanho)` S2C com a do oficial, conexão por conexão. Também confere o CRC de tudo que o emulador manda.

## Preparar
Rode o emulador **no devcontainer**, num banco separado para não mexer nos dados de jogo. O script roda no host e conecta nas portas que o compose publica em 127.0.0.1:
```bash
docker exec -w /workspaces/DRPGServer -e DRPG_Database__Name=drpg_replay server_devcontainer-server-1 dotnet run --project src/DRPGServer
```
Parar: `docker exec server_devcontainer-server-1 pkill -TERM -f DRPGServer` (encerrar o `docker exec` não para o processo no container).
Na primeira vez, crie a conta de teste (padrões do script: `replay` / `REPLAYKEY` / `Replayer`):
```sql
INSERT INTO account(username,password,auth_key,authority) VALUES('replay','x','REPLAYKEY',3);
INSERT INTO `character`(account_uid,name,tamer_id,level,map_id,pos_x,pos_y,slot) VALUES(LAST_INSERT_ID(),'Replayer',1,1,79,56,56,0);
INSERT INTO digimon(character_uid,digimon_id,name,level,is_leader) VALUES(LAST_INSERT_ID(),10,'Agumon',1,1);
```

## Usar
```bash
python3 drpg_replay.py --list          # sessões capturadas
python3 drpg_replay.py 10              # login-into-map
python3 drpg_replay.py 4 --ignore 0x11,0xb2
```
- O papel da conexão vem do primeiro C2S (`0x05` login, `0x03`/`0x04` channel, `0x49` map, `0x64` global).
- Troca usuário, chave e personagem em login `0x05`, channel `0x04`/`0x14`/`0x16`, map `0x01`, global `0x64`, e recalcula o CRC.
- Espera entre pacotes = intervalo capturado limitado a `--min-gap`..`--max-gap`.
- `--ignore` (padrão `0x11`): opcodes mandados por timer, que não dependem do C2S.
- Saída 0 se todas as conexões forem idênticas.

## Limites
- Capturas que começam no meio da sessão (sem login/entrada) não rodam (ex.: `battle_1`, sessão 2).
- Seriais (digimon selvagem, batalha) são do oficial; pedidos que dependem deles não batem.
- Compara só opcode e tamanho, não o conteúdo.

## Resultado em 2026-11-21
| Sessão | login | channel | map | global |
|---|---|---|---|---|
| 10 login-into-map | ✅ | ✅ | ≈ falta 0xC4 depois do 0x2B | ≈ 0x7B/0x7C em ordem trocada |
| 4 first-access | ✅ | ✅ | ≈ falta 0xB2 depois de um keepalive | ✅ |
| **52 v322 login até o mapa** | ✅ | ✅ | ✅ | ✅ |

Desde a mudança para v322, as sessões v242 diferem de propósito no tamanho de `0x02`, `0x10` e `0xB4`.

❓ O oficial parece mandar 0xC4 e 0xB2 por conta própria (timer), não em resposta. Só 2 saídas capturadas: uma com 0xC4, outra sem.
