-- 0003: time de 5 posições, formas de evolução, inventário e flags de NPC (tutorial).
-- digimon.digimon_id passa a ser a forma-base (in-training ou rookie); os atributos distribuídos e os
-- níveis das skills ficam por forma em digimon_form. Ver docs/mecanicas/evolucao.md.

ALTER TABLE `digimon`
  ADD COLUMN IF NOT EXISTS `slot` tinyint(3) unsigned NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `unlocked_stage` tinyint(3) unsigned NOT NULL DEFAULT 0;

-- o líder já é a posição 0; os outros digimons de cada personagem vão para 1, 2, … pela ordem do uid
UPDATE `digimon` d
JOIN (
  SELECT uid, ROW_NUMBER() OVER (PARTITION BY character_uid ORDER BY is_leader DESC, uid) - 1 AS pos
  FROM `digimon`
) o ON o.uid = d.uid
SET d.slot = LEAST(o.pos, 4);

CREATE TABLE IF NOT EXISTS `digimon_form` (
  `digimon_uid` int(10) unsigned NOT NULL,
  `species_id` smallint(5) unsigned NOT NULL,
  `alloc_str` int(11) NOT NULL DEFAULT 0,
  `alloc_agi` int(11) NOT NULL DEFAULT 0,
  `alloc_con` int(11) NOT NULL DEFAULT 0,
  `alloc_int` int(11) NOT NULL DEFAULT 0,
  `skill1_level` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `skill2_level` tinyint(3) unsigned NOT NULL DEFAULT 0,
  PRIMARY KEY (`digimon_uid`, `species_id`),
  CONSTRAINT `digimon_form_digimon_FK` FOREIGN KEY (`digimon_uid`) REFERENCES `digimon` (`uid`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

-- a alocação que existia era da forma-base
INSERT IGNORE INTO `digimon_form` (digimon_uid, species_id, alloc_str, alloc_agi, alloc_con, alloc_int)
SELECT uid, digimon_id, alloc_str, alloc_agi, alloc_con, alloc_int FROM `digimon`;

CREATE TABLE IF NOT EXISTS `inventory_item` (
  `character_uid` int(10) unsigned NOT NULL,
  `bag` tinyint(3) unsigned NOT NULL,
  `slot` tinyint(3) unsigned NOT NULL,
  `uid` int(10) unsigned NOT NULL,
  `item_id` int(10) unsigned NOT NULL,
  `amount` int(10) unsigned NOT NULL,
  PRIMARY KEY (`character_uid`, `bag`, `slot`),
  CONSTRAINT `inventory_item_character_FK` FOREIGN KEY (`character_uid`) REFERENCES `character` (`uid`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

CREATE TABLE IF NOT EXISTS `character_flag` (
  `character_uid` int(10) unsigned NOT NULL,
  `flag` smallint(5) unsigned NOT NULL,
  PRIMARY KEY (`character_uid`, `flag`),
  CONSTRAINT `character_flag_character_FK` FOREIGN KEY (`character_uid`) REFERENCES `character` (`uid`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;
