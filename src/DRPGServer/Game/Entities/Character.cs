namespace DRPGServer.Game.Entities;

/// <summary>Personagem carregado para o mapa.</summary>
public sealed class Character(Digimon leader, Inventory inventory)
{
    public required uint Uid { get; init; }
    public required uint AccountUid { get; init; }
    public required byte Slot { get; init; }
    public required string Name { get; init; }
    public required byte TamerId { get; init; }
    public Serial Serial { get; } = Serial.New();
    public ushort Level { get; set; } = 1;
    public byte MapId { get; set; }
    public short X { get; set; }
    public short Y { get; set; }
    public double Bits { get; private set; }
    public int TotalWins { get; set; }
    public int TotalBattles { get; set; }

    public Digimon Leader { get; } = leader;
    public Inventory Inventory { get; } = inventory;

    public void SetBits(double bits) => Bits = Math.Max(0, bits);
    public void AddBits(double amount) => SetBits(Bits + amount);
}

/// <summary>Resumo para a lista de personagens do channel.</summary>
public sealed record CharacterSummary(
    uint Uid, byte Slot, string Name, byte TamerId, ushort Level, byte MapId, short X, short Y,
    int TotalWins, int TotalBattles, ushort LeaderSpeciesId, ushort LeaderLevel, string LeaderName);
