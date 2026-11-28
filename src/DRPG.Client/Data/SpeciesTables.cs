namespace DRPG.Client.Data;

/// <summary>
/// The per-species tables fixed in the v322 code (✅ <c>0x00b23d20</c> and <c>0x00b46120</c>, docs/cliente/tabelas-de-especie.md),
/// transcribed one range per line so each can be checked against the page. A species outside a table uses the default.
/// </summary>
public static class SpeciesTables
{
    private readonly record struct SpriteRange(int Sprite, int First, int Last);

    private readonly record struct OffsetRange(int Dx, int Dy, int First, int Last);

    /// <summary>✅ Offset of the digimon's frame from the slot button when the species has no entry (<c>0x00b46120</c>).</summary>
    public static readonly (int Dx, int Dy) DefaultOffset = (-40, -14);

    /// <summary>
    /// ✅ Species drawn centred on a point relative to the button instead of by an offset: 638, 639, 641–644, 647, 648,
    /// 650 and 653 on (135, 140), 645 on (135, 110). ❓ Relative to the button (see the page).
    /// </summary>
    public static (int X, int Y)? CenterOf(int species) => species switch
    {
        638 or 639 or (>= 641 and <= 644) or 647 or 648 or 650 or 653 => (135, 140),
        645 => (135, 110),
        _ => null,
    };

    /// <summary>
    /// ✅ The id of the portrait <c>Sprite\icon_%03d.spr</c> of a tamer or a species (<c>0x00b1ec40</c>, loaded by <c>0x00b231b0</c>):
    /// 0x141–0x143, 0x3ec and 0x3a5–0x3ad are used as they are, everything else through <see cref="SpriteId"/> (the tamers 1–9 have no
    /// table entry, so they are their own id). Outside 1..1100 there is no portrait (null).
    /// </summary>
    public static int? IconId(int id) => id switch
    {
        < 1 or > 1100 => null,
        0x141 or 0x142 or 0x143 or 0x3ec => id,
        >= 0x3a5 and <= 0x3ad => id,
        _ => SpriteId(id),
    };

    /// <summary>✅ The id in <c>Sprite\%03d_1.ani</c>: the table entry, or the species itself when it has none (<c>0x00b23d20</c>).</summary>
    public static int SpriteId(int species)
    {
        foreach (var range in SpriteRanges)
            if (species >= range.First && species <= range.Last)
                return range.Sprite;
        return species;
    }

    /// <summary>✅ (dx, dy) of the frame's top-left corner from the button of the slot (<c>0x00b46120</c>).</summary>
    public static (int Dx, int Dy) OffsetOf(int species)
    {
        foreach (var range in OffsetRanges)
            if (species >= range.First && species <= range.Last)
                return (range.Dx, range.Dy);
        return DefaultOffset;
    }

    /// <summary>
    /// ✅ <c>0x007e7470</c>: the species a digimon counts as for its height (<see cref="NameHeight"/>); a species not listed is itself.
    /// </summary>
    public static int HeightSpecies(int species) => species switch
    {
        0x25 or 0x2f or 0x32 => 0xf,
        0x30 => 0x13,
        0x31 => 0x21,
        0x54 or 0x27d => 0x33,
        0x58 or 0x298 => 0x40,
        0x59 or 0x299 => 0x4a,
        0x80 or 0x2e3 => 0x5b,
        0xa8 or 0x34c => 0x83,
        0xd5 or 0xd6 => 0xbc,
        0xff => 0xfe,
        0x271 => 0x53,
        0x272 => 0x39,
        0x286 => 0x3e,
        0x2c1 or 0x306 => 0x70,
        0x2c2 or 0x307 => 0x69,
        0x2d6 or 0x2ed => 0x66,
        0x328 or 0x36e => 0x8f,
        0x329 or 0x36f => 0x90,
        0x33a => 0x9b,
        _ => species,
    };

    /// <summary>
    /// ✅ <c>0x007f3bf0</c>, recomputed each frame by the draw pass (<c>0x00a0ece5</c>): a digimon's height <c>+0x18c</c>, how far
    /// above its feet the name plate sits. By <see cref="HeightSpecies"/> first, then by stage (<c>+0x8a0</c>).
    /// </summary>
    public static int NameHeight(int species, int stage) => HeightSpecies(species) switch
    {
        0x35 or 0x38 or 0x3b or 0x3f or 0x41 or 0x4e or 0x63 or 0x292 => 0x41,
        0xa0 => 0x82,
        _ => (byte)stage switch
        {
            1 => 0x37,
            5 => 0x82,
            _ => 0x55,
        },
    };

