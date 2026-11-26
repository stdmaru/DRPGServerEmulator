-- 0013: auth_key guardada como hash (PBKDF2-SHA256, docs/emulador/seguranca.md#autenticação-entre-servidores).
-- O servidor converte as chaves em texto puro na partida e no login, e apaga a coluna auth_key da conta convertida.

ALTER TABLE `account`
  ADD COLUMN IF NOT EXISTS `auth_key_hash` varchar(128) DEFAULT NULL AFTER `auth_key`;
