using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>0xB4: informações de um digimon (enviado na entrada do mapa e por comandos).</summary>
public sealed class SingleDigimonInfoPacket(Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.SingleDigimonInfo;
    protected override void Write(PacketWriter w) => DigimonStatusBlock.Write(w, digimon);
}

/// <summary>0xB2: status atualizado (recuperação automática no mapa).</summary>
public sealed class RefreshDigimonStatusPacket(Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.RefreshDigimonStatus;
    protected override void Write(PacketWriter w) => DigimonStatusBlock.Write(w, digimon);
}

/// <summary>0x71: resposta à distribuição de ponto: bloco de status + eco dos dois bytes finais do pedido.</summary>
public sealed class AddAbilityPointPacket(Digimon digimon, byte requestFlag, byte stat) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.DigimonAddAbilityPoint;

    protected override void Write(PacketWriter w)
    {
        DigimonStatusBlock.Write(w, digimon);
        w.WriteU8(requestFlag);
        w.WriteU8(stat);
        w.WriteZeros(6);
    }
}
