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
  - [Channel](protocolo/structs/channel.md): lista (0x12), criar (0x10) e apagar (0x11) personagem.
  - [Inventário](protocolo/structs/inventario.md): 0x03 (24 × 76 + 24 × 108) e o registro de item de 108 bytes.
  - [Resumo do tamer (0x10)](protocolo/structs/tamer-resumo.md): nível, posição/destino, líder; 204 bytes na v322.

### Arquitetura (leitura obrigatória antes de codar)
- [Fundação](arquitetura/README.md): valores (segurança > legibilidade > expansão), monorepo `DRPG.Shared`/`Server`/`Client`/`Tools`, o mesmo modelo de thread e camadas nos dois lados.
- [Padrões de código](arquitetura/padroes-de-codigo.md): nomes e sufixos, tamanho, tipos e erros, recursos permitidos/proibidos, testes, garantias automáticas (analyzers, APIs banidas, testes de arquitetura).
- [Biblioteca compartilhada](arquitetura/shared.md): pacotes de mão dupla com ida e volta e testes dourados, leitores de formato, versões do oficial.
- [Cliente](arquitetura/cliente.md): camadas, fatia de feature (`State`/`Handlers`/`Actions`/`Window`), frame, registro compatível com NativeAOT, resolução e UI ancorada.
- [Como estender](arquitetura/extensao.md): receitas fechadas (item, C2S, S2C, janela, domínio, formato, opção, patch do oficial) e os únicos pontos de registro manuais.

### Emulador
- [Arquitetura](emulador/arquitetura.md): como rodar, migrações, camadas, onde fica cada coisa, modelo de threads, dependências, testes.
- [Handlers](emulador/handlers.md): caminho de um pacote, request tipado (C2S), bases de handler, pré-condições, I/O pelo `WorldIo`, checklist de handler novo.
- [Dados de jogo](emulador/dados.md): fontes, regras (texto de UI nunca define dado), modificadores de status e efeitos tipados em XML.
- [Segurança](emulador/seguranca.md): modelo de ameaça, tickets entre servidores, entrada/saída do mundo, transferências conta ↔ personagem, alcance, limites, persistência e auditoria.

### Mecânicas (lógica do servidor)
- [Fórmulas de status](mecanicas/formulas-status.md): atributos = 10 + bônus do estágio + status-base × (L − 1) + pontos; ATK/DEF/BR/HP/VP = ⌊c₁√a + c₂√b⌋; MaxEVP L(L − 1).
- [Fórmulas de EXP](mecanicas/formulas-exp.md): digimon ⌊20L·1,2^L⌋ em float32 (trava no nível 77), tamer 5(L+1), recompensa 0x6E, evento de EXP ❓.
- [Evolução](mecanicas/evolucao.md): cada forma tem atributos, pontos e skills próprios; nível e EXP são do digimon; 0x70/0x80/0x81/0x82/0x9E, pontos por estágio.
- [Área inicial e time](mecanicas/area-inicial.md): NPCs do tutorial (0x56 escolhas 1–5 e 8), 0xF1 líder, 0x173 troca de posições.
- [Captura e renomeação](mecanicas/captura.md): Taming Net no mapa (`0x41`, `0xEF`, `0xC3`), captura comum na batalha (ícone 1, `0xB3`, 3 tentativas, só in-training), renomeação grátis (`0xF0`).
- [Enciclopédia](mecanicas/enciclopedia.md): Global `0x7B` (1104 B, `uid` + `owned[1100]`) e `0x7C` (64 B, time + evoluções), cifrados em AES-128-ECB com chave derivada do `0x0E`.
- [Digimon Depository](mecanicas/depository.md): Map `0x89` (736 B, abrir/depositar/retirar/trocar), `0x8A` (808 B, 10 × 80 B), `0x94` (1 B, expansão por item 901/929) e integração com Global `0x7B`.
- [Movimento](mecanicas/movimento.md): 0x20 a cada 0,87 s, até ~10,6 células/s, validação de célula e velocidade.
- [Batalha](mecanicas/batalha.md): fluxo 0x61…0x6D, 0x67, dano ⌊ATK²/(10·DEF)⌋, barra por nível/BR, acerto, fuga, grupo compartilhado, recuperação.
- [Selvagens](mecanicas/selvagens.md): 0x11, tick de 344 ms, casa fixa com caixa de 7 × 7, 5 % de chance de andar, estados.
- [Portais](mecanicas/portais.md): posição e destino do `map_us`, chegada capturada em `Portals.xml` ou estimada.
- [Tick do mapa](mecanicas/tick-mapa.md): ping 0xC4 do servidor a cada ~5,2 s, recuperação HP +5 / VP +3 / EVP +10, saída 0x2B.

### Cliente
- [Binário](cliente/binario.md): desempacotamento, RTTI, dispatcher, IPs fixos, renderer.
- [Mapas](cliente/mapas.md): formato do `.map` (grade de células, bit de andável), `grids.bin`.
- [Arquivos de dados](cliente/arquivos-de-dados.md): `table.pf` e `resource.pf`, `drpg_pack.py`, colunas que o servidor lê na partida, o que falta.
- [Wine](cliente/wine.md): rodar no Linux, prefixo dedicado, causa e correção da tela congelada no loading (corrida no backbuffer DirectDraw).
- [Qualidade de vida](cliente/qualidade-de-vida.md): resolução e os três níveis (wrapper → hooks → decompilação incremental).
- [Cliente Reboot (fãs)](cliente/reboot-analise.md): reescrita do zero em C# .NET 8 NativeAOT + MonoGame que lê a arte original e fala o protocolo; evidências e licença.

### Planos
- [Cliente novo](planos/cliente-novo.md): **estacionado até o merge do PR #41**. Source port em C# .NET 10 + SDL3/SDL_GPU (Vulkan/D3D12), Windows/Linux x64/arm64, regras de desempenho, RE de formatos e marcos.
- [Cards e Card Slash](planos/card-slash.md): especificação do sistema de cards.

### Captura
- [Campanha de captura](captura/campanha.md): checklist do que capturar e em que ordem.

### Ferramentas
- [Proxy-logger](ferramentas/proxy.md): captura ao vivo para SQLite, marcadores, importação de pcap, consultas, **detecção de mudanças entre versões**.
- [patch_client.py](ferramentas/patch-cliente.md): remove o XIGNCODE, corrige o loading e troca IPs e portas (`--server`) por assinatura. Fluxo para quando o oficial atualizar.
- [extract_status.py](ferramentas/extract-status.md): extrai amostras de status dos pcaps.
- [drpg_replay.py](ferramentas/replay.md): reproduz uma sessão capturada contra o emulador e compara as respostas com as do oficial.
- [extract_equipment_effects.py](ferramentas/extract-equipment-effects.md): gera o `EquipmentEffects.xml` (bônus de equipamento em `<Modifier>`) a partir do `item_info_us.dat`.
- [extract_digimon_titles.py](ferramentas/extract-digimon-titles.md): gera o `DigimonTitles.xml` (nome, bônus e regra de desbloqueio) a partir do `digimon_title_us.dat`.
