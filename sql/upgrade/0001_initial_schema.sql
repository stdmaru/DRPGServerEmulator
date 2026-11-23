-- 0001: esquema inicial (o mesmo do protótipo, .devcontainer/drpg-struct.sql).
-- Idempotente: bancos criados antes do executor de migrações já têm estas tabelas.

CREATE TABLE IF NOT EXISTS `account` (
  `uid` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `username` varchar(100) NOT NULL,
  `password` varchar(100) NOT NULL,
  `auth_key` varchar(40) DEFAULT NULL,
  `authority` tinyint(3) unsigned DEFAULT NULL,
  PRIMARY KEY (`uid`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

CREATE TABLE IF NOT EXISTS `character` (
  `uid` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `account_uid` int(10) unsigned NOT NULL,
  `name` varchar(100) NOT NULL,
  `tamer_id` tinyint(3) unsigned NOT NULL,
  `level` smallint(5) unsigned NOT NULL DEFAULT 1,
  `map_id` tinyint(3) unsigned NOT NULL,
  `pos_x` smallint(6) NOT NULL,
  `pos_y` smallint(6) DEFAULT NULL,
  `slot` tinyint(3) unsigned NOT NULL,
  PRIMARY KEY (`uid`),
  KEY `character_account_FK` (`account_uid`),
  CONSTRAINT `character_account_FK` FOREIGN KEY (`account_uid`) REFERENCES `account` (`uid`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

CREATE TABLE IF NOT EXISTS `digimon` (
  `uid` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `character_uid` int(10) unsigned NOT NULL,
  `digimon_id` smallint(5) unsigned NOT NULL,
  `name` varchar(100) NOT NULL,
  `level` smallint(5) unsigned NOT NULL DEFAULT 1,
  `is_leader` bit(1) NOT NULL DEFAULT b'0',
  PRIMARY KEY (`uid`),
  KEY `digimon_character_FK` (`character_uid`),
  CONSTRAINT `digimon_character_FK` FOREIGN KEY (`character_uid`) REFERENCES `character` (`uid`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;
