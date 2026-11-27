using DRPG.Shared.Protocol;

namespace DRPG.Shared.Packets.Map;

/// <summary>✅ One skill of a status block: id, required level (column 1 of <c>digimon_skill_us</c>) and learned level (0 = not yet).</summary>
public readonly record struct DigimonSkill(uint Id, ushort RequiredLevel, short Level);

/// <summary>
/// ✅ 696-byte digimon status block, common to 0x02, 0x63, 0x70, 0x71, 0xB2 and 0xB4 (v322;
/// docs/protocolo/structs/status-digimon.md). Offsets in the comments are relative to the block. Everything the layout
/// does not name is zero in every capture and written as zero. An empty position is all zeros (<see cref="Empty"/>).
/// </summary>
public sealed record DigimonStatus(
    uint Uid, Serial Serial, ushort SpeciesId, byte UnlockedStage, string Name, ushort Level,
    int Str, int Agi, int Con, int Int,
    byte Stage, byte Attribute, ushort ElementMask,
    int Hp, int Vp, int Evp, int Atk, int Def, int Br,
    uint Unknown216, long Exp, int MaxActionGauge, int MaxHp, int MaxVp, int MaxEvp, uint Unknown252,
    long ExpToNextLevel, int TotalWins, int TotalBattles, ushort AbilityPoints, ushort SkillPoints,
    DigimonSkill Skill1, DigimonSkill Skill2, uint GaugePerStep, byte Owner, byte Unknown312,
    uint TitleId, uint TitleActive, int TitleStr, int TitleAgi, int TitleCon, int TitleInt)
{
    public const int Length = 696;
    public const int NameBytes = 21;

    public static readonly DigimonStatus Empty = new(
        0, Serial.Empty, 0, 0, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        default, default, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public bool IsEmpty => Equals(Empty);

    public static DigimonStatus Read(PacketReader r)
    {
        int start = r.Position;
        var uid = r.ReadU32();                  // 0
        var serial = Serial.Read(r);            // 4
        var species = r.ReadU16();              // 20
        r.ReadU16();                            // 22 always 0
        var unlockedStage = r.ReadU8();         // 24
        var name = r.ReadFixedString(NameBytes);// 25
        var level = r.ReadU16();                // 46
        int str = r.ReadI32(), agi = r.ReadI32(), con = r.ReadI32(), intel = r.ReadI32();   // 48
        r.Skip(96);                             // 64..159 zeros
        var stage = r.ReadU8();                 // 160
        var attribute = r.ReadU8();             // 161
        var mask = r.ReadU16();                 // 162
        r.Skip(28);                             // 164..191
        int hp = r.ReadI32(), vp = r.ReadI32(), evp = r.ReadI32();                           // 192
        int atk = r.ReadI32(), def = r.ReadI32(), br = r.ReadI32();                          // 204
        var unknown216 = r.ReadU32();           // 216 ❓ 0x80000000
        r.ReadU32();                            // 220
        var exp = r.ReadI64();                  // 224
        var gauge = r.ReadI32();                // 232
        int maxHp = r.ReadI32(), maxVp = r.ReadI32(), maxEvp = r.ReadI32();                 // 236
        r.ReadU32();                            // 248
        var unknown252 = r.ReadU32();           // 252 ❓ 100000
        var toNext = r.ReadI64();               // 256
        int wins = r.ReadI32(), battles = r.ReadI32();                                       // 264
        var abilityPoints = r.ReadU16();        // 272
        var skillPoints = r.ReadU16();          // 274
        var skill1 = new DigimonSkill(r.ReadU32(), r.ReadU16(), r.ReadI16());                // 276
        var skill2 = new DigimonSkill(r.ReadU32(), r.ReadU16(), r.ReadI16());                // 284
        r.ReadU32();                            // 292
        var gaugePerStep = r.ReadU32();         // 296
        var owner = r.ReadU8();                 // 300
        r.Skip(11);                             // 301..311
        var unknown312 = r.ReadU8();            // 312
        r.Skip(7);                              // 313..319
        var titleId = r.ReadU32();              // 320
        var titleActive = r.ReadU32();          // 324
        int tStr = r.ReadI32(), tAgi = r.ReadI32(), tCon = r.ReadI32(), tInt = r.ReadI32(); // 328
        r.Skip(Length - (r.Position - start));  // 344.. zeros, plus the 8 new v322 bytes
        return new(uid, serial, species, unlockedStage, name, level, str, agi, con, intel, stage, attribute, mask,
            hp, vp, evp, atk, def, br, unknown216, exp, gauge, maxHp, maxVp, maxEvp, unknown252, toNext, wins, battles,
            abilityPoints, skillPoints, skill1, skill2, gaugePerStep, owner, unknown312, titleId, titleActive,
            tStr, tAgi, tCon, tInt);
    }

    public void Write(PacketWriter w)
    {
        int start = w.Position;
        w.WriteU32(Uid);                        // 0
        Serial.Write(w);                        // 4
        w.WriteU16(SpeciesId);                  // 20
        w.WriteU16(0);                          // 22
        w.WriteU8(UnlockedStage);               // 24
        w.WriteFixedString(Name, NameBytes);    // 25
        w.WriteU16(Level);                      // 46
        w.WriteI32(Str);                        // 48
        w.WriteI32(Agi);
        w.WriteI32(Con);
        w.WriteI32(Int);
        w.WriteZeros(96);                       // 64..159
        w.WriteU8(Stage);                       // 160
        w.WriteU8(Attribute);                   // 161
        w.WriteU16(ElementMask);                // 162
        w.WriteZeros(28);                       // 164..191
        w.WriteI32(Hp);                         // 192
        w.WriteI32(Vp);
        w.WriteI32(Evp);
        w.WriteI32(Atk);                        // 204
        w.WriteI32(Def);
        w.WriteI32(Br);
        w.WriteU32(Unknown216);                 // 216
        w.WriteU32(0);                          // 220
        w.WriteI64(Exp);                        // 224
        w.WriteI32(MaxActionGauge);             // 232
        w.WriteI32(MaxHp);                      // 236
        w.WriteI32(MaxVp);
        w.WriteI32(MaxEvp);
        w.WriteU32(0);                          // 248
        w.WriteU32(Unknown252);                 // 252
        w.WriteI64(ExpToNextLevel);             // 256
        w.WriteI32(TotalWins);                  // 264
        w.WriteI32(TotalBattles);               // 268
        w.WriteU16(AbilityPoints);              // 272
        w.WriteU16(SkillPoints);                // 274
        WriteSkill(w, Skill1);                  // 276
        WriteSkill(w, Skill2);                  // 284
        w.WriteU32(0);                          // 292
        w.WriteU32(GaugePerStep);               // 296
        w.WriteU8(Owner);                       // 300 ✅ 0 player, 2 Beginner wild, 3 Veteran wild
        w.WriteZeros(11);                       // 301..311
        w.WriteU8(Unknown312);                  // 312
        w.WriteZeros(7);                        // 313..319
        w.WriteU32(TitleId);                    // 320
        w.WriteU32(TitleActive);                // 324
        w.WriteI32(TitleStr);                   // 328
        w.WriteI32(TitleAgi);
        w.WriteI32(TitleCon);
        w.WriteI32(TitleInt);
        w.WriteZeros(Length - (w.Position - start));
    }

    private static void WriteSkill(PacketWriter w, DigimonSkill skill)
    {
        w.WriteU32(skill.Id);
        w.WriteU16(skill.RequiredLevel);
        w.WriteI16(skill.Level);
    }
}
