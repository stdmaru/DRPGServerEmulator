using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>✅ Ações do pacote Map 0x89 (offset 2), confirmadas em DigimonRPG.bin @ 0x00AC2498.</summary>
public enum DepositoryAction : byte
{
    Open = 1,
    Deposit = 2,
    Withdraw = 3,
    Swap = 4,
}

/// <summary>✅ Resultado da expansão de slot (Map 0x94), confirmado em DigimonRPG.bin @ 0x00AC2883.</summary>
public enum DepositoryExpandResult : byte
{
    Ok = 0,
    MissingItem = 1,
    MaxSlots = 2,
    Error = 3,
}

/// <summary>
/// ✅ Map 0x89 (736 bytes = 40 + 696): resposta de abrir, depositar, retirar ou trocar no Digimon Depository.
/// Ver <c>docs/mecanicas/depository.md</c>.
/// </summary>
public sealed class DepositoryActionPacket(
    ushort npcId,
    DepositoryAction action,
    uint partyDigimonUid,
    uint depositoryDigimonUid,
    double newBits,
    DepositoryResult result,
    Digimon? digimon = null) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.DepositoryAction;

    protected override void Write(PacketWriter w)
    {
        w.WriteU16(npcId);
        w.WriteU8((byte)action);
        w.WriteU8(0);
        w.WriteU32(partyDigimonUid);
        w.WriteU32(depositoryDigimonUid);
        w.WriteZeros(12);
        w.WriteF64(newBits);
        w.WriteU8((byte)result);
        w.WriteZeros(7);
        if (digimon is not null)
            DigimonStatusBlock.Write(w, digimon);
        else
            w.WriteZeros(DigimonStatusBlock.Length);
    }
}

/// <summary>
/// ✅ Map 0x8A (808 bytes = 8 + 10 × 80): página atual, total de páginas, slots disponíveis na página e até 10 Digimons.
/// Confirmado em <c>DigimonRPG.bin @ 0x00AC27F0</c> (<c>0x00B7C530</c> / <c>0x00B7BF50</c> / <c>0x00B7B858</c>).
/// </summary>
public sealed class DepositoryListPacket(ushort page, Character character) : ServerPacket
{
    public const int EntryLength = 80;

    public override ushort Opcode => (ushort)MapOpcode.DepositoryList;

    protected override void Write(PacketWriter w)
    {
        ushort currentPage = Math.Clamp(page, (ushort)1, character.DepositoryTotalPages);
        ushort totalPages = character.DepositoryTotalPages;
        byte slotsOnPage = character.DepositorySlotsOnPage(currentPage);

        w.WriteU16(currentPage);
        w.WriteU16(totalPages);
        w.WriteU8(slotsOnPage);
        w.WriteZeros(3);

        var pageDigimons = character.DepositoryPage(currentPage);
        for (int i = 0; i < Character.DepositoryPageSize; i++)
        {
            if (i < pageDigimons.Count)
            {
                var digimon = pageDigimons[i];
                var baseSpecies = digimon.BaseForm.Species;
                w.WriteU32(digimon.Uid);
                w.WriteU32(baseSpecies.Id);
                w.WriteFixedString(digimon.Name, 21);
                w.WriteU8(0);
                w.WriteU16(digimon.Level);
                w.WriteU8(baseSpecies.Stage);
                w.WriteU8((byte)baseSpecies.Attribute);
                w.WriteU16(0);
                w.WriteI32(digimon.Hp);          // ✅ > 0 desenha o retrato colorido (0x00B7C14A); <= 0 desenha silhueta
                w.WriteZeros(40);
            }
            else
            {
                w.WriteZeros(EntryLength);
            }
        }
    }
}

/// <summary>✅ Map 0x94 (1 byte): resultado da tentativa de expandir slot do Digimon Depository.</summary>
public sealed class DepositoryExpandPacket(DepositoryExpandResult result) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.DepositoryExpand;
    protected override void Write(PacketWriter w) => w.WriteU8((byte)result);
}
