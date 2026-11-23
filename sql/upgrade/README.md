# Migrações do banco

O servidor aplica estes arquivos na partida, em ordem, e registra cada um em `schema_version`.

- Nome: `NNNN_descricao.sql` (quatro dígitos, sem buracos obrigatórios, ordem numérica).
- **Nunca edite um arquivo já aplicado**: o servidor compara o SHA-256 e recusa subir se mudou. Para corrigir, crie um novo.
- DDL no MariaDB não é transacional. Escreva de forma idempotente (`IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`), para que uma migração interrompida possa rodar de novo.
- `DELIMITER` não funciona (o arquivo vai inteiro num comando).