    private static readonly SpriteRange[] SpriteRanges =
    [
        new(10, 34, 34),
        new(15, 37, 37),
        new(15, 47, 47),
        new(15, 50, 50),
        new(17, 41, 41),
        new(19, 48, 48),
        new(21, 42, 42),
        new(23, 43, 43),
        new(32, 38, 38),
        new(33, 49, 49),
        new(51, 84, 84),
        new(51, 637, 637),
        new(52, 657, 657),
        new(53, 658, 658),
        new(57, 626, 626),
        new(62, 646, 646),
        new(64, 88, 88),
        new(64, 664, 664),
        new(67, 635, 635),
        new(74, 89, 89),
        new(74, 665, 665),
        new(81, 651, 651),
        new(82, 87, 87),
        new(83, 625, 625),
        new(91, 128, 128),
        new(91, 739, 739),
        new(92, 766, 766),
        new(93, 767, 767),
        new(94, 738, 738),
        new(100, 58, 58),
        new(100, 137, 137),
        new(100, 222, 222),
        new(100, 293, 296),
        new(100, 313, 316),
        new(100, 333, 333),
        new(100, 363, 363),
        new(100, 856, 856),
        new(102, 726, 726),
        new(102, 749, 749),
        new(103, 755, 756),
        new(105, 706, 706),
        new(105, 775, 775),
        new(108, 735, 735),
        new(112, 705, 705),
        new(112, 774, 774),
        new(126, 759, 759),
        new(131, 168, 168),
        new(131, 844, 844),
        new(132, 869, 869),
        new(133, 870, 870),
        new(134, 842, 842),
        new(143, 808, 808),
        new(143, 878, 878),
        new(144, 809, 809),
        new(144, 879, 879),
        new(153, 840, 840),
        new(155, 826, 826),
        new(161, 854, 854),
        new(166, 863, 863),
        new(171, 306, 306),
        new(172, 356, 356),
        new(173, 357, 357),
        new(187, 353, 353),
        new(188, 213, 214),
        new(192, 330, 330),
        new(199, 343, 343),
        new(216, 307, 307),
        new(221, 305, 305),
        new(226, 1011, 1011),
        new(230, 304, 304),
        new(239, 355, 355),
        new(248, 308, 308),
        new(250, 344, 344),
        new(254, 255, 255),
        new(254, 1010, 1010),
        new(256, 345, 345),
        new(268, 337, 337),
        new(272, 354, 354),
        new(283, 349, 349),
        new(285, 350, 350),
        new(288, 348, 348),
        new(309, 1015, 1015),
        new(317, 1009, 1009),
        new(319, 1006, 1006),
        new(321, 322, 323),
        new(321, 1004, 1004),
        new(328, 1013, 1013),
        new(329, 1001, 1001),
        new(332, 1003, 1003),
        new(334, 1008, 1008),
        new(335, 1005, 1005),
        new(336, 338, 338),
        new(339, 340, 340),
        new(341, 342, 342),
        new(346, 1019, 1019),
        new(360, 1012, 1012),
        new(362, 1000, 1000),
        new(369, 1016, 1016),
        new(370, 1002, 1002),
        new(372, 1007, 1007),
        new(377, 1018, 1018),
        new(381, 1014, 1014),
        new(388, 1017, 1017),
        new(392, 394, 394),
        new(401, 912, 912),
        new(402, 403, 436),
        new(437, 387, 387),
        new(600, 609, 609),
        new(605, 615, 615),
        new(607, 636, 636),
        new(611, 616, 616),
        new(617, 618, 618),
        new(619, 674, 674),
        new(621, 656, 656),
        new(629, 649, 649),
        new(630, 654, 654),
        new(631, 655, 655),
        new(632, 652, 652),
        new(648, 650, 650),
        new(671, 913, 913),
        new(712, 765, 765),
        new(715, 781, 781),
        new(716, 737, 737),
        new(723, 762, 762),
        new(729, 753, 753),
        new(730, 763, 763),
        new(731, 764, 764),
        new(732, 760, 760),
        new(751, 754, 754),
        new(757, 758, 758),
        new(812, 872, 872),
        new(819, 886, 886),
        new(820, 841, 841),
        new(824, 868, 868),
        new(832, 859, 859),
        new(833, 866, 866),
        new(834, 867, 867),
        new(835, 864, 864),
        new(858, 860, 860),
        new(861, 862, 862),
        new(880, 881, 881),
        new(931, 933, 941),
        new(947, 921, 921),
    ];

    private static readonly OffsetRange[] OffsetRanges =
    [
        new(-84, -54, 26, 26),
        new(-90, -54, 27, 27),
        new(-87, -84, 31, 31),
        new(-87, -84, 44, 45),
        new(-84, -80, 40, 40),
        new(-80, -110, 58, 58),
        new(-80, -110, 604, 604),
        new(-35, -30, 61, 61),
        new(-40, -38, 62, 62),
        new(-40, -38, 646, 646),
        new(-80, -85, 66, 66),
        new(-80, -85, 619, 619),
        new(-80, -85, 621, 623),
        new(-80, -85, 632, 632),
        new(-80, -85, 652, 652),
        new(-80, -85, 656, 656),
        new(-80, -85, 674, 674),
        new(-95, -80, 70, 70),
        new(-95, -80, 788, 788),
        new(-90, -108, 76, 76),
        new(-74, -52, 77, 77),
        new(-90, -90, 81, 81),
        new(-90, -90, 651, 651),
        new(-85, -70, 82, 83),
        new(-85, -70, 87, 87),
        new(-85, -70, 625, 625),
        new(-85, -70, 659, 662),
        new(-85, -70, 912, 913),
        new(-80, -80, 85, 85),
        new(-80, -80, 600, 603),
        new(-80, -80, 605, 609),
        new(-80, -80, 611, 613),
        new(-80, -80, 615, 616),
        new(-80, -80, 624, 624),
        new(-80, -80, 627, 627),
        new(-80, -80, 636, 636),
        new(-90, -80, 86, 86),
        new(-85, -80, 90, 90),
        new(-35, -25, 95, 95),
        new(-80, -70, 614, 614),
        new(-100, -80, 617, 618),
        new(-90, -95, 628, 631),
        new(-90, -95, 649, 649),
        new(-90, -95, 654, 655),
        new(-80, -90, 633, 633),
        new(-85, -90, 634, 634),
        new(-111, -32, 663, 663),
        new(-162, -140, 666, 666),
        new(-192, -145, 667, 667),
        new(-106, -98, 668, 668),
        new(-180, -150, 669, 669),
        new(-120, -120, 670, 670),
        new(-97, -62, 672, 672),
        new(-100, -85, 673, 673),
        new(-140, -90, 675, 675),
        new(-60, -50, 676, 676),
        new(-60, -40, 677, 677),
        new(-100, -70, 678, 678),
    ];
}
