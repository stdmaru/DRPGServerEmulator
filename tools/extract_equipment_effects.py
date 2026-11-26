#!/usr/bin/env python3
"""
Gera src/DRPGServer/Resources/Item/EquipmentEffects.xml a partir de table\\item_info_us.dat (v322), offline.

O texto de bônus (coluna 28) só é lido aqui; o servidor carrega o XML com elementos <Modifier> e não
interpreta texto de UI. Revise o diff do XML antes de commitar.

Uso (de work/):
  python3 -I server/tools/extract_equipment_effects.py client/table.hf client/table.pf \\
      server/src/DRPGServer/Resources/Item/EquipmentEffects.xml --pack tools
"""
import argparse
import importlib.util
import os
import re
import struct
import zlib
from xml.sax.saxutils import quoteattr

EQUIP_TYPES = {1, 2, 8, 9, 15, 16, 17, 18, 19, 20, 21, 23, 24, 25, 27, 28, 37, 38, 40, 41, 42, 43, 44, 45}

RE_STAT = re.compile(r'(All stats|Max HP|Max VP|Battle Rating|STR|AGI|CON|INT|HP|VP|ATK|DEF|BR)\s*\+?\s*(\d+)\s*(%?)', re.I)
RE_KR_ATK = re.compile(r'공격력\s*(\d+)\s*증가')
RE_KR_DEF = re.compile(r'방어력\s*(\d+)\s*증가')
RE_CARD_SLASH = re.compile(r'^Card Slash\s+(\d+)\s+times?$', re.I)
RE_EXTRA_CARD_SLASH = re.compile(r'^Extra Card Slash\s+(\d+)\s+times?$', re.I)
RE_SUMMON_MORE = re.compile(r'^Summon\s+(\d+)\s+more Digimon$', re.I)
RE_DIGIMON_EXP = re.compile(r'^Digimon EXP\s*\+?\s*(\d+)\s*%$', re.I)
RE_TAMER_EXP = re.compile(r'^Tamer EXP\s*\+?\s*(\d+)$', re.I)
RE_RECOVERY = re.compile(r'^(HP|VP|EVP)\s+Recovery Amount\s+(\d+)$', re.I)
RE_SKILL_DMG = re.compile(r'^(Single|Multi|Melee|Ranged)\s+Skill Damage\s*\+?\s*(\d+)\s*%$', re.I)
RE_GEN_DMG = re.compile(r'^Damag?e\s*\+?\s*(\d+)\s*%$', re.I)
RE_ON_HIT = re.compile(r'^Recover\s+(\d+)\s+(HP|VP),?\s*when attacking\.?$', re.I)
RE_MOVE_SPEED = re.compile(r'^Movement\s+speed\s*\+?\s*(\d+)\s*%$', re.I)


def parse_item(item_id, desc, extra_desc):
    b = {
        'Str': 0, 'Agi': 0, 'Con': 0, 'Int': 0,
        'Hp': 0, 'Vp': 0, 'Atk': 0, 'Def': 0, 'Br': 0,
        'AtkPercent': 0, 'DefPercent': 0, 'BrPercent': 0,
        'CardSlashLimit': 0, 'ExtraCardSlashes': 0,
        'DigimonExpPercent': 0, 'TamerExp': 0,
        'HpRecovery': 0, 'VpRecovery': 0, 'EvpRecovery': 0,
        'MaintainEvolution': item_id == 555,
        'AutoSummonPartners': 0,
        'DamagePercent': 0,
        'SingleSkillDamagePercent': 0, 'MultiSkillDamagePercent': 0,
        'MeleeSkillDamagePercent': 0, 'RangedSkillDamagePercent': 0,
        'OnHitHealHp': 0, 'OnHitHealVp': 0,
        'MoveSpeedPercent': 0,
    }

    if extra_desc and 'summons the leader and partner digimon' in extra_desc.lower():
        b['AutoSummonPartners'] += 1

    if desc:
        for raw in desc.split('/'):
            part = raw.strip()
            if not part or part == '-':
                continue
            if "maintains the digimon's current evolution stage" in part.lower():
                b['MaintainEvolution'] = True
                continue
            if m := RE_CARD_SLASH.match(part):
                b['CardSlashLimit'] = max(b['CardSlashLimit'], int(m.group(1)))
                continue
            if m := RE_EXTRA_CARD_SLASH.match(part):
                b['ExtraCardSlashes'] += int(m.group(1))
                continue
            if m := RE_SUMMON_MORE.match(part):
                b['AutoSummonPartners'] += int(m.group(1))
                continue
            if m := RE_DIGIMON_EXP.match(part):
                b['DigimonExpPercent'] += int(m.group(1))
                continue
            if m := RE_TAMER_EXP.match(part):
                b['TamerExp'] += int(m.group(1))
                continue
            if m := RE_RECOVERY.match(part):
                k = m.group(1).upper()
                v = int(m.group(2))
                if k == 'HP': b['HpRecovery'] += v
                elif k == 'VP': b['VpRecovery'] += v
                elif k == 'EVP': b['EvpRecovery'] += v
                continue
            if m := RE_SKILL_DMG.match(part):
                k = m.group(1).capitalize()
                b[f'{k}SkillDamagePercent'] += int(m.group(2))
                continue
            if m := RE_GEN_DMG.match(part):
                b['DamagePercent'] += int(m.group(1))
                continue
            if m := RE_ON_HIT.match(part):
                v = int(m.group(1))
                if m.group(2).upper() == 'HP': b['OnHitHealHp'] += v
                else: b['OnHitHealVp'] += v
                continue
            if m := RE_MOVE_SPEED.match(part):
                b['MoveSpeedPercent'] += int(m.group(1))
                continue
            if m := RE_KR_ATK.search(part):
                b['Atk'] += int(m.group(1))
                continue
            if m := RE_KR_DEF.search(part):
                b['Def'] += int(m.group(1))
                continue
            for m in RE_STAT.finditer(part):
                st = m.group(1).strip().upper()
                v = int(m.group(2))
                pct = m.group(3) == '%'
                if st == 'ALL STATS':
                    b['Str'] += v; b['Agi'] += v; b['Con'] += v; b['Int'] += v
                elif st == 'STR': b['Str'] += v
                elif st == 'AGI': b['Agi'] += v
                elif st == 'CON': b['Con'] += v
                elif st == 'INT': b['Int'] += v
                elif st in ('HP', 'MAX HP'): b['Hp'] += v
                elif st in ('VP', 'MAX VP'): b['Vp'] += v
                elif st == 'ATK': b['AtkPercent' if pct else 'Atk'] += v
                elif st == 'DEF': b['DefPercent' if pct else 'Def'] += v
                elif st in ('BR', 'BATTLE RATING'): b['BrPercent' if pct else 'Br'] += v

    return b


