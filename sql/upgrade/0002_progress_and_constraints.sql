-- 0002: progresso persistido (bits, EXP, pontos distribuídos, vitórias) e unicidade.

UPDATE `character` SET `pos_y` = 0 WHERE `pos_y` IS NULL;

ALTER TABLE `character`
  MODIFY `pos_y` smallint(6) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `bits` double NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `total_wins` int(11) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `total_battles` int(11) NOT NULL DEFAULT 0;

ALTER TABLE `digimon`
  ADD COLUMN IF NOT EXISTS `exp` bigint(20) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `alloc_str` int(11) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `alloc_agi` int(11) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `alloc_con` int(11) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `alloc_int` int(11) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `ability_points` smallint(5) unsigned NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `skill_points` smallint(5) unsigned NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `total_wins` int(11) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `total_battles` int(11) NOT NULL DEFAULT 0;

-- a checagem de nome livre no código tinha corrida; o banco garante
CREATE UNIQUE INDEX IF NOT EXISTS `account_username_UQ` ON `account` (`username`);
CREATE UNIQUE INDEX IF NOT EXISTS `character_name_UQ` ON `character` (`name`);
CREATE UNIQUE INDEX IF NOT EXISTS `character_slot_UQ` ON `character` (`account_uid`, `slot`);
