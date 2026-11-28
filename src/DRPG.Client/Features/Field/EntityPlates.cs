using DRPG.Client.Data;
using DRPG.Client.Startup;
using DRPG.Client.Ui;
using DRPG.Client.World;
using DRPG.Engine.Rendering;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Features.Field;

/// <summary>What the hover pass shows for the entity under the mouse: the plate's text, where and how it is drawn.</summary>
/// <param name="X">The plate's left (<c>0x00a11180</c> x).</param>
/// <param name="Y">The plate's middle row (<c>0x00a11180</c> y): the entity's feet minus its height.</param>
/// <param name="Icon">✅ <c>0x00a11180</c>'s 5th argument: the tamer's class or the wild's grade (the icon), 0 for none; not 0 widens
/// the box by 14 px.</param>
public sealed record EntityPlate(string Text, int X, int Y, int Icon, PlateStyle Style);

/// <summary>✅ The modes of <c>0x00a11180</c> the field uses.</summary>
public enum PlateStyle
{
    /// <summary>✅ Mode 0: a tamer with a class: the class icon and the name in green.</summary>
    Tamer = 0,

    /// <summary>✅ Mode 1: the name in the pass's colour (white), no icon (NPCs, the tamer's digimon, a tamer of class 0).</summary>
    Plain = 1,

    /// <summary>✅ Mode 2: a wild digimon: the grade icon and the name in yellow (<c>+0x2830</c>).</summary>
    Wild = 2,
}

/// <summary>✅ The HP bar of the player's leader digimon under the mouse (<c>0x00a13e60</c>, one row in the field).</summary>
public sealed record LeaderBar(int X, int Y, long Hp, long MaxHp);

/// <summary>
/// The labels of the entity under the mouse (✅ the hover part of the draw pass <c>0x00a0d340</c>, docs/cliente/janelas.md#entidade-sob-o-mouse):
/// each queued entity whose serial is <see cref="FieldState.Hovered"/> sets the plate's text and place, and after the whole queue the
/// pass draws the plate (<c>0x00a11180</c>) and then, for the player's own leader digimon, its HP bar (<c>0x00a13e60</c>).
/// </summary>
public sealed class EntityPlates(UiSprites sprites, UiText text, MonsterRegenTable regen)
{
    /// <summary>✅ <c>0x00a159a0</c>: the plate's 7×7 pieces (<c>+0x74</c>), the HP bar (<c>+0x3c</c>), the grade and class icons.</summary>
    public const string PlateSprite = "interface\\draw_balloon.spr", BarSprite = "gui\\dr2_sub_007_014.spr",
        GradeSprite = "interface\\draw_Monster_class.spr", ClassSprite = "interface\\draw_tamer_class.spr";

    /// <summary>✅ <c>0x00a166b0</c>: the pass's text colours (COLORREF): <c>+0x282c</c> white, <c>+0x2830</c> yellow for a wild.</summary>
    public const uint PlainColor = 0xFFFFFF, WildColor = 0xFFFF;

    /// <summary>✅ <c>0x00a11b8a</c>: a tamer's name with its class icon is green.</summary>
    public const uint TamerColor = 0xFF00;

    /// <summary>✅ <c>0x00a0db86</c>: NPCs whose name is never shown (vtable <c>+0x20</c> = 0x2b, 0x49).</summary>
    public static bool HidesName(int npcId) => npcId is 0x2b or 0x49;

    /// <summary>✅ <c>0x00a0dc2a</c>: the NPC whose plate sits 50 px higher.</summary>
    public const int RaisedNpc = 0x93, RaisedNpcLift = 0x32;

    /// <summary>✅ <c>0x0085f0de</c>: every NPC's height. ✅ <c>0x00b5bb50</c> / <c>0x00864650</c>: a tamer's, 140 for tamer 8.</summary>
    public const int NpcHeight = 100, TamerHeight = 100, TallTamerHeight = 0x8c, TallTamer = 8;

    /// <summary>✅ <c>0x00a0da94</c>: a tamer's plate is anchored 13 px left of its feet.</summary>
    public const int TamerShift = 0xd;

    /// <summary>✅ <c>0x00a10be4</c>: plates without the tamer's icon start 7 px further left.</summary>
    public const int PlateShift = 7;

    /// <summary>✅ <c>0x00a102f4</c>: the HP bar sits 10 px below the leader's feet.</summary>
    public const int BarDrop = 10;

    /// <summary>✅ <c>0x00a0f65f</c>: with the <c>[LANGUAGE]</c> option above 1 a digimon's text stays empty.</summary>
    public const int LastNamedLanguage = 1;

    private const int Piece = 7, WideCorner = 14, IconRoom = 14, TextLeft = 8, IconTextLeft = 0x16, IconShift = 5;
    private const int PlateAlpha = 0x10;

