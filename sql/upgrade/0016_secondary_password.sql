-- 0016: secondary password hash on account (PBKDF2-SHA256 of the client's lowercase hex MD5).
-- Controlled by Game:SecondaryPasswordMode (Disabled / Optional / Required).

ALTER TABLE `account`
  ADD COLUMN IF NOT EXISTS `secondary_password_hash` varchar(128) DEFAULT NULL AFTER `password_hash`;
