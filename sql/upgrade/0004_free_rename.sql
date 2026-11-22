-- 0004: renomeação grátis do digimon (0xF0). ✅ Digimon obtido pelo jogo (captura, ovo) nasce "InputName" e pode
-- ser renomeado uma vez de graça; depois o oficial responde 2 (sessão 44). Os que já existem não têm a grátis.

ALTER TABLE `digimon`
  ADD COLUMN IF NOT EXISTS `free_rename` tinyint(1) unsigned NOT NULL DEFAULT 0;