    /// <summary>
    /// ✅ <c>0x00a0d340</c>: the plate of <paramref name="state"/>'s hovered entity at screen coordinates, or null when nothing is
    /// hovered or the entity shows none. <paramref name="character"/> is the player (its name, tamer, class and leader).
    /// </summary>
    public EntityPlate? PlateOf(IRenderer renderer, FieldState state, CharacterDataPacket character, int cameraX, int cameraY,
        int language)
    {
        var hovered = state.Hovered;
        if (hovered == default)
            return null;
        var party = state.Party;
        if (hovered == party.TamerSerial)
            return TamerPlate(renderer, character, party.Tamer.WorldX - cameraX, party.Tamer.WorldY - cameraY);
        if (party.Partner is { } partner && hovered == party.PartnerSerial && !character.Party[0].IsEmpty)
        {
            var leader = character.Party[0];
            return DigimonPlate(renderer, leader.SpeciesId, leader.Name, leader.Level, leader.Stage, wild: false, grade: 0,
                partner.WorldX - cameraX, partner.WorldY - cameraY, language);
        }
        if (state.Area.Wilds.Find(hovered) is { } wild)
            return DigimonPlate(renderer, wild.SpeciesId, wild.Name, wild.Level, wild.Stage, wild: true, wild.Grade,
                wild.Entity.WorldX - cameraX, wild.Entity.WorldY - cameraY, language);
        foreach (var npc in state.Area.Npcs.All)
            if (npc.Serial == hovered)
                return NpcPlate(renderer, npc, npc.WorldX - cameraX, npc.WorldY - cameraY);
        return null;
    }

    /// <summary>
    /// ✅ <c>0x00a10249</c>: the HP bar when the hovered entity is the player's leader digimon on the map, at (x − w/2, y + 10) with
    /// w the bar's width; null otherwise.
    /// </summary>
    public LeaderBar? BarOf(FieldState state, CharacterDataPacket character, int cameraX, int cameraY)
    {
        var party = state.Party;
        var leader = character.Party[0];
        if (party.Partner is not { } partner || leader.IsEmpty || state.Hovered == default || state.Hovered != party.PartnerSerial)
            return null;
        var bar = sprites.Load(BarSprite);
        int width = bar.Length > 0 ? bar[0].Width : 0;
        return new LeaderBar(partner.WorldX - cameraX - width / 2, partner.WorldY - cameraY + BarDrop, leader.Hp, leader.MaxHp);
    }

    /// <summary>✅ <c>0x00a10aae</c>: after the queue, the plate, then the leader's bar.</summary>
    public void Draw(IRenderer renderer, FieldState state, CharacterDataPacket character, int cameraX, int cameraY, SpriteOptions options,
        int language)
    {
        if (PlateOf(renderer, state, character, cameraX, cameraY, language) is { } plate)
            DrawPlate(renderer, options, plate);
        if (BarOf(state, character, cameraX, cameraY) is { } bar)
            DrawBar(renderer, bar);
    }

    /// <summary>
    /// ✅ <c>0x00a11180</c>: the box of 7×7 pieces of <see cref="PlateSprite"/> mixed at 16/32 (mode −1 of <c>0x00b569f0</c>), n =
    /// (w + 4) / 7 middle pieces for a text w wide (+14 with an icon): the top row at y − 7 (frames 1, 0 × n, 2), two rows of frame 0 at
    /// y and y + 7 (n + 2 pieces), the bottom row at y + 14 (frames 3, 0 × n, then 4 cut to 14 × 7). Then the icon at (x + 8 − 5, y − 1)
    /// for a wild (grade, <c>0x00a145d0</c>) or (x + 8, y − 1) for a tamer (class − 1, <c>0x00a14910</c>) and the text without shadow
    /// (<c>0x00b55760</c>) at (x + 22, y + 1) next to an icon, (x + 8, y + 1) without. ❓ A tamer's guild and title lines above the
    /// box (<c>+0x268</c>, <c>+0x250</c>) are not drawn: the client does not read them yet.
    /// </summary>
    public void DrawPlate(IRenderer renderer, SpriteOptions options, EntityPlate plate)
    {
        int width = text.Measure(renderer, plate.Text).Width;
        if (plate.Icon != 0)
            width += IconRoom;
        int pieces = (width + 4) / Piece;
        var box = sprites.Load(PlateSprite);
        int x = plate.X, y = plate.Y;

        int top = y - Piece;
        DrawPiece(renderer, options, box, 1, x, top, Piece);
        for (int i = 1; i <= pieces; i++)
            DrawPiece(renderer, options, box, 0, x + i * Piece, top, Piece);
        DrawPiece(renderer, options, box, 2, x + (pieces + 1) * Piece, top, Piece);
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < pieces + 2; i++)
                DrawPiece(renderer, options, box, 0, x + i * Piece, y + row * Piece, Piece);
        int bottom = y + 2 * Piece;
        DrawPiece(renderer, options, box, 3, x, bottom, Piece);
        for (int i = 1; i <= pieces; i++)
            DrawPiece(renderer, options, box, 0, x + i * Piece, bottom, Piece);
        DrawPiece(renderer, options, box, 4, x + (pieces + 1) * Piece, bottom, WideCorner);

