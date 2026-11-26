# Pacote de tabelas sintético

`client/table.hf` + `client/table.pf` imitam o pacote de tabelas do cliente com linhas **inventadas**,
para testar os casos de borda do carregamento (dex por nome, espécie sem dex, nome vazio, portal estimado; NPCs com os ids reais das escolhas e dos donos de loja)
sem depender dos dados reais. O pacote real fica em `src/DRPG.Server/Resources/Client/`, fora do git. As posições dos
portais repetem as do `Resources/Map/Portals.xml`, que vieram das capturas.

Para regenerar depois de mudar `client-src/table/*.txt`:

```bash
python3 -I work/tools/drpg_pack.py pack work/server/tests/DRPG.Server.Tests/Fixtures/client-src work/server/tests/DRPG.Server.Tests/Fixtures/client/table.hf --version 0x20
```
