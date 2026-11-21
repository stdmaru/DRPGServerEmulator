# Proxy-logger

Código em `work/tools/proxy/`. Python ≥ 3.11, sem dependências externas (o importador usa o `tshark`).

| Arquivo | Função |
|---|---|
| `drpg_proxy.py` | proxy ao vivo (`run`) e marcadores (`mark`) |
| `import_pcap.py` | importa .pcap/.pcapng para o mesmo banco |
| `drpg_query.py` | consultas rápidas |
| `drpg_store.py` | schema SQLite, `Framer` (quebra o fluxo em pacotes) e `Writer` (grava em thread) |
| `config.toml` | rotas, banco, porta de controle |

O proxy **repassa os bytes sem alterar nada**: grava depois de repassar, e falha de parsing nunca derruba a conexão.

## Uso
```bash
cd work/tools/proxy
python3 drpg_proxy.py run --label "status: Yuramon STR 1 a 1"
python3 drpg_proxy.py mark "add STR #3"     # em outro terminal (ou digite no terminal do proxy + Enter)
python3 import_pcap.py ../../packets        # importa os pcaps antigos (reimportar é ignorado)
python3 drpg_query.py sessions
python3 drpg_query.py stats [-s 3] [--dir S2C]
python3 drpg_query.py timeline -s 3 [--op 0x71]
python3 drpg_query.py dump --op 0x71 --dir S2C -n 3
sqlite3 captures.db "SELECT op_hex, COUNT(*) FROM v_packets GROUP BY 1"
```
Ctrl+C ou SIGTERM encerra e mostra o total da sessão.

## Banco (`captures.db`)
- `sessions(id, started, source['proxy'|'pcap'], label)`
- `connections(id, session, server, client_addr, upstream, opened, closed)`
- `packets(id, conn, ts, dir['C2S'|'S2C'], opcode, size, seq, crc, payload, raw)`. `payload` não inclui o cabeçalho; `raw=1` marca bytes fora de quadro (desync).
- `marks(id, session, ts, text)`
- View `v_packets`: packets + session + server + `op_hex`.

## Como pôr o cliente no proxy
Os IPs não vêm em pacotes (ver [servidores](../protocolo/servidores.md)), então basta redirecionar a conexão TCP.

### Modo A: rotas fixas (qualquer SO)
Cada servidor oficial ganha um IP de loopback (127.0.0.2, .3, .4), configurado em `[[route]]` no `config.toml`, e o cliente é patchado para esses IPs com um comando:
```bash
python3 ../client/patch_client.py apply ../../client/DigimonRPG.bin ../../client/DigimonRPG_proxy.bin --proxy-config config.toml
```
✅ Na v322 isso troca 52.238.30.33 → 127.0.0.2, 52.238.29.166 → 127.0.0.3 e 52.238.31.90 → 127.0.0.4. Ainda não foi testado contra o oficial.

### Modo B: transparente (Linux + Wine), sem patchar o cliente
❓ Ainda não testado. O nftables redireciona as conexões do jogo para o proxy, e o proxy lê o destino original (`SO_ORIGINAL_DST`). As conexões do próprio proxy levam uma marca para não entrarem em loop. Marcar socket exige `CAP_NET_ADMIN`, então rode o proxy com `sudo`.
```bash
sudo nft add table ip drpg
sudo nft 'add chain ip drpg out { type nat hook output priority -100 ; }'
sudo nft add rule ip drpg out meta mark 0x2a return
sudo nft add rule ip drpg out ip daddr '{ 52.238.0.0/16, 20.214.0.0/16 }' tcp dport '{ 7000, 7001 }' redirect to :17000
# descomente [transparent] no config.toml e então:
sudo python3 drpg_proxy.py run --label "..."
# para desfazer:
sudo nft delete table ip drpg
```
Para não rodar o proxy como root: crie um usuário dedicado, troque a regra de `meta mark` por `meta skuid <usuario> return` e remova `mark` do config.

## Detecção de mudanças
Para não ficarmos no escuro quando o oficial mudar uma estrutura de pacote.
- Cada sessão grava a **versão do cliente** (`sessions.client_version`, lida de `client_dir/VersionInfo.ini` no config, ou `--version N`). Os pcaps antigos estão marcados como 242. Para importar com versão: `import_pcap.py … --version 322`.
- **Ao vivo:** ao iniciar, o proxy monta um perfil por (direção, opcode) com tudo o que já foi gravado e avisa `*** VERSÃO NOVA DO CLIENTE` se a versão mudou. Durante a sessão, imprime `!!!` e grava em `alerts`, uma vez por tipo:
  | Alerta | Quando |
  |---|---|
  | `OPCODE_NOVO` | opcode nunca visto naquela direção |
  | `TAMANHO_NOVO` | tamanho nunca visto num opcode que tinha 1 tamanho (ou até 3, com ≥3 amostras). Mostra quantas amostras e a diferença em bytes |
  | `BYTE_CONSTANTE_MUDOU` | mesmo tamanho, mas bytes que foram constantes em ≥20 amostras mudaram |
  Ver depois: `drpg_query.py alerts [-s N]`. Desligar: `run --no-alerts`. Código: `drpg_profile.py`.
- **Relatório entre versões:**
  ```bash
  python3 drpg_diff.py --base 242 --new 322
  python3 drpg_diff.py --base 242 --new 322 --op 0x71    # máscara de bytes constantes lado a lado
  python3 drpg_diff.py --base-sessions 1-30 --new-sessions 49-60
  ```
  Classifica em `TAMANHO` (forte), `tamanho?` (opcode de tamanho variável) e `conteúdo?` (constante diverge, ≥5 amostras de cada lado). Também lista os opcodes que só aparecem num lado.
- Limitação: sem schema, um campo que mudou de significado mas manteve tamanho e não era constante **não é detectado**. Quando o schema existir (Etapa 2), a comparação passa a ser campo a campo.
- Referência de ruído: comparando sessões da **mesma** versão (1–30 × 31–48), saíram 4 alertas fracos (`tamanho?`/`conteúdo?`) e 1 `TAMANHO` com só 2 amostras. Considere o número de amostras antes de concluir.

## Testes feitos (2026-11-21)
- Alertas ao vivo (cópia do banco, `--version 322`, servidor falso): `VERSÃO NOVA 242 -> 322`, `TAMANHO_NOVO` em 0x71 S2C (700 contra 696, +4), `OPCODE_NOVO` 0x199, `BYTE_CONSTANTE_MUDOU` em 0x11, `TAMANHO_NOVO` em 0xC4 C2S. O segundo 0x71 não repetiu o alerta.
- Servidor falso local: pacote C2S fragmentado em dois `send` foi remontado; bytes repassados idênticos (740/740); lixo entre pacotes virou `raw=1` com ressincronização; marcador via `mark` gravado; SIGTERM encerra de forma limpa.
- Importação dos 52 pcaps: 46 opcodes C2S / 67 S2C, nenhum trecho fora de quadro. Bate com a análise anterior.
- ❌ Ainda **não** testado contra o servidor oficial.
