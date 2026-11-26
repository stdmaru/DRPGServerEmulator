-- 0015: launcher login (docs/protocolo/launcher.md).
-- email: optional second login name (up to 255 bytes, unique).
-- password_hash replaces `password`: existing passwords are copied as 'plain$<password>' and the column is dropped;
-- the server turns every 'plain$' value into a PBKDF2-SHA256 hash at start (Game/Accounts/PasswordHash.cs), so no
-- plain password survives the first start.
-- auth_key_expires_at: Unix seconds UTC after which the game key issued by the launcher is refused (0 = no expiry,
-- for keys written by older issuers).

ALTER TABLE `account`
  ADD COLUMN IF NOT EXISTS `email` varchar(255) DEFAULT NULL AFTER `username`,
  ADD COLUMN IF NOT EXISTS `password_hash` varchar(300) DEFAULT NULL AFTER `email`,
  ADD COLUMN IF NOT EXISTS `auth_key_expires_at` int(10) unsigned NOT NULL DEFAULT 0 AFTER `auth_key_hash`;

UPDATE `account` SET `password_hash` = CONCAT('plain$', `password`)
  WHERE `password_hash` IS NULL AND `password` IS NOT NULL AND `password` <> '';

ALTER TABLE `account` DROP COLUMN IF EXISTS `password`;

CREATE UNIQUE INDEX IF NOT EXISTS `ux_account_email` ON `account` (`email`);
