# Nota legal

Preservar jogo abandonado é zona cinzenta.
- **Publicar:** código do emulador, schema do protocolo, ferramentas, esta wiki, e as tabelas de metadados do servidor em `src/DRPG.Server/Resources/` (versionadas no repositório por decisão do projeto, ver [decisões](decisoes.md)).
- **Não publicar:** binário do cliente, assets (`resource.pf`), tabelas do cliente (`table.pf` e os .txt extraídos), nem pcaps com dados de conta (authkey, MAC, nomes). O servidor lê o `table.hf`/`table.pf` de `src/DRPG.Server/Resources/Client/`, que está no `.gitignore`: cada um copia do próprio cliente.
- Para o acervo bruto (capturas, cliente, assets), considere entregar a uma instituição de preservação (Video Game History Foundation, Internet Archive) em vez de publicar diretamente.
