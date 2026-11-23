-- 0005: histórico permanente da enciclopédia por personagem (Global 0x7B).
-- ✅ O pacote 0x7B carrega um array de 1100 bytes (owned[speciesId]) que não é limpo pelo reset do 0x7C.
-- Ver docs/mecanicas/enciclopedia.md.

CREATE TABLE IF NOT EXISTS `character_encyclopedia` (
  `character_uid` int(10) unsigned NOT NULL,
  `species_id` smallint(5) unsigned NOT NULL,
  PRIMARY KEY (`character_uid`, `species_id`),
  CONSTRAINT `character_encyclopedia_character_FK` FOREIGN KEY (`character_uid`) REFERENCES `character` (`uid`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

-- popula com todas as espécies já possuídas pelos personagens existentes (forma-base e evoluções)
INSERT IGNORE INTO `character_encyclopedia` (character_uid, species_id)
SELECT character_uid, digimon_id FROM `digimon`;

INSERT IGNORE INTO `character_encyclopedia` (character_uid, species_id)
SELECT d.character_uid, f.species_id
FROM `digimon_form` f
JOIN `digimon` d ON d.uid = f.digimon_uid;
