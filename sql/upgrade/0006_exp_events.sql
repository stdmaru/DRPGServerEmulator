-- 0006: Eventos periódicos de bônus de EXP (Map S2C 0xE6, cifrado com AES-128-ECB da sessão).
-- O servidor consulta esta tabela periodicamente (Game:EventPollSeconds, padrão 60s) e sob demanda via /reloadevents.
-- Ver docs/mecanicas/formulas-exp.md.

CREATE TABLE IF NOT EXISTS `exp_event` (
  `id`            int(10) unsigned NOT NULL AUTO_INCREMENT,
  `name`          varchar(64) NOT NULL DEFAULT '',
  `bonus_percent` int(10) unsigned NOT NULL,
  `is_hot_time`   tinyint(1) unsigned NOT NULL DEFAULT 0,
  `starts_at`     datetime NOT NULL,
  `ends_at`       datetime NOT NULL,
  `enabled`       tinyint(1) unsigned NOT NULL DEFAULT 1,
  PRIMARY KEY (`id`),
  KEY `ix_exp_event_active` (`enabled`, `starts_at`, `ends_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
