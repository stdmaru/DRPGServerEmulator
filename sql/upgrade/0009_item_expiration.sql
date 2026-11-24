-- 0009: persiste expiração (Unix timestamp UTC) e duração em segundos de itens de período.
-- 0 = item permanente.

ALTER TABLE `inventory_item`
  ADD COLUMN IF NOT EXISTS `expires_at` int(10) unsigned NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `duration_seconds` int(10) unsigned NOT NULL DEFAULT 0;
