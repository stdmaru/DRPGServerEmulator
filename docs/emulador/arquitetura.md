# Emulador: arquitetura

.NET 10 (`net10.0`), C# com `Nullable` e `TreatWarningsAsErrors`. Reescrito em 2026-11-21 (ver [decisões](../projeto/decisoes.md)). O protótipo antigo não é referência: só vale o que está marcado ✅ na wiki.

Páginas desta seção:
- **Esta página:** como rodar, camadas, onde fica cada coisa e modelo de threads.
- [Handlers](handlers.md): como um pacote entra, é validado e vira uma chamada de serviço.
- [Dados de jogo](dados.md): de onde vêm as tabelas, efeitos e modificadores (sem parser de texto).
- [Segurança](seguranca.md): modelo de ameaça e os controles que todo código novo usa.

> Peças marcadas com **(PR #41: Xn)** fazem parte da refatoração da fundação e entram no item `Xn` do checklist daquele PR. A marca sai no commit que implementa a peça.

## Rodar
- **Stack:** `.devcontainer/docker-compose.yml`: MariaDB 11.8 (com healthcheck) + `dotnet/sdk:10.0`. O serviço `server` só sobe quando o banco estiver saudável.
- **Sempre no devcontainer** (é o ambiente de trabalho): `dotnet run --project src/DRPGServer` e `dotnet test DRPGServer.sln`. De fora: `docker exec -w /workspaces/DRPGServer server_devcontainer-server-1 dotnet ...`.
- O compose publica 7000–7003 e 3306 em 127.0.0.1, para o cliente (Wine) e as ferramentas Python do host.
- **Protocolo alvo: v322.**
- **Configuração:** `appsettings.json`, depois `appsettings.Local.json` (fora do git) e variáveis `DRPG_<Seção>__<Chave>`. Seções: `Servers` (porta e IP de cada servidor), `Database`, `Game` (taxas, nível máximo, mapa inicial, autosave), `Network` (validade do ticket entre servidores; limites de conexão, prazos e pacotes, ver [segurança](seguranca.md#limites-de-recurso)). Toda opção é validada na partida (`Configuration/OptionsValidation.cs`, `ValidateOnStart`): o boot lista todos os erros de uma vez e não sobe. O que depende das tabelas (mapa e posição inicial) é conferido pelo `PlayerSystem` na construção. `Database:Name` só aceita letras, números e `_`, porque entra no `CREATE DATABASE`. Opção nova entra com o limite dela no validador.

## Banco e migrações
- `sql/upgrade/NNNN_descricao.sql` roda **sozinho na partida**, em ordem. As aplicadas ficam em `schema_version` com SHA-256; um arquivo aplicado que mudou **impede a partida**.
- Nunca editar uma migração aplicada: criar a próxima. Regras em `sql/upgrade/README.md`.
- O banco é criado se não existir; a partida espera o banco responder.
- Escrita que envolve mais de uma tabela ou mais de uma fonte (conta e personagem) roda numa **unidade de trabalho** (`UnitOfWork`): os repositórios recebem a transação em vez de abrir a própria (ex.: `CashRepository.TakeAsync(uow, …)` + `CharacterRepository.SaveAsync(uow, …)` no `AccountTransferRepository`). O mapeamento `snake_case` → `PascalCase` do Dapper é ligado pelo próprio `Database`.

## Camadas
Cada camada só conhece a de baixo. Regra de negócio nunca fica em handler nem em pacote.

| Camada | Pasta | Faz | Não faz |
|---|---|---|---|
| Protocolo | `Network/Protocol` | framing, CRC, `PacketReader`/`PacketWriter`, `ServerPacket`, `IClientPacket<T>` | nada de jogo |
| Pacotes | `Network/Packets/<Servidor>` | layout de cada S2C (`ServerPacket`) e de cada C2S (`IClientPacket<T>`) | validar regra, mudar estado |
| Handlers | `Network/Handlers` | recebe o request tipado, declara pré-condições, chama **um** serviço, envia a resposta | calcular preço, mexer em inventário, ir ao banco |
| Serviços | `Game/<Domínio>` | regras, validação autoritativa, orquestração, efeitos colaterais | ler bytes, abrir conexão de banco direto |
| Entidades | `Game/Entities` | estado e invariantes (não deixa o estado ficar inválido) | I/O, rede, regras que cruzam entidades |
| Dados | `Data/` | repositórios (Dapper), `UnitOfWork`, `GameData` (tabelas carregadas), `PersistenceQueue` | regra de jogo |

## Onde fica cada coisa
| Domínio | Pasta | Peças |
|---|---|---|
| Mundo | `Game/World` | `GameWorld` (thread do mundo), `Zone`, `ZoneSystem`, `PlayerSystem`, `WorldIo`, `IGameClock`, `InteractionGuard` (**S4**) |
| Itens | `Game/Items` | `ItemService` (dar, tirar, trocar, depósito da conta; **G2**), `ItemGrant`/`ItemDuration` (**G1**), `EquipmentService` (**G5**), `ItemUseService` e efeitos de uso (**E4**) |
| Economia | `Game/Economy` | `Wallet`/`CurrencyKind` (**G3**), `NpcShopService` (**G4**), `CashShopService`, `EconomyLedger`, `IAuditLog` |
| Status | `Game/Stats` | `StatFormulas` (**o único lugar** com fórmulas de status), `ExpFormulas`, `StatModifiers` (**E1**) |
| Digimon | `Game/Digimons` | títulos, captura (`Taming`), progressão |
| Batalha | `Game/Battle`, `Game/Battle/Cards` | `Battle` (máquina de estados), `BattleSystem`, `BattleFormulas`, efeitos de card (**E3**) |
| NPC | `Game/Npcs` | scripts por mapa (`NpcScriptBase`) e `NpcScriptRegistry` |
| GM | `Game/Commands` | comandos de chat (`/help`) |

Antes de escrever uma função de jogo, procure o serviço do domínio. Se uma lógica vai ser usada por mais de uma feature (dar item, gastar moeda, mandar para depósito, recalcular status), ela **é** do serviço e nunca é copiada.

## Modelo de threads
- Cada sessão tem um laço de recepção. **Login e channel** rodam no laço da sessão (handlers de sessão, async, podem ir ao banco).
- **Jogo no mapa:** handlers de mundo são postados para a **thread do mundo** (`GameWorld`, tick de 50 ms). Estado do mundo (zonas, jogadores, batalhas) só se toca nela. Sistemas: `ZoneSystem`, `BattleSystem`, `PlayerSystem`.
- **I/O a partir do mundo** passa sempre por `WorldIo` ([handlers](handlers.md#io-a-partir-de-um-handler-de-mundo)): a operação roda fora da thread, e a continuação volta para ela já sabendo se o jogador continua online. Exceção nunca fica sem observação. É proibido `_ = AlgoAsync()` e é proibido criar `Task` solta no código de jogo.
- **Persistência:** gravações vão para a `PersistenceQueue` (snapshot imutável, **coalescing por `CharacterUid`**), e o `PersistenceService` grava em segundo plano dentro de uma única transação MySQL:
  - **Gravação imediata (`player.RequestImmediateSave()`):** eventos críticos (coleta, descarte e uso de itens, captura, Depository, renomeação, fim de batalha, evolução permanente, NPCs, troca de mapa).
  - **Dirty + autosave (`player.MarkDirty()`):** ações frequentes de baixo risco (movimento, organização interna, pontos, ordem do time), gravadas a cada `AutosaveSeconds` (padrão 60 s).
  - **Transferência conta ↔ personagem:** job **não coalescível** (`CharacterTransfers`) que grava o snapshot e a mudança da conta na mesma transação (ver [segurança](seguranca.md#transferências-entre-conta-e-personagem)).
  - **Falha:** retry com backoff e depois dead-letter em arquivo, com o personagem bloqueado até a reaplicação. Uma gravação nunca é descartada em silêncio ([persistência](seguranca.md#persistência)).
- Ordem dos serviços: persistência → mundo → rede. Na parada, a rede fecha primeiro e a persistência grava o resto por último.
- Tempo: o mundo usa `GameWorld.Now` (monotônico), e o tempo de calendário (expiração de itens, histórico) vem de `IGameClock`. Ninguém chama `DateTimeOffset.UtcNow` direto: entidades recebem o relógio (o `Inventory` guarda o dele) e pacotes recebem o horário pronto (ex.: `CharacterDataPacket(character, serverUnixTime)`). Prazos de infraestrutura (espera do banco, folga de ticket) usam `TimeProvider`. Nos testes, `GameClock` sobre `ManualTime` controla o calendário.

## Dependências
- Todas as dependências são **obrigatórias** e vêm pelo construtor (DI). Não existe parâmetro `= null` para "funcionar sem" um serviço. Os testes usam fakes (`tests/DRPGServer.Tests/TestSupport.cs`), nunca um caminho alternativo no código de produção. O container de DI valida o grafo inteiro na partida (`ValidateOnBuild`), também em produção.
- Entidades não recebem serviços. Quem precisa de `GameData` recebe os dados que usa.

## Autenticação entre servidores
O channel valida usuário + `auth_key` e emite um **ticket** da conta, amarrado ao IP da conexão. Mapa e global só aceitam a conta com ticket válido, do mesmo IP, e uma vez por servidor. Um segundo login no channel derruba o primeiro. Detalhes em [segurança](seguranca.md#autenticação-entre-servidores).

## Verificação
- Testes de banco (`[DatabaseFact]`) rodam contra o MariaDB do devcontainer só com `DRPG_TEST_DB_HOST=database`, cada classe num banco descartável: `docker exec -e DRPG_TEST_DB_HOST=database -w /workspaces/DRPGServer server_devcontainer-server-1 dotnet test DRPGServer.sln`. Sem a variável, eles aparecem como pulados.
- Testes unitários em `tests/DRPGServer.Tests`, um arquivo por domínio: CRC contra capturas, framing, tamanho de payload dos S2C contra o capturado, conta, digimon, batalha simulada, migrações, entradas inválidas de cada handler.
- Replay de captura contra o emulador: [drpg_replay.py](../ferramentas/replay.md).
