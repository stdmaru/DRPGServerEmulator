# Emulador: dados de jogo

De onde vêm as tabelas, os modificadores de status e os efeitos. Visão geral em [arquitetura](arquitetura.md). As colunas do cliente estão em [arquivos de dados](../cliente/arquivos-de-dados.md).

> Peças marcadas com **(PR #41: Xn)** entram no item `Xn` do checklist do PR de refatoração da fundação.

## Fontes
| Fonte | Onde | O que tem | Quem lê |
|---|---|---|---|
| Tabelas do cliente | `Resources/Client/table.pf` (fora do git) | espécies, itens, cards, skills, mapas, Cash Shop | `ClientTables` |
| Dados do servidor | `Resources/<Domínio>/*.xml` (versionados) | o que o cliente não tem ou só tem como texto: spawns, drops, chegadas de portal, lojas, efeitos de equipamento, card, título e uso de item | `GameDataLoader` |
| Grades | `Resources/Client/grids.bin` (fora do git) | célula andável de cada mapa | `MapGrid` |

Tudo vira o `GameData` imutável (`FrozenDictionary`) na partida.

## Regras
1. **Texto de UI nunca define dado de jogo.** Descrição, resumo e nome não são lidos para extrair atributo, efeito, preço ou regra, nem em runtime nem na carga. Se o dado só existe como texto no cliente, uma **ferramenta offline** gera o XML estruturado, uma pessoa revisa e o XML é commitado. O servidor só lê o XML (**PR #41: E2**).
2. **Coluna do cliente tem nome.** `ClientTables` traduz índice de coluna para propriedade com nome de domínio (`CoinPrice`, não `Col16`). Coluna ainda sem significado conhecido fica `UnknownColNN` (`NN` = índice da coluna, a partir de 0), com o que já se sabe no comentário. Ex.: `ItemInfo.UnknownCol23`, `CardInfo.UnknownCol30`.
3. **Fail-fast.** Referência quebrada (item, espécie, mapa ou drop inexistente), valor fora do intervalo, atributo XML desconhecido ou duplicado derrubam a partida com uma mensagem que diz arquivo, elemento e valor.
4. **Sem números mágicos no código.** Preço, chance, duração e limite de jogo vêm da tabela ou do XML. O que é constante confirmada do protocolo (✅) fica como `const` com nome e evidência.
5. **Gerador versionado.** Ferramenta que gera um XML de `Resources/` fica em `tools/` no repositório, com página na seção Ferramentas do [índice](../README.md), e o XML diz no comentário de topo qual ferramenta e qual tabela o geraram.

## Modificadores de status (`Game/Stats/StatModifiers.cs`)
Bônus de equipamento, título, card e buff são todos `StatModifier`: um tipo de modificador (`StatModifierKind`), um valor e uma forma (plano ou percentual). O XML é o mesmo em todos os arquivos:

```xml
<Equipment ItemID="1" Name="Crest of Courage +1">
  <Modifier Kind="Atk" Value="15" />
  <Modifier Kind="Def" Value="10" />
  <Modifier Kind="Br" Value="50" />
</Equipment>

<Title ID="20100" Name="First Win">
  <Modifier Kind="Str" Value="1" />
  <Modifier Kind="Atk" Percent="5" />
</Title>
```

- `StatModifierKind` é um enum fechado. `StatModifierRules` diz, por tipo, como acumula (soma; máximo em `CardSlashLimit`; "algum" em `MaintainEvolution`) e que formas aceita (atributos e recuperação só plano; EXP do Digimon e dano só %; ATK/DEF/BR os dois). Forma não aceita é erro na carga. Tipo novo = um valor no enum, a regra em `StatModifierRules` e o uso no cálculo.
- `StatModifiers` é o conjunto agregado de uma ou mais fontes: `Of` junta os modificadores de uma fonte, `Combine` junta fontes, e a leitura é `Flat(kind)`, `Percent(kind)`, `Has(kind)` e `Attributes`.
- Fontes: `ItemInfo.Modifiers` (equipamento), `DigimonTitleInfo.Modifiers` (título). `Inventory.EquipmentModifiers()` junta o que o tamer veste; `Character.SyncEquipmentModifiers` repassa a cada Digimon; `Digimon.Modifiers` junta equipamento e título equipado e fica em cache até `SetEquipmentModifiers`, `EquipTitle` ou `LoadTitles`.
- Cálculo em `Digimon`: atributos somam antes de `StatFormulas.Combat`; HP/VP planos somam depois; ATK/DEF/BR = `(base + plano) × (1 + %)`, arredondado para baixo. Os modificadores de batalha e de zona (`Damage`, `CardSlashLimit`, `HpRecovery`…) são lidos de `Character.EquipmentModifiers` por quem os usa.
- `ModifierXml.Read` lê os `<Modifier>` de qualquer fonte e derruba a carga em tipo desconhecido (nome exato do enum, nunca número), forma não aceita, `Value` e `Percent` juntos ou ausentes, valor que não é inteiro positivo, atributo a mais ou elemento estranho.
- `EquipmentEffects.xml` é gerado por [`tools/extract_equipment_effects.py`](../ferramentas/extract-equipment-effects.md) e `Digimon/DigimonTitles.xml` por [`tools/extract_digimon_titles.py`](../ferramentas/extract-digimon-titles.md).

## Efeitos (**PR #41: E3, E4**)
Comportamento (curar, teleportar, capturar, dar item, atacar todos) é um **efeito tipado**, nunca um `switch` por id de item ou card:

```xml
<Card ID="64" Name="Golden Drill">
  <Effect Type="AllTargets" />
  <Effect Type="GuaranteedHit" />
</Card>

<Item ID="900" Name="Teleport">
  <OnUse>
    <Effect Type="TeleportInMap" />
  </OnUse>
</Item>
```

- Cada `Type` é uma classe C# de efeito com parâmetros tipados (`Amount`, `Percent`, `MapId`…). O loader liga atributo XML a parâmetro e recusa atributo que o efeito não declara.
- **Cards (`Resources/Card/CardEffects.xml`, mantido à mão):** `CardEffectTypes` liga cada `Type` a uma fábrica que lê os parâmetros por `EffectParams` (`PositiveInt`, `Enum<T>`, `RequireOneOf`; atributo não lido é erro). O efeito é `ICardAttackEffect`, `ICardDefenseEffect` ou os dois (`Br`) e tem que valer no gatilho do card (coluna 23), senão a carga cai. `CardInfo.Effects` guarda a lista; `CardEffectEvaluator` junta os efeitos ativados no turno; `Haste` e `SlotReturn` são lidos por `CardBattleState` pelo tipo (`CardInfo.Has<T>()`). Card sem entrada no XML ativa e consome sem efeito.
- Cada efeito tem `Validate` (pode aplicar agora? alvo certo, espaço, estado) e `Apply`. O serviço chama `Validate` de **todos** os efeitos antes de aplicar qualquer um, e só então consome o item.
- Efeito que não cabe em parâmetros simples continua sendo uma classe de efeito com nome próprio, registrada pelo **tipo**, nunca pelo id do item.
- **Uso de item (`Resources/Item/ItemEffects.xml`, mantido à mão):** `ItemUseEffectTypes` liga `Type` a fábrica (mesmo `EffectParams` dos cards); `ItemInfo.UseEffects` guarda a lista. Cada efeito (`IItemUseEffect`) declara a entrada (`OnTarget` = `0x41` no selvagem, `AtPosition` = `0x85` numa posição; um item não mistura as duas), `CanApply` sem efeito colateral e `Apply`. `ItemUseService` valida todos e só depois consome; a ordem consumo × resposta é a de cada entrada (✅ `0x85` ecoa a quantidade de antes e o `0xC9` a de depois, conn=125/469; `0x41` consome antes de sortear a rede). Hoje: `TeleportInMap` (900, 907) e `TamingNet` (`Stage` pelo nome, `Chance` 1..100). Item citado que não existe no `item_info_us` derruba a carga.
- ❓ **Formato definitivo dos efeitos de uso de item em aberto**: elementos estruturados (acima) × scripts no estilo rAthena (plano do PR de item scripts). A decisão fica para depois da refatoração da fundação; a base acima é o ponto de entrada. Seja qual for o formato, os efeitos são validados na carga e nunca vêm de texto de UI.

## Regras de desbloqueio (`Game/Digimons/TitleUnlocks.cs`)
Condições de jogo (títulos por vitórias, derrotas, nível e estágio) também são dados tipados, em `Resources/Digimon/DigimonTitles.xml`:

```xml
<Title ID="20010" Name="Break Through Fixed EXP">
  <Modifier Kind="Str" Value="5" />
  <Unlock Condition="Level" Value="100" />
</Title>
<Title ID="20000" Name="A New Start">
  <Modifier Kind="Int" Value="3" />
  <Unlock Condition="Stage" Value="Mega" />
</Title>
```

- `Condition` é o enum fechado `TitleUnlockCondition` (`Wins`, `Losses`, `Level`, `Stage`), e cada um vira uma classe `ITitleUnlockRule` (`WinsUnlock`, `LossesUnlock`, `LevelUnlock`, `StageUnlock`). `Value` é inteiro positivo; em `Stage` é o nome do estágio. Condição nova = um valor no enum, uma classe e uma linha em `TitleUnlockRules.Create`.
- Um título com várias `<Unlock>` exige todas. Sem `<Unlock>`, só sai por evento, item ou GM.
- `DigimonTitles.CheckUnlocks` percorre `GameData.AutoUnlockTitles` em ordem de id. O loader recusa condição ou valor inválido, atributo ou elemento a mais, id 0 e id repetido.
