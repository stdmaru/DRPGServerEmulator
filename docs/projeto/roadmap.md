# Roadmap

Ordem de execução. O estado de cada etapa fica no [status](status.md).

## Etapa 1: proxy-logger (semanas 1–2)
Captura ao vivo para SQLite, com marcadores e importação dos pcaps antigos. Ver [proxy](../ferramentas/proxy.md).
**Pronto quando:** uma sessão inteira de jogo contra o oficial fica gravada e dá para consultar.

## Etapa 2: schema v0 + cobertura (semanas 2–3)
- Descrição declarativa (YAML) dos ~68 opcodes já vistos, montada a partir das classes C# existentes. Campos desconhecidos viram `unknown_N` explícitos, não arrays de zeros.
- O proxy decodifica ao vivo. Um script gera a matriz **cliente (~245) × capturados × implementados** em [opcodes](../protocolo/opcodes.md).
- O mesmo schema vai gerar depois o dissector do Wireshark, o código C# e os testes.
**Pronto quando:** o relatório de cobertura alimenta o checklist da [campanha](../captura/campanha.md).

## Etapa 3: campanha de captura (contínua, a partir da semana 2)
Ver [campanha](../captura/campanha.md).

## Etapa 4: refatorar o emulador (mês 2 em diante)
A base foi reescrita em 2026-11-21 (ver [decisões](decisoes.md) e [arquitetura](../emulador/arquitetura.md)). Daqui em diante, por feature.
1. Gerar as classes de pacote C# a partir do schema, começando pelo bloco de [status de digimon](../protocolo/structs/status-digimon.md) (0x71, 0x70, 0x10, 0x02).
2. **Harness de replay:** ✅ por opcode e tamanho ([drpg_replay.py](../ferramentas/replay.md)). Falta comparar campo a campo.
3. Trocar as fórmulas pelas inferidas ([fórmulas](../mecanicas/formulas-status.md)).
4. Implementar por feature, na ordem do jogo: vila inicial → batalha → quests iniciais → lojas e crafting → party e trade → avançado.

## Em paralelo, baixo custo
- Wrapper cnc-ddraw no cliente ([qualidade de vida](../cliente/qualidade-de-vida.md), nível 1).
- Mirror da CDN e do patch server (URLs em [servidores](../protocolo/servidores.md)).
- Ghidra: nomear os handlers do dispatcher conforme os opcodes entram no schema ([binário](../cliente/binario.md)).
- Depois: hooks e decompilação incremental do cliente (níveis 2 e 3).
