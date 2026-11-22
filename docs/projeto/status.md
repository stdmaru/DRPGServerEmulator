# Status atual

> Atualize ao fim de cada sessão. Mantenha curto: só o estado presente, sem histórico (o histórico fica nas [decisões](decisoes.md)).

**Última atualização:** 2026-11-22. **Cliente oficial: v322** (era 242).

## Feito
- ✅ Análise de viabilidade e plano: [visão geral](visao-geral.md), [roadmap](roadmap.md).
- ✅ Proxy-logger (`work/tools/proxy/`): rotas fixas, SQLite, marcadores, importação de pcap, consultas e **detecção de mudança de estrutura** (alertas ao vivo e `drpg_diff.py` entre versões). Ver [proxy](../ferramentas/proxy.md).
- ✅ 52 pcaps antigos em `captures.db` (sessões 1–48, marcadas como v242).
- ✅ Papel dos 4 servidores confirmado, e os 4 IPs localizados na tabela de texto da v322. Ver [servidores](../protocolo/servidores.md).
- ✅ `patch_client.py`: remoção do XIGNCODE por assinatura (v242 e v322) e troca de IPs, incluindo `--proxy-config`. **Aplicado** em `client/DigimonRPG.bin` (v322); o original está em `DigimonRPG.bin.orig`. Ver [patch_client](../ferramentas/patch-cliente.md).
- ✅ `patch_client.py --server IP [--port papel=N]`: troca IPs **e portas** (tabela de mundos do cliente). Gerado `client/DigimonRPG_local.bin` + `start_local.bat` para o emulador local (127.0.0.1, 7001/7000/7002/7003). ❓ Ainda não testado em jogo contra o emulador v322.
- ✅ `drpg_wine.sh`: prefixo dedicado, `run` (args do `start_bin.bat`), `run --debug`, `hang` (backtrace de todas as threads + minidump). Ver [Wine](../cliente/wine.md).
- ✅ **Tela congelada no loading resolvida** (patch `loadingbar`, aplicado em `client/DigimonRPG.bin` e `DigimonRPG_proxy.bin`). Causa: a thread da barra e a principal usavam o mesmo backbuffer DirectDraw sem trava. O backbuffer ficava preso (`SURFACEBUSY`), mas o jogo seguia até a seleção de servidor. Agora a tela fica preta durante o loading.
- ✅ Primeira sessão no oficial pela v322 (sessão 52, login → mapa) e `drpg_diff.py` rodado. Ver [opcodes](../protocolo/opcodes.md#mudanças-v242--v322).
- ✅ Fórmulas de status: `MaxEVP = L × (L − 1)`, atributos pelo status-base da dex (até rookie) e ATK/DEF/BR/HP/VP = ⌊c₁√a + c₂√b⌋ (BR erra 1 ponto em 8/80). Ver [fórmulas](../mecanicas/formulas-status.md).
- ✅ **Emulador reescrito** (branch `refactor/base`, sem commit, aguardando revisão): .NET 10, thread única do mundo, persistência em fila, migrações automáticas em `sql/upgrade`, 23 testes, devcontainer com SDK 10. Replay das sessões 10 e 4: login e channel idênticos ao oficial. Ver [arquitetura](../emulador/arquitetura.md) e [replay](../ferramentas/replay.md).

- ✅ Capturas longas v322: sessões 53, 54 e 56 (~84 mil pacotes, 80 marcadores) cobrem criar/apagar, tutorial, batalha, level up, pontos de atributo, evolução, itens, depósito, party, chat.
- ✅ Channel corrigido pela v322: lista, criar e apagar. Ver [channel](../protocolo/structs/channel.md).

- ✅ Bloco de status v322: offsets 0..343 iguais à v242; o resto é zero nas capturas (os 8 bytes novos não têm efeito observável).
- ✅ Status por forma de evolução confirmado na sessão 56. Ver [evolução](../mecanicas/evolucao.md). **Exige remodelar o digimon no emulador.**
- ✅ Layout do `0x03` mapeado e gerado a partir do inventário. Ver [inventário](../protocolo/structs/inventario.md). Falta persistir o inventário e os dados dos itens.
- ✅ `0x10` mapeado (0..100 igual à v242) e corrigido no emulador. Ver [resumo do tamer](../protocolo/structs/tamer-resumo.md). Falta a tabela de estágio das espécies.
- ✅ Tick do mapa como no oficial: ping `0xC4` do servidor e recuperação a cada 5,2 s. Ver [tick do mapa](../mecanicas/tick-mapa.md).
- ✅ Espécies, itens, zonas e portais lidos do `table.pf` do cliente na partida (sem XML inventado). Crescimento da `dex_us`, estágio e atributo do `digimon_us`, MaxActionGauge constante, nível máximo 160 (❓ provisório). Ver [arquivos de dados](../cliente/arquivos-de-dados.md) e [portais](../mecanicas/portais.md).
- ✅ Grade dos mapas lida do cliente (`extract-client` → `grids.bin`), 0x20 validado por célula e velocidade, roaming dos selvagens como no oficial (tick de 344 ms, casa ± 3, estados 1/4/11/8/12). Ver [movimento](../mecanicas/movimento.md) e [selvagens](../mecanicas/selvagens.md). Falta: casas e espécies dos selvagens nos outros mapas.
- ✅ Batalha PvE refeita sobre 67 batalhas v322: fluxo e pacotes do oficial (0x35 marcar/desmarcar, 0x67, fuga 0x65/0x6A, fim 0xB1/0x10/0x84/0x6D, 0xB4 na saída), dano básico ⌊ATK²/(10·DEF)⌋, barra por nível/BR, grupo selvagem compartilhado, estado do tamer no 0x10, cura retomada 0,2 s após o fim. Ver [batalha](../mecanicas/batalha.md). Falta: fator de atributo, skills, derrota por nocaute, chance de acerto real.
- ✅ **Área inicial / tutorial** (PR draft #2, 2026-11-22): digimon por forma (atributos e skills), in-training → rookie no L11, evolução automática no L21/31/41 em batalha e pelo menu, pontos e skills por forma (`0x71`, `0x72`, `0x82`), time de 5 (`0xF1`, `0x173`, invocação), inventário com duas bolsas (`0x91`, `0xBD`, `0xC9`, `0xC8`, `0xFC`), NPCs 1–5 e 8, status de selvagem pelo nível, EXP para todos os participantes e bits uma vez. Persistido (migração `0003`). 78 testes. Ver [evolução](../mecanicas/evolucao.md) e [área inicial](../mecanicas/area-inicial.md). ❓ Não testado em jogo com o cliente.
- ✅ Tabelas de EXP trocadas por fórmulas exatas (digimon e tamer). Ver [fórmulas de EXP](../mecanicas/formulas-exp.md). ❓ Evento de EXP ainda sem pacote identificado.

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
- Atributos confirmados só até rookie; champion em diante ❓. Bônus de equipamento/título/buff ainda não aplicados.
- Protocolo alvo **v322**. Entrada no mapa já bate em tamanho com a sessão 52; o conteúdo de vários pacotes ainda é cópia de captura ou zero.
