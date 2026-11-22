# Servidores e conexões

## Servidores oficiais
✅ Papel de cada servidor confirmado pelos opcodes trocados em cada conexão (importação dos 52 pcaps).

| Papel | Endereço | Opcodes típicos | Emulador (`appsettings.json`, `Servers`) |
|---|---|---|---|
| login | 52.238.30.33:**7001** | 0x05 (handshake) | LoginServer :7001 |
| channel | 52.238.30.33:7000 | 0x03 seleção, 0x04 auth, 0x10–0x14 personagens, 0x16 | ChannelServer :7000 |
| map | 52.238.29.166:7000 | jogo: 0x01, 0x02, 0x03, 0x20, batalha 0x61–0x70… | MapServer :7002 |
| global | 52.238.31.90:7000 | 0x64 auth, 0xde join, 0x7b/0x7c enciclopédia, chat 0xc8/0xca/0xcc | GlobalServer :7003 |

Ordem em `first-access.pcapng`: login → channel → map → global (o global conecta ~5 s depois do map).

O 0xC4 aparece em channel, map e global. É um keep-alive vazio: o cliente envia e o servidor responde.

## De onde o cliente tira os endereços
- **Os IPs não aparecem em nenhum pacote** (procurei em texto, UTF-16 e binário nas 52 capturas). Por isso o proxy não precisa reescrever pacotes.
- ✅ O `.bin` tem uma **tabela de IPs em texto** com **todos os servidores**. Na v322 (offsets de arquivo):
  | Offset | IP | Papel |
  |---|---|---|
  | 0x92CE1C | 52.238.30.33 | login (:7001) e channel (:7000) |
  | 0x92CE44 | 52.238.31.90 | global |
  | 0x92CE54 | 52.238.29.166 | map |
  | 0x92CE64 | 52.238.29.153 | ❓ outro map? |
  | 0x92CE94… | 20.214.228.11, 20.214.227.249, 52.238.31.48, 20.214.227.129, 52.238.31.51, 52.238.30.192, 20.214.228.137 | ❓ |
  Há também `20.214.228.140` em 0x910E58.
  - No dump da v242 (`_backups/DigimonRPG.bin_unpacked`), as duas primeiras entradas **já estavam trocadas por `127.0.0.1`**. Por isso a v242 parecia não ter os IPs de login e global.
- ✅ As **portas** são imediatos no código, numa tabela de mundos × slots (Login, Session, 255 mapas). Ver [patch_client](../ferramentas/patch-cliente.md#tabela-de-servidores---server). O 7001 do login vem do mundo 1; o 7000 do channel, do mundo 0.
- Para pôr o cliente no emulador: `patch_client.py apply … --server 127.0.0.1`.
- Para pôr o cliente no proxy (modo A): `patch_client.py apply … --proxy-config ../proxy/config.toml` (ver [patch_client](../ferramentas/patch-cliente.md)).
- Linha de comando: `start_bin.bat` → `DigimonRPG.bin <conta> <hash>`; o launcher usa `nadigimonrpg:<hash>;<conta>;`.

## Web e patch (espelhar enquanto existir)
- Config: `https://digimonrpg-na.kinxzone.com/digimonrpg-na/NorthAmerica/Config/Live` (cópia local em `work/web/Patch/DR_Configuration.json`)
- Launcher: `…/NorthAmerica/Launcher/Live` (`DigimonRPG_6.zip`, versão 8)
- Patches: `…/NorthAmerica/PatchFiles/Live/Patch_%d.zip` (patch atual: 242)
- Páginas: `http://dro.gameking.com/Main/Main.aspx`, `http://www.digimonrpg.com/notice/launcher.aspx`
- XIGNCODE: 18.178.95.116 / 52.78.210.233 (não é necessário)
