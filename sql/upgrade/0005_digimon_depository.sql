-- 0005: Digimon Depository (armazém de até 10 Digimons, Map 0x89 / 0x8A / 0x94).
-- Reutiliza a tabela `digimon` com a flag `in_depository` e guarda a quantidade de slots liberados em `character`.
-- Ver docs/mecanicas/depository.md.

ALTER TABLE `character`
  ADD COLUMN IF NOT EXISTS `depository_slots` tinyint(3) unsigned NOT NULL DEFAULT 0;

ALTER TABLE `digimon`
  ADD COLUMN IF NOT EXISTS `in_depository` tinyint(1) unsigned NOT NULL DEFAULT 0;