        int textX = x + TextLeft;
        uint color = PlainColor;
        switch (plate.Style)
        {
            case PlateStyle.Tamer:
                if (plate.Icon != 0)
                    Frame(renderer, sprites.Load(ClassSprite), plate.Icon - 1, x + TextLeft, y - 1);
                textX = x + IconTextLeft;
                color = TamerColor;
                break;
            case PlateStyle.Wild:
                Frame(renderer, sprites.Load(GradeSprite), plate.Icon, x + TextLeft - IconShift, y - 1);
                textX = x + IconTextLeft;
                color = WildColor;
                break;
        }
        text.Draw(renderer, plate.Text, textX, y + 1, color, shadow: false);
    }

    /// <summary>
    /// ✅ <c>0x00a13e60</c> with one row: frame 0 of <see cref="BarSprite"/> at (x, y), then frame 1 at (x, y + 1) cut to
    /// <c>(int)((w − 1) · pct / 100 + 1)</c> with <c>pct</c> = <c>0x00b31c00</c>(HP, max).
    /// </summary>
    public void DrawBar(IRenderer renderer, LeaderBar bar)
    {
        var frames = sprites.Load(BarSprite);
        if (frames.Length < 2)
            return;
        renderer.Draw(frames[0], bar.X, bar.Y);
        renderer.DrawPart(frames[1], bar.X, bar.Y + 1, 0, Math.Clamp(FillWidth(frames[0].Width, bar.Hp, bar.MaxHp), 0, frames[1].Width));
    }

    /// <summary>✅ <c>0x00a13ebe</c>: <c>(int)((float)(w − 1) · pct / 100 + 1)</c>.</summary>
    public static int FillWidth(int width, long current, long maximum) =>
        (int)((width - 1) * Ui.FieldHud.ExpPercent(current, maximum) / 100f + 1f);

    /// <summary>
    /// ✅ <c>0x00a0d9ef</c> (queue type 3): <c>"[%s]"</c> of the name at (x − 13 − w/2, y − height), with the class icon and green
    /// when the class (<c>+0x1c9</c>, the low byte of the <c>0x02</c>'s) is not 0 (<c>0x00a10b45</c>), plain otherwise.
    /// </summary>
    private EntityPlate TamerPlate(IRenderer renderer, CharacterDataPacket character, int x, int y)
    {
        var label = $"[{character.Name}]";
        int width = text.Measure(renderer, label).Width;
        int height = character.TamerId == TallTamer ? TallTamerHeight : TamerHeight;
        int tamerClass = (byte)character.TamerClass;
        int anchor = x - TamerShift;
        return tamerClass != 0
            ? new EntityPlate(label, anchor - width / 2, y - height, tamerClass, PlateStyle.Tamer)
            : new EntityPlate(label, anchor - width / 2 - PlateShift, y - height, 0, PlateStyle.Plain);
    }

    /// <summary>
    /// ✅ <c>0x00a0f365</c> (queue types 2 and 5): <c>"LV%d %s"</c> of the level and the name, the full one of <c>MonsterRegen</c>
    /// (<c>0x00a0f669</c>), empty with the language above 1; at (x − w/2 − 7, y − height). A wild (type 5) shows its grade and is
    /// yellow; the tamer's digimon (type 2) is plain. ❓ The owner text <c>'%s'의 %s</c> is built and overwritten at once, and the
    /// title effects (<c>+0x940</c>, <c>0x00a09580</c>) wait for the titles.
    /// </summary>
    private EntityPlate? DigimonPlate(IRenderer renderer, int species, string name, int level, int stage, bool wild, byte grade,
        int x, int y, int language)
    {
        if (language > LastNamedLanguage)
            return null;
        var label = $"LV{level} {regen.FullName(species, name)}";
        int width = text.Measure(renderer, label).Width;
        int top = y - SpeciesTables.NameHeight(species, stage);
        return wild
            ? new EntityPlate(label, x - width / 2 - PlateShift, top, grade, PlateStyle.Wild)
            : new EntityPlate(label, x - width / 2 - PlateShift, top, 0, PlateStyle.Plain);
    }

    /// <summary>✅ <c>0x00a0db55</c> (queue type 6): <c>"[%s]"</c> of the NPC's name at (x − w/2 − 7, y − 100), 50 px higher for 0x93.</summary>
    private EntityPlate? NpcPlate(IRenderer renderer, NpcEntity npc, int x, int y)
    {
        if (HidesName(npc.NpcId))
            return null;
        var label = $"[{npc.Name}]";
        int width = text.Measure(renderer, label).Width;
        int top = y - NpcHeight - (npc.NpcId == RaisedNpc ? RaisedNpcLift : 0);
        return new EntityPlate(label, x - width / 2 - PlateShift, top, 0, PlateStyle.Plain);
    }

    private static void DrawPiece(IRenderer renderer, SpriteOptions options, ITexture[] frames, int frame, int x, int y, int width)
    {
        if (frame >= frames.Length)
            return;
        var texture = frames[frame];
        options.DrawRegionBlended(renderer, texture, x, y, Math.Min(width, texture.Width), Math.Min(Piece, texture.Height), PlateAlpha);
    }

    private static void Frame(IRenderer renderer, ITexture[] frames, int index, int x, int y)
    {
        if (index >= 0 && index < frames.Length)
            renderer.Draw(frames[index], x, y);
    }
}
