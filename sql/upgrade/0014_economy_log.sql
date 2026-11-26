-- 0014: trilha de auditoria da economia (docs/emulador/seguranca.md#auditoria).
-- Uma linha por movimentação de moeda ou item, gravada na mesma transação da operação. Sem chave estrangeira: a trilha
-- fica mesmo se a conta ou o personagem for apagado. `id` vem do servidor (UUID v7), e a gravação repetida do mesmo
-- snapshot não duplica (INSERT IGNORE).

CREATE TABLE IF NOT EXISTS `economy_log` (
  `id` binary(16) NOT NULL,
  `at` datetime(3) NOT NULL,
  `account_uid` int(10) unsigned NOT NULL,
  `character_uid` int(10) unsigned DEFAULT NULL,
  `source` varchar(24) NOT NULL,
  `action` varchar(24) NOT NULL,
  `asset` varchar(16) NOT NULL,
  `asset_id` int(10) unsigned NOT NULL DEFAULT 0,
  `amount` bigint(20) NOT NULL,
  `balance_after` bigint(20) DEFAULT NULL,
  `detail` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `economy_log_account` (`account_uid`, `at`),
  KEY `economy_log_character` (`character_uid`, `at`),
  KEY `economy_log_at` (`at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;
