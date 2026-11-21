# Wiki: preservação do Digimon RPG (NA)

Documentação do emulador e da preservação do jogo. Ela está dividida em páginas curtas e independentes para que cada sessão de trabalho leia **só o que precisa**.

## Como usar (sessões de trabalho)

1. Leia **este índice** e o [status atual](projeto/status.md).
2. Abra só as páginas ligadas à tarefa. Não leia a wiki inteira.
3. Ao terminar uma tarefa:
   - atualize a página do assunto com **fatos** (offsets, layouts, fórmulas, comandos), não com narrativa;
   - atualize o [status](projeto/status.md) e, se houve uma decisão, o [registro de decisões](projeto/decisoes.md);
   - página nova entra **neste índice** com uma linha de descrição.
4. Regras de escrita:
   - uma página por assunto, no máximo ~150 linhas; se crescer, divida e crie um subíndice;
   - marque o que é certeza e o que é hipótese (**✅ confirmado** / **❓ hipótese**);
   - cite a evidência: pcap, opcode, endereço no binário ou script.

## Índice

### Projeto
- [Visão geral](projeto/visao-geral.md): objetivo, o que existe, viabilidade e expectativa.
- [Status atual](projeto/status.md): o que está feito, em andamento e o próximo passo. **Sempre atualizado.**
- [Roadmap](projeto/roadmap.md): etapas, ordem e critérios de pronto.
- [Registro de decisões](projeto/decisoes.md): o que foi decidido e por quê.
- [Nota legal](projeto/legal.md): o que publicar e o que não publicar.

### Protocolo
- [Cabeçalho e framing](protocolo/cabecalho.md): os 14 bytes, CRC, limites.
- [Servidores e conexões](protocolo/servidores.md): IPs, portas, ordem das conexões e de onde o cliente tira os endereços.
- [Catálogo de opcodes](protocolo/opcodes.md): opcodes tratados pelo cliente, capturados e implementados.
- Structs:
  - [Status de digimon](protocolo/structs/status-digimon.md): bloco comum aos opcodes 0x02, 0x10, 0x63, 0x70, 0x71…

### Emulador
- [Arquitetura](emulador/arquitetura.md): como rodar, migrações, estrutura, modelo de threads, handlers, autenticação entre servidores, testes.

### Mecânicas (lógica do servidor)
- [Fórmulas de status](mecanicas/formulas-status.md): MaxEVP confirmada, análise de ATK/DEF/BR/HP/VP, experimento proposto.

### Cliente
- [Binário](cliente/binario.md): desempacotamento, RTTI, dispatcher, IPs fixos, renderer.
- [Arquivos de dados](cliente/arquivos-de-dados.md): `table.pf` e `resource.pf`, `drpg_pack.py`, o que existe e o que falta.
- [Wine](cliente/wine.md): rodar no Linux, prefixo dedicado, causa e correção da tela congelada no loading (corrida no backbuffer DirectDraw).
- [Qualidade de vida](cliente/qualidade-de-vida.md): resolução e os três níveis (wrapper → hooks → decompilação incremental).

### Captura
- [Campanha de captura](captura/campanha.md): checklist do que capturar e em que ordem.

### Ferramentas
- [Proxy-logger](ferramentas/proxy.md): captura ao vivo para SQLite, marcadores, importação de pcap, consultas, **detecção de mudanças entre versões**.
- [patch_client.py](ferramentas/patch-cliente.md): remove o XIGNCODE e troca IPs por assinatura. Fluxo para quando o oficial atualizar.
- [extract_status.py](ferramentas/extract-status.md): extrai amostras de status dos pcaps.
- [drpg_replay.py](ferramentas/replay.md): reproduz uma sessão capturada contra o emulador e compara as respostas com as do oficial.
