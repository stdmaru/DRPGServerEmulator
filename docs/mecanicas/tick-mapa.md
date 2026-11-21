# Tick do mapa, keep-alive e saída

Evidência: `captures.db`, sessões 52–56 (v322), conexões do servidor de mapa. Mostra quando o oficial manda pacotes sem pedido.

## Keep-alive (0xC4) ✅
| Servidor | Quem pinga | Resposta |
|---|---|---|
| channel, global | o cliente (14 bytes zero) | o servidor responde vazio (25 + 25 amostras) |
| mapa | **o servidor**, a cada ~5,2 s (1161 amostras: p10 5,1 s, mediana 5,2 s, p90 5,3 s), também durante a batalha | o cliente responde com `0xC4` em 0–40 ms; o servidor **não** responde de volta |

## Recuperação (0xB2) ✅
No mesmo tick de ~5,2 s, fora da batalha, o líder recupera **HP +5, VP +3, EVP +10**, limitado ao máximo e sem relação com ele (349 ticks; máximos de 115 a 429). O `0xB2` (bloco de status, 696 bytes) só sai quando algum valor estava abaixo do máximo. Um `0xB2` por tick, só do líder.

## Outros pacotes frequentes sem pedido
- `0x11` (56 bytes, ~62 mil amostras): atualização dos digimons selvagens (serial, nome, posição). Movimento, não timer.
- `0x10` (204 bytes): outros jogadores. Ver [resumo do tamer](../protocolo/structs/tamer-resumo.md).

## Saída (0x2B) ✅
Cliente `0x2B` (14 bytes zero) → servidor `0x2B` vazio em ~0,3 s → o cliente fecha a conexão em ~1 a 1,5 s.

## No emulador
`Game/World/Zone.cs` (`AutoRecovery`, a cada 5200 ms: ping e recuperação) e `Network/Handlers/CommonHandlers.cs` (no mapa o `0xC4` do cliente é ignorado). Teste: `ZoneTickTests`.
