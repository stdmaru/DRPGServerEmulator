#!/usr/bin/env python3
"""
Gera src/DRPGServer/Resources/Digimon/DigimonTitles.xml a partir de table\\digimon_title_us.dat (v322), offline.

- Nome e id vêm da tabela (colunas 0 e 1).
- Bônus: as linhas "※ ..." das colunas 3..13 viram <Modifier>. O servidor nunca lê esse texto.
- Regras de desbloqueio automático não existem na tabela: ficam em UNLOCKS abaixo, com a evidência.

Uso (de work/):
  python3 -I server/tools/extract_digimon_titles.py client/table.hf client/table.pf \\
      server/src/DRPGServer/Resources/Digimon/DigimonTitles.xml --pack tools
"""
import argparse
import importlib.util
import os
import re
import struct
import zlib
from xml.sax.saxutils import quoteattr

TABLE = 'table\\digimon_title_us.dat'

RE_STAT = re.compile(r'(All stats|Max HP|Max VP|Battle Rating|STR|AGI|CON|INT|HP|VP|ATK|DEF|BR)\s*\+?\s*(\d+)\s*(%?)', re.I)

# ✅ Confirmados em digimon_title_us.dat e capturas v322 (conn=76, conn=141; 30402/30444 em conn=137 pid=80450/80451).
# Losses = batalhas menos vitórias (fuga conta como derrota). Stage usa o nome do estágio.
UNLOCKS = {
    20000: [('Stage', 'Mega')],
    20010: [('Level', 100)],
    20020: [('Level', 110)],
    20030: [('Level', 120)],
    20040: [('Level', 130)],
    20050: [('Level', 140)],
    30402: [('Level', 150)],
    30444: [('Level', 160)],
    20100: [('Wins', 1)],
    20110: [('Wins', 10)],
    20120: [('Wins', 100)],
    20130: [('Wins', 1000)],
    20140: [('Wins', 10000)],
    20200: [('Losses', 1)],
    20210: [('Losses', 100)],
    20220: [('Losses', 1000)],
    20230: [('Losses', 10000)],
}

# Ordem fixa de escrita: (chave, StatModifierKind, forma).
MODIFIERS = [
    ('Str', 'Str', 'Value'), ('Agi', 'Agi', 'Value'), ('Con', 'Con', 'Value'), ('Int', 'Int', 'Value'),
    ('Hp', 'Hp', 'Value'), ('Vp', 'Vp', 'Value'),
    ('Atk', 'Atk', 'Value'), ('Def', 'Def', 'Value'), ('Br', 'Br', 'Value'),
    ('AtkPercent', 'Atk', 'Percent'), ('DefPercent', 'Def', 'Percent'), ('BrPercent', 'Br', 'Percent'),
]


def parse_bonus(lines):
    b = {key: 0 for key, _, _ in MODIFIERS}
    for line in lines:
        for m in RE_STAT.finditer(line):
            st = m.group(1).strip().upper()
            v = int(m.group(2))
            pct = m.group(3) == '%'
            if st == 'ALL STATS':
                b['Str'] += v; b['Agi'] += v; b['Con'] += v; b['Int'] += v
            elif st in ('STR', 'AGI', 'CON', 'INT'):
                b[st.capitalize()] += v
            elif st in ('HP', 'MAX HP'):
                b['Hp'] += v  # ❓ "HP +N%" é tratado como plano, como sempre foi
            elif st in ('VP', 'MAX VP'):
                b['Vp'] += v
            elif st == 'ATK':
                b['AtkPercent' if pct else 'Atk'] += v
            elif st == 'DEF':
                b['DefPercent' if pct else 'Def'] += v
            elif st in ('BR', 'BATTLE RATING'):
                b['BrPercent' if pct else 'Br'] += v
    return b


def load_pack(pack_dir):
    spec = importlib.util.spec_from_file_location('drpg_pack', os.path.join(pack_dir, 'drpg_pack.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_rows(drpg_pack, hf_path, pf_path, table):
    _, entries = drpg_pack.read_hf(hf_path)
    with open(pf_path, 'rb') as pf:
        for name, off, ln in entries:
            if name.lower() != table:
                continue
            pf.seek(off)
            plain = zlib.decompress(drpg_pack.old_decode(zlib.decompress(pf.read(ln))))
            count = struct.unpack_from('<I', plain, 0)[0]
            p = 4
            rows = []
            for _ in range(count):
                n = struct.unpack_from('<I', plain, p)[0]; p += 4
                rows.append(plain[p:p + 2 * n].decode('utf-16le').split('|')); p += 2 * n
            return rows
    raise SystemExit(f'{table} não encontrado no pacote')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('hf', help='table.hf do cliente v322')
    ap.add_argument('pf', help='table.pf do cliente v322')
    ap.add_argument('out', help='DigimonTitles.xml de saída')
    ap.add_argument('--pack', required=True, help='pasta com drpg_pack.py (work/tools)')
    args = ap.parse_args()

    titles = {}
    for r in read_rows(load_pack(args.pack), args.hf, args.pf, TABLE):
        if len(r) < 4 or not r[0].isdigit() or int(r[0]) == 0 or int(r[0]) in titles:
            continue
        title_id = int(r[0])
        bonus = parse_bonus(r[3:min(len(r), 14)])
        modifiers = [(kind, form, bonus[key]) for key, kind, form in MODIFIERS if bonus[key]]
        titles[title_id] = (r[1].strip(), modifiers)

    missing = sorted(set(UNLOCKS) - set(titles))
    if missing:
        raise SystemExit(f'UNLOCKS cita títulos que a tabela não tem: {missing}')

    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        '<!-- Gerado por server/tools/extract_digimon_titles.py de table\\digimon_title_us.dat v322. -->',
        '<!-- <Modifier>: bônus do título equipado. <Unlock>: desbloqueio automático (todas as condições). -->',
        '<DigimonTitles>',
    ]
    for title_id in sorted(titles):
        name, modifiers = titles[title_id]
        unlocks = UNLOCKS.get(title_id, [])
        head = f'  <Title ID="{title_id}" Name={quoteattr(name)}'
        if not modifiers and not unlocks:
            lines.append(head + ' />')
            continue
        lines.append(head + '>')
        for kind, form, value in modifiers:
            lines.append(f'    <Modifier Kind="{kind}" {form}="{value}" />')
        for condition, value in unlocks:
            lines.append(f'    <Unlock Condition="{condition}" Value="{value}" />')
        lines.append('  </Title>')
    lines.append('</DigimonTitles>')

    with open(args.out, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')

    print(f'{len(titles)} títulos em {args.out}')


if __name__ == '__main__':
    main()
