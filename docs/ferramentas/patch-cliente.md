# patch_client.py (XIGNCODE, tela congelada no loading, IPs)

`work/tools/client/patch_client.py`. Os patches são localizados **por assinatura**, não por offset, então sobrevivem a atualizações do cliente. Cada assinatura precisa casar exatamente uma vez; se não casar, nada é gravado.

```bash
cd work/tools/client
python3 patch_client.py check ../../client/DigimonRPG.bin              # empacotado? patches? IPs?
python3 patch_client.py apply ../../client/DigimonRPG.bin --in-place   # xigncode + loadingbar (backup em .orig)
python3 patch_client.py apply in.bin out.bin --proxy-config ../proxy/config.toml   # + IPs das rotas do proxy
python3 patch_client.py apply in.bin out.bin --ip 52.238.29.166=127.0.0.3          # IP avulso
```

## Patches
| Nome | O que faz | Assinatura | Validado |
|---|---|---|---|
| `xigncode` | Faz a função de init do XIGNCODE retornar 1 (`mov eax,1; ret; nop×3` no lugar de `push ebp; mov ebp,esp; sub esp,imm32`) | o switch dos códigos de erro do XIGNCODE3 depois do init: `cmp eax,0xE019101C / ja / je / cmp eax,0xE0010001 / je / cmp eax,0xE0010003`; o prólogo `55 8B EC 81 EC` fica até 0x100 bytes antes | v242: VA 0xC4C230, idêntico à amostra `_backups/DigimonRPG.bin_noxign`. v322: VA 0x919890 |
| `loadingbar` | Corrige a tela congelada no loading (ver [Wine](../cliente/wine.md#problema-tela-congelada-no-loading)). A thread da barra pula o desenho (`jmp` do início do bloco de desenho para a checagem de conclusão) e troca a apresentação (22 bytes) por `push 0x10; call [Sleep]`. A tela fica preta durante o loading | A = `lea ecx,[ebp-X]; call; push 0; push -1; push -1; push 0; push 0; sub esp,0x18; mov ecx,esp; push 0x1a`. B = `mov eax,[g]; test eax,eax; je; cmp byte [eax+0x81],1; jne; mov byte [esi+RUN],0; mov byte [esi+DONE],1; mov ecx,[g]; call; mov ecx,[g]; call`, até 0x400 bytes depois de A. O slot do `Sleep` é lido da tabela de import | v322: A VA 0xB356BC, B 0xB358D0, Sleep IAT 0x1006140. v242: casa em VA 0xE4D10C (não testado em jogo) |
| `loadingrace` (opcional, `--enable loadingrace`) | Corrige uma corrida real na criação da thread da barra de loading. **Não** é a causa do congelamento (ver `loadingbar`). Não é aplicado por padrão. Troca o log de debug do início do criador da thread (`push str; mov edi,ecx; call log; add esp,4`, 15 bytes) por `mov edi,ecx; mov byte [edi+FLAG],1; nop×6`: o flag de execução passa a ser gravado **antes** do `CreateThread` | `push <VA da string "PreLoadingBar Thread">` seguido de `mov edi,ecx / call / add esp,4 / lea eax,[ebp-4] / push eax / push 0 / push edi / push imm32 / push 0 / push 0 / call [CreateThread]`. O FLAG é lido do `mov byte [edi+imm32],1` (`C6 87`) depois do `CreateThread` | v322: criador VA 0xB34BA0 (patch em 0xB34BA5), FLAG +0x63C. v242: casa em VA 0xE4C5F5 (não testado em jogo) |
| `--ip` | Troca um IP em texto (NUL-terminado) por outro que caiba no espaço da string | literal | v322 |

A função patchada carrega o XIGNCODE (strings "There is no path to the XignCode", "The file in the Xigncode path is corrupted.") e mostra uma MessageBox com o erro se falhar. Com o stub, o jogo segue como se o init tivesse dado certo. Na v242 isso bastou para jogar no oficial. Na v322: ✅ login e seleção de servidor; ❓ canal e mapa.

## Fluxo quando o oficial atualizar
1. Atualizar pelo launcher (`DigimonRPG.exe`). **O launcher sobrescreve o `DigimonRPG.bin` com a versão empacotada.**
2. ❓ **Dump do Themida**: hoje é manual, feito pelo usuário. Documentar o método aqui e automatizar se possível.
3. `patch_client.py check` no dump. Precisa dar "desempacotado" e 1 ocorrência de `xigncode` e de `loadingbar`.
4. `patch_client.py apply … --in-place`.
5. Rodar o proxy: o perfil compara o tráfego com as versões anteriores (ver [proxy](proxy.md#detecção-de-mudanças)).

Se uma assinatura parar de casar numa versão futura:
- `xigncode`: encontre a nova função (procure pelas strings acima ou pelas constantes 0xE019101C/0xE0010001) e ajuste `locate_xigncode`.
- `loadingbar`: procure o loop da thread pela string "PreLoadingBar Thread..." (criador → proc → loop) e ajuste `_BAR_A`/`_BAR_B`.
- `loadingrace`: procure a referência à string "PreLoadingBar Thread..." e confira se o criador ainda grava o flag depois do `CreateThread`. Ajuste `_LOADING_CREATOR`.
- Registre a versão na tabela acima.
- `--skip NOME` pula um patch; `--enable NOME` liga um opcional.
