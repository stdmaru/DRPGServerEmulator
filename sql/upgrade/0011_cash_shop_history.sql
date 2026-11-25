-- 0011: histórico de compras do Cash Shop (S2C 0x0161).

CREATE TABLE IF NOT EXISTS `cash_shop_history` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `account_uid` int(10) unsigned NOT NULL,
  `item_data_id` int(10) unsigned NOT NULL,
  `quantity` int(10) unsigned NOT NULL,
  `unit_price` int(10) unsigned NOT NULL,
  `cash_after` int(10) unsigned NOT NULL,
  `bonus_cash_after` int(10) unsigned NOT NULL,
  `purchased_at` int(10) unsigned NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_cash_shop_history_account_time` (`account_uid`, `purchased_at`),
  CONSTRAINT `cash_shop_history_account_FK`
    FOREIGN KEY (`account_uid`) REFERENCES `account` (`uid`)
    ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;
