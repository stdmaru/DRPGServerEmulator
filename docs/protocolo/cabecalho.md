# Cabeçalho e framing

✅ Confirmado nos 52 pcaps (todas as streams fecham sem desync). Emulador: `Network/Protocol/` (`PacketHeader`, `Crc16`, `PacketFramer`).

Sem criptografia, sem compressão, little-endian.

| Offset | Tipo | Campo | Notas |
|---|---|---|---|
| 0 | u16 | assinatura | sempre `0x00CC` |
| 2 | u16 | opcode | ≤ `0x179` na cena de jogo (ver [binário](../cliente/binario.md)) |
| 4 | u32 | tamanho | **inclui** os 14 bytes do cabeçalho |
| 8 | u32 | sequência | o oficial manda sempre 0 |
| 12 | u16 | CRC16 | ver abaixo |
| 14 | … | payload | |

- ✅ **CRC:** CRC16-XMODEM (poli `0x1021`, início 0) sobre **o payload seguido de 14 bytes zero**. Bate em 4764 de 4810 pacotes capturados. Payload vazio → 0.
- ❓ Os 46 que não batem são todos `0x7B`, `0x7C`, `0xE6` e `0x78`, **nos dois sentidos**: pacotes com chaves de 16 bytes. Talvez o CRC seja do conteúdo antes de alguma cifra. O emulador manda CRC correto nesses e o cliente aceita.
- O emulador não valida o CRC que chega (o cliente manda errado nesses mesmos opcodes).
- **Keep-alive `0xC4`:** o cliente manda payload de 14 zeros em channel, map e global; o servidor responde com `0xC4` vazio.
- **Tamanho máximo:** o dispatcher do cliente rejeita pacote com tamanho > `0x3080` (12416 bytes).
- **Strings:** tamanho fixo, preenchidas com `\0` (ex.: nome de digimon com 21 bytes).
- **Framing no proxy:** `Framer` em `work/tools/proxy/drpg_store.py`. Bytes que não formam um quadro válido são gravados como `raw=1` e o framer ressincroniza na próxima assinatura plausível.
