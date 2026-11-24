-- 0008: persiste HP, VP e EVP atuais do digimon para evitar recuperação gratuita ao relogar.
-- NULL indica registro antigo ou recém-criado (carrega com HP/VP/EVP cheios).

ALTER TABLE `digimon`
  ADD COLUMN IF NOT EXISTS `hp` int(11) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `vp` int(11) DEFAULT NULL,
  ADD COLUMN IF NOT EXISTS `evp` int(11) DEFAULT NULL;
