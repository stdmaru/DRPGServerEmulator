# Portais

## De onde vem cada parte
- **Posição do portal e mapa de destino:** `map_us` do cliente, colunas 16..31 (até 4 portais por mapa, com o
  **nome** do mapa de destino). ✅ A posição bate com o passo do jogador antes do `0xB0` nas capturas.
- **Ponto de chegada:** não está no cliente. O oficial manda no `0xB0`.
  - ✅ 15 portais capturados ficam em `Resources/Map/Portals.xml` e valem mais que o `map_us`.
  - ❓ Os outros usam uma estimativa: o portal de volta no mapa de destino, deslocado até 5 casas por eixo
    para o centro dos portais daquele mapa. Nas capturas a chegada fica de 4 a 9 casas para dentro.

## Resolução do nome
1. Mapas com o nome do destino. Se houver mais de um (salas de dungeon repetem nomes), fica o que tem portal
   de volta para o mapa de origem.
2. Sem portal de volta e sem `Portals.xml`, o portal fica de fora.

No pacote real: 105 portais no `map_us`, 103 resolvidos. Ficam de fora 38 → "Digimon Labyrinth 3F" e
77 → "Sky Dungeon 2F" (sem volta; ❓ escadas de mão única).

## O nome no `map_us` nem sempre é o destino
O mapa 40 (Gekomon Village) diz "West of Gekomon Forest" no portal (4, 80). Nas capturas, o oficial manda
para o 44 (West of Gekomon Village), que é o vizinho no mapa-múndi. Os mapas 13 e 44 também não têm o nome
de volta esperado. Os três estão no `Portals.xml`.

## Teleportes que não são portal
`0xB0` para (1, 58, 82) e (46, 15, 15) aparecem sem portal por perto: ❓ retorno à cidade e warp de NPC.
