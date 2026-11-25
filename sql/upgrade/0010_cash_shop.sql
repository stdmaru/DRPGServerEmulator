-- 0010: saldo de Cash na conta e depósito de itens de Cash (Cash Depository, 24 cards + 24 items).

ALTER TABLE `account`
  ADD COLUMN IF NOT EXISTS `cash` int(10) unsigned NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `bonus_cash` int(10) unsigned NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS `cash_depository_item` (
  `account_uid` int(10) unsigned NOT NULL,
  `kind` tinyint(3) unsigned NOT NULL COMMENT '1 = Card (76 B), 2 = Item (108 B)',
  `slot` tinyint(3) unsigned NOT NULL COMMENT '0..23',
  `uid` int(10) unsigned NOT NULL,
  `item_id` int(10) unsigned NOT NULL,
  `amount` int(10) unsigned NOT NULL,
  `expires_at` int(10) unsigned NOT NULL DEFAULT 0,
  PRIMARY KEY (`account_uid`, `kind`, `slot`),
  CONSTRAINT `cash_depository_item_account_FK`
    FOREIGN KEY (`account_uid`) REFERENCES `account` (`uid`)
    ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;
