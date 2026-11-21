# Registro de decisões

Formato: data, decisão, motivo. Só acrescente no fim. Para reverter uma decisão, registre uma nova.

| Data | Decisão | Motivo |
|---|---|---|
| 2026-11-20 | Priorizar a **captura** antes do emulador e do cliente | Só o servidor oficial tem as regras e ele vai fechar. O binário do cliente não some. |
| 2026-11-20 | Cliente: **não** decompilar e recompilar tudo de uma vez; seguir wrapper → hooks → decompilação incremental | ~25 mil funções. Decompilação completa leva anos; o caminho incremental entrega algo jogável em cada passo. |
| 2026-11-21 | **Não reescrever o emulador**; refatorar peça por peça | A base (listeners, dispatcher, DAOs, Zone, Battle) serve. Os problemas estão localizados (serialização manual, fórmulas). |
| 2026-11-21 | Proxy, importador e análise em **Python**; emulador segue em C# | Iteração rápida, numpy para ajuste de fórmulas, tshark. O schema YAML será a ponte entre os dois. |
| 2026-11-21 | Armazenamento de capturas em **SQLite** (`captures.db`) | Consultável com SQL, um arquivo só, fácil de arquivar. |
| 2026-11-21 | O proxy **não altera pacotes** | Os IPs dos servidores não aparecem em nenhum pacote, vêm do binário. Basta redirecionar a conexão. |
| 2026-11-21 | Documentação em **wiki com índice** em `server/docs/` | Trabalhar entre sessões lendo só a página necessária. |
| 2026-11-21 | Patches do cliente **por assinatura**, não por offset | O oficial ainda atualiza (242 → 322). Assinatura sobrevive a recompilações; offset não. |
| 2026-11-21 | Detecção de mudança de protocolo **por perfil estatístico** (tamanhos + bytes constantes) até o schema existir | Dá alerta hoje, sem esperar a Etapa 2. O schema substitui depois. |
| 2026-11-21 | Wine em **prefixo dedicado** para o jogo | O `~/.wine` é compartilhado e cheio de overrides; prefixo limpo isola o diagnóstico. |
| 2026-11-21 | Wine com **driver Wayland** (`winewayland.drv`) por padrão; o patch `loadingrace` fica **opcional** | Com o driver X11/XWayland a imagem congela no fim do loading (o jogo segue na seleção de servidor). gdi, vulkan e desktop virtual não resolvem; Wayland resolve. A corrida da thread da barra é real, mas não é a causa. |
| 2026-11-21 | **Revoga a anterior** (o Wayland passou por acaso). Patch `loadingbar` por padrão: a thread da barra de loading **não desenha**; `loadingrace` fica opcional; o driver Wayland continua padrão, mas não importa | A causa do congelamento é a corrida no backbuffer DirectDraw entre a thread da barra e a principal (`SURFACEBUSY`). Tirar o desenho da thread elimina a corrida com um patch pequeno. O custo é a tela preta durante o loading; serializar o acesso fica para depois. |
| 2026-11-21 | **Revoga "Não reescrever o emulador"**: base reescrita do zero em .NET 10; o protótipo não é referência | Pedido do dono do projeto. O protótipo tinha estado global sem sincronização, tamanho de pacote truncado em u16, autenticação entre servidores ausente e pacotes com tamanho errado. Reescrever saiu mais barato que corrigir peça por peça. |
| 2026-11-21 | Lógica de jogo numa **thread única do mundo**; banco por **fila de persistência** | Elimina locks no estado do jogo e tira o I/O do tick. |
| 2026-11-21 | Banco evolui só por **migrações numeradas** em `sql/upgrade`, aplicadas na partida, com checksum | Bancos existentes sobem sem passo manual; editar migração aplicada vira erro, não divergência silenciosa. |
| 2026-11-21 | As tabelas de `src/DRPGServer/Resources/` são **versionadas e publicadas** com o projeto | Decisão do dono do projeto: são metadados do servidor importantes para a preservação. |
| 2026-11-21 | Protocolo alvo do emulador: **v322** (versão atual do oficial); a v242 só como referência histórica | É a versão que o cliente usa e a única que dá para capturar agora. |
| 2026-11-21 | Trabalho **progressivo**: primeiro corrigir o que existe, do começo do fluxo (login) para o fim, com capturas novas; depois implementar o novo | A maior parte do que existe está incompleta ou errada. Tirar isso do caminho antes de construir em cima. |
| 2026-11-21 | Tabelas `ExpTable.xml` de digimon e tamer **removidas** e trocadas por fórmulas (`ExpFormulas`) | As fórmulas batem exatamente com todas as capturas; a tabela do protótipo errava do nível 14 em diante. |
| 2026-11-21 | Espécies, itens, zonas e portais vêm do **`table.pf` do cliente**, lido na partida (`Game:ClientTablePath`); `DigimonData.xml`, `ItemTable.xml` e `Zones.xml` removidos, `Portals.xml` só com chegadas capturadas | Os números do protótipo eram inventados; o cliente tem os dados reais. |
| 2026-11-21 | `ActionGauge` e `Classification` saem da espécie: medidor máximo é a constante 500 e o byte 300 diz se é selvagem; nível máximo **160** por ora | Os valores eram inventados; as capturas mostram 500 sempre e 0/2 conforme o dono. 160 é decisão provisória do dono do projeto. |
| 2026-11-21 | O `table.hf`/`table.pf` do cliente (v322) fica em `src/DRPGServer/Resources/Client/`, **fora do git** (`.gitignore`) | É peça central do servidor, mas continua sem publicar ([nota legal](legal.md)). |
