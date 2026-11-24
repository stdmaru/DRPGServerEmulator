-- 0007: títulos de Digimon (0x97 lista até 200 títulos; 0x99 equipar/desequipar; offset 320 no bloco de status).
-- Ver docs/mecanicas/titulos.md.

ALTER TABLE `digimon`
  ADD COLUMN IF NOT EXISTS `title_id` int(10) unsigned NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS `digimon_title` (
  `digimon_uid` int(10) unsigned NOT NULL,
  `title_id` int(10) unsigned NOT NULL,
  PRIMARY KEY (`digimon_uid`, `title_id`),
  CONSTRAINT `digimon_title_digimon_FK` FOREIGN KEY (`digimon_uid`) REFERENCES `digimon` (`uid`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;
