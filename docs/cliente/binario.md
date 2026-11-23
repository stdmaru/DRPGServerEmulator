# Binário do cliente

## Arquivos (`work/client/`)
| Arquivo | O que é |
|---|---|
| `DigimonRPG.bin` | **v322** (2026-11-21): dump desempacotado, ImageBase 0x700000, `.text` em VA 0x701000 / arquivo 0x600, XIGNCODE removido por [patch_client](../ferramentas/patch-cliente.md), IPs originais |
| `DigimonRPG.bin.orig` | v322 desempacotado **antes** do patch. **Usar este para análise da v322.** |
| `_backups/2025/` | cliente v242 (`DigimonRPG.bin`, `DigimonRPG.exe`) |
| `_backups/DigimonRPG.bin_themida` | v242 original empacotado (Themida) |
| `_backups/DigimonRPG.bin_unpacked` | v242 desempacotado, ImageBase 0xA70000 (as duas primeiras entradas da tabela de IPs já trocadas por 127.0.0.1) |
| `_backups/DigimonRPG.bin_noxign` | v242 sem XIGNCODE (= `DigimonRPG_default_ports.bin`) |
| `DigimonRPG.exe` | launcher (atualiza e **sobrescreve o .bin com a versão empacotada**) |

> Endereços abaixo são da **v242** (VA = offset − 0x600 + 0xA71000). Na v322 tudo mudou de lugar: use assinaturas, não endereços fixos.

`work/notes.txt` tem o patch `00C4C230: mov eax,1 / ret` (❓ provavelmente a desativação do XIGNCODE).

## Características
- PE32 x86, MSVC 2017, build Jenkins `NorthAmerica_Digimon_RPG_Live_Build_vs2017`. Seções `.text` em VA `0xA71000` (offset de arquivo `0x600`), mais `.themida`/`.boot` residuais.
- **RTTI: 985 classes com nome.** Singletons `CSingleton<T>`: `NetworkMgr`, `CGameMgr`, `CSceneMgr`, `CMapBattle`, `CMapField`, `CItemMgr`, `CSkillMgr`, `CDRQuestMgr`, `CPartyManager`, `CFriendList`, `CTutorialMgr`… e uma classe `CWindow_*` por NPC (ex.: `CWindow_Spiral_Mountain_Piemon`).
- Bibliotecas estáticas que **não** precisam ser decompiladas: OpenSSL 1.1.1d, libcurl, cliente FTP (`nsFTP`), CRT.
- Gráficos: **DirectDraw 7** (`DirectDrawCreateEx`), 2D. Som: DirectSound. Ver [qualidade de vida](qualidade-de-vida.md).
- ~25 mil funções com prólogo `push ebp`.

## Dispatcher de pacotes (cena de jogo)
✅ Localizado por análise estática (objdump), ainda não aberto no Ghidra.
- Volta do loop: `0xDD64A0`. Função começa por volta de `0xDCE4B3` e tem frame de pilha enorme (~0x36ABC).
- Validações: `cmp byte [ebp-0x64d4], 0xCC` (assinatura) e `cmp dword [ebp-0x64d0], 0x3080` (tamanho máximo).
- Switch: `movzx eax, word [ebp-0x64d2]` (opcode); `cmp eax, 0x179`; tabela de índices de 1 byte em **`0xDE50EC`** (0x17A entradas); jump table em **`0xDE4D18`**; o default volta para `0xDD64A0`.
- Resultado: 246 opcodes tratados, listados em [opcodes](../protocolo/opcodes.md).
- Para converter VA em offset de arquivo: `off = va − 0xA71000 + 0x600`.

## IPs fixos
Tabela de strings perto de `0x8BFB1C`. Ver [servidores](../protocolo/servidores.md).

## Próximos passos de RE
1. Abrir no Ghidra com análise RTTI e criar labels `OnPacket_0xNNN` a partir da jump table.
2. Achar os outros dispatchers (seleção de personagem, loading, global), a partir das xrefs de `NetworkMgr`.
3. Achar as rotinas de envio (C2S) do `NetworkMgr`.
