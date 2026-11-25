-- 0012: moedas especiais do personagem para lojas de NPC (0x0053 actions 3/4/5 e S2C 0x00CB).

ALTER TABLE `character`
  ADD COLUMN IF NOT EXISTS `medals` smallint(5) unsigned NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `coins` smallint(5) unsigned NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `ladder_points` int(10) unsigned NOT NULL DEFAULT 0;
