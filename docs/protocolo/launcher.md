# Login do launcher (opcodes 0x0F00..0x0F03)

Opcodes **novos** no LoginServer, usados só pelo launcher próprio. O `DigimonRPG.bin` original nunca os envia (os opcodes originais vão até 0x179). Mesmo framing do resto do protocolo ([cabeçalho](cabecalho.md)). Código: `src/DRPG.Shared/Protocol/Launcher/` (pacotes e cripto, compartilhados com o launcher) e `src/DRPG.Server/Network/Handlers/Login/LauncherHandlers.cs`. Decisão em [decisões](../projeto/decisoes.md) (2026-11-26) e fluxo em [cliente novo](../planos/cliente-novo.md#launcher-login--auto-patcher-marco-1).

| Opcode | Sentido | Nome |
|---|---|---|
| `0x0F00` | launcher → servidor | Hello |
| `0x0F01` | servidor → launcher | HelloReply |
| `0x0F02` | launcher → servidor | Login |
| `0x0F03` | servidor → launcher | LoginReply |

Inteiros little-endian. Texto = `u16 tamanho` + bytes UTF-8, sem zero no fim.

## Pacotes
**Hello** `u16 protocol` (=1), `u32 launcherVersion`.

**HelloReply** `u16 protocol`, `u8 status` (0 Ok, 1 UnsupportedProtocol, 2 Disabled), `byte[32] nonce`, `u16 n` + `byte[n] publicKey` (SPKI DER da chave P-256, n ≤ 256).

**Login** `u16 n` + `byte[n] ephemeralKey` (SPKI DER P-256 temporária), `byte[12] iv`, `u16 m` + `byte[m] sealed` (m ≤ 1024; texto cifrado + tag GCM de 16).

Conteúdo decifrado: `byte[32] serverNonce`, `byte[32] responseKey`, `u32 launcherVersion`, texto `login` (≤ 255 bytes: usuário ou e-mail), texto `password` (≤ 255 bytes).

**LoginReply** `byte[12] iv`, `u16 m` + `byte[m] sealed`, cifrado com AES-256-GCM pela `responseKey`. Conteúdo: `u8 status` (0 Ok, 1 InvalidCredentials, 2 TooManyAttempts, 3 OutdatedLauncher, 4 AccountUnavailable, 5 ServerError), texto `gameUsername` (≤ 20 bytes, o `username[21]` do jogo), texto `gameKey` (40 hex), `u32 validSeconds`.

## Criptografia
- ECDH P-256 entre a chave temporária do launcher e a do servidor. Não é X25519, porque a BCL do .NET não tem X25519 e o projeto é NativeAOT sem dependência nativa extra.
- Chave AES = HKDF-SHA256(segredo ECDH, salt = nonce do servidor, info = `drpg-launcher-login-request-v1`), 32 bytes. Corpo em AES-256-GCM.
- O nonce vale **uma vez por conexão**: o servidor o consome no primeiro Login. Corpo que não decifra, nonce diferente ou campo fora do limite: a conexão é fechada sem resposta.
- **Impressão digital** = SHA-256 do SPKI DER, em hex minúsculo. O servidor a escreve no log ao iniciar.
- Chave privada: `LauncherLogin:KeyPath` (padrão `launcher-login.pem`, PKCS#8, criada na primeira partida com permissão 600, fora do git).
- Sem sigilo futuro: se a chave privada vazar, logins gravados podem ser decifrados (risco aceito).

## Configuração do launcher
O `DR_Configuration.json`, baixado por HTTPS, ganha o campo:

```json
"LoginServer": { "Host": "login.exemplo", "Port": 7001, "KeyFingerprint": "<64 hex>" }
```

Chave recebida com outra impressão digital: o launcher não libera os campos e mostra erro.

**Modo de desenvolvimento:** `drpg-launcher.ini` → `[Development] AllowInsecure=true`. O launcher aceita o JSON por HTTP e pula a impressão digital, com aviso no console. A opção só existe no launcher: o servidor não tem como saber se a checagem foi feita, então não há opção do lado dele. A cifra do corpo continua ligada.

**`drpg-launcher.ini`** (ao lado do launcher; modelo em `drpg-launcher.ini.example`): `[Patch] ConfigUrl` (HTTPS), `[Development] AllowInsecure`, `[Game] Runner` (programa que roda o `RunExeFileName`, por exemplo `wine` no Linux). Com login aceito, o launcher roda `RunExeFileName <usuário do jogo> <chave>` na pasta do jogo e fecha.

## Servidor
- `LauncherLogin:Enabled` (desligado → HelloReply com status `Disabled`), `GameKeyValidHours` (5), `MinLauncherVersion`, limites por IP e por login (token bucket).
- A senha é conferida contra `account.password_hash` (PBKDF2-SHA256, 600 000 iterações). Usuário inexistente gasta o mesmo tempo. A chave de jogo nova vai só como hash para `auth_key_hash`, com `auth_key_expires_at`. Ver [segurança](../emulador/seguranca.md).
- O `AuthTimeout` do LoginServer (30 s) fecha conexões sem login. O launcher reconecta e manda o Hello de novo, sem o usuário perceber, quando a conexão caiu antes do envio.
