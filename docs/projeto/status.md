# Status atual

> Atualize ao fim de cada sessão. Mantenha curto: só o estado presente, sem histórico (o histórico fica nas [decisões](decisoes.md)).

**Última atualização:** 2026-11-21. **Cliente oficial: v322** (era 242).

## Feito
- ✅ Análise de viabilidade e plano: [visão geral](visao-geral.md), [roadmap](roadmap.md).
- ✅ Proxy-logger (`work/tools/proxy/`): rotas fixas, SQLite, marcadores, importação de pcap, consultas e **detecção de mudança de estrutura** (alertas ao vivo e `drpg_diff.py` entre versões). Ver [proxy](../ferramentas/proxy.md).
- ✅ 52 pcaps antigos em `captures.db` (sessões 1–48, marcadas como v242).
- ✅ Papel dos 4 servidores confirmado, e os 4 IPs localizados na tabela de texto da v322. Ver [servidores](../protocolo/servidores.md).
- ✅ `patch_client.py`: remoção do XIGNCODE por assinatura (v242 e v322) e troca de IPs, incluindo `--proxy-config`. **Aplicado** em `client/DigimonRPG.bin` (v322); o original está em `DigimonRPG.bin.orig`. Ver [patch_client](../ferramentas/patch-cliente.md).
- ✅ `drpg_wine.sh`: prefixo dedicado, `run` (args do `start_bin.bat`), `run --debug`, `hang` (backtrace de todas as threads + minidump). Ver [Wine](../cliente/wine.md).
- ✅ **Tela congelada no loading resolvida** (patch `loadingbar`, aplicado em `client/DigimonRPG.bin` e `DigimonRPG_proxy.bin`). Causa: a thread da barra e a principal usavam o mesmo backbuffer DirectDraw sem trava. O backbuffer ficava preso (`SURFACEBUSY`), mas o jogo seguia até a seleção de servidor. Agora a tela fica preta durante o loading.
- ✅ Primeira sessão no oficial pela v322 (sessão 52, login → mapa) e `drpg_diff.py` rodado. Ver [opcodes](../protocolo/opcodes.md#mudanças-v242--v322).
- ✅ Fórmula confirmada: `MaxEVP = L × (L − 1)`. Ver [fórmulas](../mecanicas/formulas-status.md).
- ✅ **Emulador reescrito** (branch `refactor/base`, sem commit, aguardando revisão): .NET 10, thread única do mundo, persistência em fila, migrações automáticas em `sql/upgrade`, 23 testes, devcontainer com SDK 10. Replay das sessões 10 e 4: login e channel idênticos ao oficial. Ver [arquitetura](../emulador/arquitetura.md) e [replay](../ferramentas/replay.md).

- ✅ Capturas longas v322: sessões 53, 54 e 56 (~84 mil pacotes, 80 marcadores) cobrem criar/apagar, tutorial, batalha, level up, pontos de atributo, evolução, itens, depósito, party, chat.
- ✅ Channel corrigido pela v322: lista, criar e apagar. Ver [channel](../protocolo/structs/channel.md).

## Em andamento / próximo passo
1. **Corrigir o que existe, do começo do fluxo para o fim** (login → channel → entrada no mapa → mapa → batalha), com amostras novas do oficial (v322). Só depois implementar coisas novas. Status e batalha entram nesse caminho.
2. **Protocolo v322:** confirmar a hipótese do bloco de status +4 bytes (sessão 52 tem `0x02`, `0x10`, `0xB4`). Capturar sessões mais longas na v322: troca de mapa, batalha, level up (`0x70`/`0x71`).
3. Experimento de status (ver [campanha](../captura/campanha.md), item 1).
4. Etapa 2 do roadmap: schema v0 e relatório de cobertura.

## Pendências conhecidas
- O `start_bin.bat` tem a conta sem o sufixo (o login falha). A certa é a do `start_launcher.bat`.
- A "PreMap Loading Thread" tem o mesmo desenho e pode congelar na troca de mapa. Verificar.
- Numa bateria de 6 execuções com `loadingbar`, o jogo fechou sozinho uma vez (~8 s, sem congelar). ❓ Causa desconhecida; talvez login repetido em sequência.
- ❓ O método de dump do Themida é manual e não está documentado. Necessário a cada atualização do oficial.
- ✅ O stub do XIGNCODE funciona contra o oficial na v322 até a seleção de servidor (login com sucesso). ❓ Falta confirmar canal e mapa.
- Os endereços em [binário](../cliente/binario.md) são da v242 (o dispatcher etc.). Refazer na v322.
- As fórmulas de ATK/DEF/BR/HP/VP do emulador erram até 109 pontos (`Game/Stats/StatFormulas.cs`). Os atributos base por nível (`10 + base × (L − 1)`) também são ❓.
- ❓ Replay: o oficial manda `0xC4` e `0xB2` sem pedido (timer?); o emulador não.
- Protocolo alvo **v322**. Entrada no mapa já bate em tamanho com a sessão 52; o conteúdo de vários pacotes ainda é cópia de captura ou zero.
