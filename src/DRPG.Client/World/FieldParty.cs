using DRPG.Client.Data;
using DRPG.Client.Ui;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.World;

/// <summary>
/// The player's tamer and the leader digimon that follows it (✅ <c>0x00abbd50</c>, <c>0x0085e7b0</c>, <c>0x008671c0</c>): their
/// entities, serials on the grid and animations. They keep going across map changes; only their cells change.
/// </summary>
public sealed class FieldParty
{
    private readonly Random _random;

    private FieldParty(Serial tamerSerial, Cell cell, TamerDoll? tamer, Random random)
    {
        _random = random;
        TamerSerial = tamerSerial;
        Tamer = new FieldEntity(cell);
        TamerDoll = tamer;
        if (tamer is not null)
            TamerAnimation = new FieldAnimator(tamer.Body, digimon: false, species: 0, random.Next(8));      // ✅ rand() % 8
    }

    public Serial TamerSerial { get; }
    public FieldEntity Tamer { get; }
    public TamerDoll? TamerDoll { get; }
    public FieldAnimator? TamerAnimation { get; }

    public Serial PartnerSerial { get; private set; }
    public int PartnerSpecies { get; private set; }

    /// <summary>Null when the tamer has no digimon on the map.</summary>
    public FieldEntity? Partner { get; private set; }
    public SpriteAnimator? PartnerSprite { get; private set; }
    public FieldAnimator? PartnerAnimation { get; private set; }

    /// <summary>
    /// ✅ <c>0x00abbd50</c> / <c>0x0085e7b0</c>: the tamer on the character's cell and the leader digimon one cell up and to the left;
    /// a tamer of 5, 6 or 7 has no digimon on the map.
    /// </summary>
    public static FieldParty Of(CharacterDataPacket character, IFieldSprites sprites, Random random)
    {
        var party = new FieldParty(character.Serial, new Cell(character.X, character.Y), sprites.OpenTamer(character.TamerId), random);
        var leader = character.Party[0];
        if (character.TamerId is not (5 or 6 or 7) && !leader.IsEmpty)
        {
            party.PartnerSerial = leader.Serial;
            party.PartnerSpecies = leader.SpeciesId;
            party.Partner = new FieldEntity(PartnerCell(party.Tamer.Cell));
            party.PartnerSprite = sprites.OpenDigimon(leader.SpeciesId);
            if (party.PartnerSprite is not null)
                party.PartnerAnimation = new FieldAnimator(party.PartnerSprite, digimon: true, leader.SpeciesId, random.Next(8));
        }
        return party;
    }

    /// <summary>✅ <c>0x0085e7b0</c> / <c>0x00acb072</c>: the partner stands one cell up and to the left of the tamer.</summary>
    public static Cell PartnerCell(Cell tamer) => new(tamer.X - 1, tamer.Y - 1);

    /// <summary>Puts both on <paramref name="collision"/> at their current cells (a map was entered).</summary>
    public void Occupy(FieldCollision collision)
    {
        collision.Occupy(Tamer.Cell.X, Tamer.Cell.Y, TamerSerial);
        if (Partner is not null)
            collision.Occupy(Partner.Cell.X, Partner.Cell.Y, PartnerSerial);
    }

    /// <summary>✅ <c>0x00acb072</c>: a <c>0xB0</c> on the same map: both are placed again (the partner one cell up and left) and stop.</summary>
    public void PlaceAt(Cell cell, FieldCollision collision)
    {
        collision.Vacate(Tamer.Cell.X, Tamer.Cell.Y);
        if (Partner is not null)
            collision.Vacate(Partner.Cell.X, Partner.Cell.Y);
        Tamer.PlaceAt(cell);
        Partner?.PlaceAt(PartnerCell(cell));
        Occupy(collision);
    }

    /// <summary>
    /// ✅ <c>0x008671c0</c>: one logic step: the tamer walks, the partner follows it, both on the grid of
    /// <paramref name="collision"/> and through <paramref name="steps"/>, and their animations follow.
    /// </summary>
    public void Step(FieldCollision collision, IStepResolver steps)
    {
        var tamerCell = Tamer.Cell;
        Tamer.StepPhysics(steps);
        collision.Move(tamerCell, Tamer.Cell, TamerSerial);
        TamerAnimation?.Follow(Tamer);
        TamerAnimation?.Step();

        if (Partner is null)
            return;
        var partnerCell = Partner.Cell;
        Partner.FollowLeader(Tamer, steps);
        collision.Move(partnerCell, Partner.Cell, PartnerSerial);
        PartnerAnimation?.Follow(Partner);
        PartnerAnimation?.Step(_random.Next(4));                          // ✅ 1 in 4 for the animation 3
    }

    /// <summary>Queues the tamer and the partner on the y-sorted draw queue.</summary>
    public void QueueDraw(List<DrawItem> queue, int cameraX, int cameraY, EntityDrawer drawer)
    {
        if (Partner is not null && PartnerSprite is not null)
            drawer.QueueSprite(queue, PartnerSprite, PartnerSpecies, Partner.WorldX - cameraX, Partner.WorldY - cameraY);
        if (TamerDoll is not null)
            drawer.QueueTamer(queue, TamerDoll, Tamer.WorldX - cameraX, Tamer.WorldY - cameraY);
    }
}
