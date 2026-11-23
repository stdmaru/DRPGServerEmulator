# Visão geral

## Objetivo
Preservar o Digimon RPG (NA, cliente v242), um jogo online abandonado pela publisher, antes que o último servidor oficial feche:
1. um **servidor emulado** (este repositório, C#);
2. um **cliente com qualidade de vida** (resolução etc.);
3. um **acervo** de capturas e documentação do protocolo.

## O que temos
| Item | Situação |
|---|---|
| Cliente | `DigimonRPG.bin` desempacotado do Themida, com versão sem XIGNCODE. Ver [binário](../cliente/binario.md). |
| Dados | `table.pf` (116 tabelas) e `resource.pf` (~11,8 GB). Ver [arquivos de dados](../cliente/arquivos-de-dados.md). |
| Protocolo | Sem criptografia, cabeçalho de 14 bytes. Ver [cabeçalho](../protocolo/cabecalho.md). |
| Capturas | 52 pcaps: 46 opcodes C2S / 67 S2C, contra ~245 S2C tratados pelo cliente. Ver [opcodes](../protocolo/opcodes.md). |
| Emulador | ~8,5 mil linhas de C#: login, canal, mapa, movimento, chat, batalha, inventário, drops, alguns NPCs. |
| Servidor oficial | Online e abandonado, sem data de fechamento anunciada. Sem risco de ban. |

## Quem sabe o quê
- **O cliente** sabe o *formato* de tudo: como lê cada S2C e monta cada C2S, os códigos de erro e a UI de NPC/loja.
- **Só o servidor** sabe as *regras*: fórmulas, drops, EXP, quests, IA, crescimento por nível, broadcast.
  - Portanto: o cliente se decompila **sem pressa**, porque o binário não some. As regras se capturam **agora**, enquanto o servidor existe.
- Gerar o servidor automaticamente a partir das capturas não é possível: a captura mostra *o que* o servidor responde, não *por quê*. As regras saem de inferência estatística, que a [campanha de captura](../captura/campanha.md) e as ferramentas tornam barata.

## Expectativa (1 pessoa, meio período)
| Meta | Viabilidade | Esforço |
|---|---|---|
| Acervo de capturas + mirror da CDN | Alta, só enquanto o servidor existir | 2–4 semanas |
| Catálogo de opcodes e layouts | Alta | 1–3 meses, incremental |
| Servidor jogável no conteúdo inicial e médio | Alta | 4–8 meses |
| Paridade próxima do oficial | Média | 1 ano ou mais |
| Paridade exata de fórmulas e taxas | Baixa | Fica como aproximação estatística |