# Chave interna do parser → (StatModifierKind, forma). A ordem é a ordem de escrita no XML.
MODIFIERS = [
    ('Str', 'Str', 'Value'), ('Agi', 'Agi', 'Value'), ('Con', 'Con', 'Value'), ('Int', 'Int', 'Value'),
    ('Hp', 'Hp', 'Value'), ('Vp', 'Vp', 'Value'),
    ('Atk', 'Atk', 'Value'), ('Def', 'Def', 'Value'), ('Br', 'Br', 'Value'),
    ('AtkPercent', 'Atk', 'Percent'), ('DefPercent', 'Def', 'Percent'), ('BrPercent', 'Br', 'Percent'),
    ('CardSlashLimit', 'CardSlashLimit', 'Value'), ('ExtraCardSlashes', 'ExtraCardSlashes', 'Value'),
    ('DigimonExpPercent', 'DigimonExp', 'Percent'), ('TamerExp', 'TamerExp', 'Value'),
    ('HpRecovery', 'HpRecovery', 'Value'), ('VpRecovery', 'VpRecovery', 'Value'), ('EvpRecovery', 'EvpRecovery', 'Value'),
    ('MaintainEvolution', 'MaintainEvolution', 'Value'),
    ('AutoSummonPartners', 'AutoSummonPartners', 'Value'),
    ('DamagePercent', 'Damage', 'Percent'),
    ('SingleSkillDamagePercent', 'SingleSkillDamage', 'Percent'),
    ('MultiSkillDamagePercent', 'MultiSkillDamage', 'Percent'),
    ('MeleeSkillDamagePercent', 'MeleeSkillDamage', 'Percent'),
    ('RangedSkillDamagePercent', 'RangedSkillDamage', 'Percent'),
    ('OnHitHealHp', 'OnHitHealHp', 'Value'), ('OnHitHealVp', 'OnHitHealVp', 'Value'),
    ('MoveSpeedPercent', 'MoveSpeed', 'Percent'),
]


def load_pack(pack_dir):
    spec = importlib.util.spec_from_file_location('drpg_pack', os.path.join(pack_dir, 'drpg_pack.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_item_rows(drpg_pack, hf_path, pf_path):
    _, entries = drpg_pack.read_hf(hf_path)
    with open(pf_path, 'rb') as pf:
        for name, off, ln in entries:
            if name.lower() != 'table\\item_info_us.dat':
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
    raise SystemExit('table\\item_info_us.dat não encontrado no pacote')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('hf', help='table.hf do cliente v322')
    ap.add_argument('pf', help='table.pf do cliente v322')
    ap.add_argument('out', help='EquipmentEffects.xml de saída')
    ap.add_argument('--pack', required=True, help='pasta com drpg_pack.py (work/tools)')
    args = ap.parse_args()

    rows = read_item_rows(load_pack(args.pack), args.hf, args.pf)

    items_out = []
    seen = set()
    for r in rows:
        if len(r) <= 28 or not r[0].isdigit():
            continue
        item_id = int(r[0])
        if item_id == 0 or item_id in seen:
            continue
        tp = int(r[2]) if r[2].isdigit() else 0
        if tp not in EQUIP_TYPES:
            continue
        extra_desc = ' '.join(x.strip() for x in r[4:13] if x.strip())
        bonus = parse_item(item_id, r[28].strip(), extra_desc)
        modifiers = []
        for key, kind, form in MODIFIERS:
            value = int(bonus[key])
            if value:
                modifiers.append((kind, form, value))
        if not modifiers:
            continue
        seen.add(item_id)
        items_out.append((item_id, r[1].strip(), modifiers))

    items_out.sort(key=lambda x: x[0])

    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        '<!-- Gerado por server/tools/extract_equipment_effects.py de table\\item_info_us.dat v322 (coluna 28). -->',
        '<!-- Bônus de equipamento do Tamer: um <Modifier Kind=StatModifierKind Value|Percent=N /> por bônus. -->',
        '<EquipmentEffects>',
    ]
    for item_id, name, modifiers in items_out:
        lines.append(f'  <Equipment ItemID="{item_id}" Name={quoteattr(name)}>')
        for kind, form, value in modifiers:
            lines.append(f'    <Modifier Kind="{kind}" {form}="{value}" />')
        lines.append('  </Equipment>')
    lines.append('</EquipmentEffects>')

    with open(args.out, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')

    print(f'{len(items_out)} equipamentos em {args.out} ({os.path.getsize(args.out):,} bytes)')


if __name__ == '__main__':
    main()
