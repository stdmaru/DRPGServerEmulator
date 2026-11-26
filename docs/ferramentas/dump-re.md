# Dump de engenharia reversa (Ghidra)

Consultar o original é obrigatório no cliente novo ([plano](../planos/cliente-novo.md)). Consultar o REA/Ghidra ao vivo é lento: a análise do binário estoura o tempo da chamada. Também é caro em contexto. Por isso a análise roda **uma vez**, em modo headless, e o resultado vai para arquivos de texto pesquisáveis com `grep`.

## Gerar
```bash
cd work/server
tools/re/dump.sh ../client/DigimonRPG.bin.orig v322
```
- Cria um projeto Ghidra em `work/re/v322/ghidra/` e o dump em `work/re/v322/dump/`. Na v322 a importação mais a decompilação levam cerca de 1 h (16 núcleos, `MAXMEM=12G`).
- Rodar de novo reaproveita o projeto (`-process -noanalysis`) e só refaz o dump.
- `GHIDRA_HOME` tem como padrão `/opt/ghidra` (Ghidra 12.2).
- **O dump nunca vai para o repositório:** é derivado de binário com copyright ([legal](../projeto/legal.md)).

## Conteúdo (`work/re/v322/dump/`)
| Arquivo | Colunas | Uso |
|---|---|---|
| `functions.tsv` | `entry size callers callees name file` | achar função, ver o tamanho antes de abrir |
| `strings.tsv` | `address functions text` | **ponto de entrada principal**: string → funções que a usam |
| `calls.tsv` | `caller callee callee_name` | quem chama quem, inclusive importações (`KERNEL32.DLL::...`) |
| `symbols.tsv` | `address type name` | símbolos não padrão (CRT, importações, strings de RTTI) |
| `decomp/<4 hex>/<entry>.c` | C do decompilador, com o endereço na 1ª linha | ler a função |
| `SOURCE.sha256` | hash do binário de origem | conferir que o dump é da v322 |

v322: 29.157 funções, 15.824 strings, 101.278 chamadas.

## Receitas
```bash
D=work/re/v322/dump
grep -i 'loading_image' $D/strings.tsv                      # string -> funções
cat $D/decomp/00aa/00aa4a90.c                               # ler uma função
grep -P '\t0x00aa24b0\t' $D/calls.tsv | cut -f1             # quem chama
grep -P '^0x00aa24b0\t' $D/calls.tsv                        # o que ela chama
grep -rl 'DAT_01158504 + 0xb8' $D/decomp                    # quem usa um campo global
```
- **Classes:** o analisador de RTTI do Ghidra **não** resolveu as classes neste dump desempacotado. Pelo nome da classe: string `.?AVCSceneTitle@@` → TypeDescriptor (string − 8) → CompleteObjectLocator (referência ao TD − 12) → vtable (referência ao COL + 4). O script da investigação está no histórico do PR #42.
- Cite sempre o endereço (`0x00aa24b0`) no código e na wiki.

## Pontos conhecidos (v322)
| Endereço | O quê |
|---|---|
| `0x00aa4a90` | `WinMain`: argumentos → carga de `Resource`/`Table` → janela → DirectDraw → `Game Init` → laço |
| `0x00aa24b0` | parse da linha de comando: separa por espaço e exige **exatamente 2** tokens (usuário, hash); senão mostra uma caixa de erro e sai. Guarda em `[DAT_01158504+0xb8]` e `[+0xd0]` |
| `0x00aa2c10` | `Game Init` (`SceneLoad Failed!`) |
| `0x0102f968` | vtable de `CSceneTitle` (4 métodos: `0x00b54c20` dtor, `0x00b54c60` init, `0x00b54cc0` frame); estado `0x16 → 0x17` |
| `0x00ad3d30` | carga inicial chamada pelo `CSceneTitle` no estado `0x16` |
| `0x00b3a230` | lê `[Server] Login/Session` de `.\drpg.ini` (IPs alternativos) |
| vtables | `CSceneServer` `0x102f62c`, `CSceneCharSel` `0x102ebc4`, `CSceneGame` `0x102ee7c` |
