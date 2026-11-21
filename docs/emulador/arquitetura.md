# Emulador: arquitetura

.NET 10 (`net10.0`), C# com `Nullable` e `TreatWarningsAsErrors`. Reescrito em 2026-11-21 (ver [decisões](../projeto/decisoes.md)). O protótipo antigo não é referência: só vale o que está marcado ✅ na wiki.

## Rodar
- **Stack:** `.devcontainer/docker-compose.yml`: MariaDB 11.8 (com healthcheck) + `dotnet/sdk:10.0`. O serviço `server` só sobe quando o banco estiver saudável.
- **Sempre no devcontainer** (é o ambiente de trabalho): `dotnet run --project src/DRPGServer` e `dotnet test DRPGServer.sln`. De fora: `docker exec -w /workspaces/DRPGServer server_devcontainer-server-1 dotnet ...`.
- O compose publica 7000–7003 e 3306 em 127.0.0.1, para o cliente (Wine) e as ferramentas Python do host.
- **Protocolo alvo: v322.**
- **Configuração:** `appsettings.json`, depois `appsettings.Local.json` (fora do git) e variáveis `DRPG_<Seção>__<Chave>`. Seções: `Servers` (porta e IP de cada servidor), `Database`, `Game` (taxas, nível máximo, mapa inicial, autosave, prazo do ticket de conta).

## Banco e migrações
- `sql/upgrade/NNNN_descricao.sql` roda **sozinho na partida**, em ordem. As aplicadas ficam em `schema_version` com SHA-256; um arquivo aplicado que mudou **impede a partida**.
- Nunca editar uma migração aplicada: criar a próxima. Regras em `sql/upgrade/README.md`.
- O banco é criado se não existir; a partida espera o banco responder.

## Estrutura (`src/DRPGServer`)
| Pasta | O que tem |
|---|---|
| `Network/Protocol` | cabeçalho, CRC, `PacketFramer`, `PacketReader`/`PacketWriter`, `ServerPacket` |
| `Network` | `Session` (recebe com `PipeReader`, envia por fila limitada), `NetworkHost` (os 4 listeners), `AccountRegistry`, `Opcodes.cs` |
| `Network/Handlers` | handlers por servidor, `PacketDispatcher` |
| `Network/Packets` | um `ServerPacket` por S2C; `Templates/*.hex` = payloads capturados ainda não decodificados |
| `Game/World` | `GameWorld` (thread única do mundo), `Zone`, `ZoneSystem`, `PlayerSystem`, `CharacterFactory` |
| `Game/Battle` | `Battle` (máquina de estados), `BattleSystem`, `BattleFormulas` |
| `Game/Stats` | `StatFormulas`: **o único lugar** com fórmulas de status |
| `Game/Entities`, `Game/Commands` | entidades; comandos de GM no chat (`/help`) |
| `Data` | `Database`, `DatabaseMigrator`, repositórios (Dapper), `GameData` (tabelas XML de `Resources/`), `Persistence` |

## Modelo de threads
- Cada sessão tem um laço de recepção. **Login e channel** rodam no laço da sessão (`ISessionPacketHandler`, async, podem ir ao banco).
- **Jogo no mapa:** `IWorldPacketHandler` é postado para a **thread do mundo** (`GameWorld`, tick de 50 ms). Estado do mundo (zonas, jogadores, batalhas) só se toca nela. Sistemas: `ZoneSystem`, `BattleSystem`, `PlayerSystem`.
- **Banco fora do mundo:** gravações vão para a `PersistenceQueue` (snapshot imutável) e o `PersistenceService` grava em segundo plano. A entrada no mapa espera `FlushAsync` antes de carregar o personagem.
- Ordem dos serviços: persistência → mundo → rede. Na parada, a rede fecha primeiro e a persistência grava o resto por último.

## Handlers
```csharp
[PacketHandler(MapOpcode.ChatNormal)]
public sealed class ChatNormalHandler(ChatCommands commands) : PlayerHandler { ... }
```
- Descobertos por reflexão e registrados no DI; um teste garante que não há opcode duplicado.
- `PlayerHandler` fecha a sessão se o jogador ainda não entrou no mapa.
- Opcode sem handler: log `Info` na primeira vez, `Debug` depois.

## Autenticação entre servidores
O channel valida usuário + `auth_key` e faz `SignIn` no `AccountRegistry`. Map e global fazem `Claim` pelo nome de usuário; sem login no channel, a conexão fecha. O ticket vale `AccountTicketGraceSeconds` depois que a última sessão da conta cai. Um segundo login no channel derruba o primeiro.

## Verificação
- Testes unitários em `tests/DRPGServer.Tests`: CRC contra capturas, framing, tamanho de payload de ~45 S2C contra o capturado, conta, digimon, batalha simulada, migrações.
- Replay de captura contra o emulador: [drpg_replay.py](../ferramentas/replay.md).
