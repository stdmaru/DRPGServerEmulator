using System.Buffers.Binary;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Login
{
    /// <summary>0x05: resposta ao primeiro pacote do login.</summary>
    public sealed class HandshakePacket(string username) : ServerPacket
    {
        public override ushort Opcode => (ushort)LoginOpcode.Handshake;

        protected override void Write(PacketWriter w)
        {
            w.WriteI32(3);
            w.WriteI32(0x68);
            w.WriteFixedString(username, 20);
        }
    }
}

namespace DRPGServer.Network.Packets.Global
{
    /// <summary>0xE1: 1342 bytes depois do 0x64 (só o último byte é 1). ❓</summary>
    public sealed class UnknownE1Packet : ServerPacket
    {
        public override ushort Opcode => (ushort)GlobalOpcode.UnknownE1;

        protected override void Write(PacketWriter w)
        {
            w.WriteZeros(1341);
            w.WriteU8(1);
        }
    }

    /// <summary>0x65: vazio, depois do 0xE1.</summary>
    public sealed class Unknown65Packet : ServerPacket
    {
        public override ushort Opcode => (ushort)GlobalOpcode.Unknown65;
        protected override void Write(PacketWriter w) { }
    }

    /// <summary>
    /// ✅ 0x7B (1104 bytes = 69 blocos AES-128-ECB): coleção permanente da enciclopédia.
    /// Layout decifrado: <c>u32 digimonUid</c> + <c>u8 owned[1100]</c> (1 = espécie possuída).
    /// Ver <c>docs/mecanicas/enciclopedia.md</c>.
    /// </summary>
    public sealed class EncyclopediaDataPacket(byte[] sessionKey, uint digimonUid, IEnumerable<ushort> ownedSpecies) : ServerPacket
    {
        public const int MaxSpecies = 1100;
        public const int PlaintextLength = 4 + MaxSpecies;

        public override ushort Opcode => (ushort)GlobalOpcode.EncyclopediaData;

        public static byte[] BuildPlaintext(uint digimonUid, IEnumerable<ushort> ownedSpecies)
        {
            var plaintext = new byte[PlaintextLength];
            BinaryPrimitives.WriteUInt32LittleEndian(plaintext, digimonUid);
            foreach (ushort speciesId in ownedSpecies)
            {
                if (speciesId < MaxSpecies)
                    plaintext[4 + speciesId] = 1;
            }
            return plaintext;
        }

        protected override void Write(PacketWriter w)
        {
            var plaintext = BuildPlaintext(digimonUid, ownedSpecies);
            w.WriteBytes(GlobalSessionCipher.EncryptBlocks(sessionKey, plaintext));
        }
    }

    /// <summary>Uma posição do time para o pacote 0x7C da enciclopédia.</summary>
    public readonly record struct EncyclopediaPartyEntry(
        uint Uid, short BaseSpecies, short ChampionSpecies, short UltimateSpecies, short MegaSpecies)
    {
        public static EncyclopediaPartyEntry From(Digimon? digimon)
        {
            if (digimon is null) return default;
            return new EncyclopediaPartyEntry(
                digimon.Uid,
                (short)digimon.BaseForm.Species.Id,
                UnlockedSpecies(digimon, StatFormulas.Champion),
                UnlockedSpecies(digimon, StatFormulas.Ultimate),
                UnlockedSpecies(digimon, StatFormulas.Mega));
        }

        private static short UnlockedSpecies(Digimon digimon, byte stage)
        {
            foreach (var form in digimon.Forms)
            {
                if (form.Species.Stage == stage)
                    return (short)form.Species.Id;
            }
            if (digimon.UnlockedStage >= stage && digimon.LineSpecies(stage) is { } species)
                return (short)species.Id;
            return 0;
        }
    }

    /// <summary>
    /// ✅ 0x7C (64 bytes = 4 blocos AES-128-ECB): time atual e suas formas desbloqueadas.
    /// Layout decifrado (60 bytes + 4 zeros): <c>u32 uid[5]</c>, <c>i16 base[5]</c>, <c>i16 champion[5]</c>,
    /// <c>i16 ultimate[5]</c>, <c>i16 mega[5]</c>. Ver <c>docs/mecanicas/enciclopedia.md</c>.
    /// </summary>
    public sealed class EncyclopediaCollectionPacket(byte[] sessionKey, IReadOnlyList<EncyclopediaPartyEntry> party) : ServerPacket
    {
        public const int PlaintextLength = 64;

        public override ushort Opcode => (ushort)GlobalOpcode.EncyclopediaCollection;

        public static byte[] BuildPlaintext(IReadOnlyList<EncyclopediaPartyEntry> party)
        {
            var plaintext = new byte[PlaintextLength];
            int count = Math.Min(party.Count, Character.PartySize);
            for (int i = 0; i < count; i++)
            {
                var entry = party[i];
                BinaryPrimitives.WriteUInt32LittleEndian(plaintext.AsSpan(i * 4, 4), entry.Uid);
                BinaryPrimitives.WriteInt16LittleEndian(plaintext.AsSpan(20 + i * 2, 2), entry.BaseSpecies);
                BinaryPrimitives.WriteInt16LittleEndian(plaintext.AsSpan(30 + i * 2, 2), entry.ChampionSpecies);
                BinaryPrimitives.WriteInt16LittleEndian(plaintext.AsSpan(40 + i * 2, 2), entry.UltimateSpecies);
                BinaryPrimitives.WriteInt16LittleEndian(plaintext.AsSpan(50 + i * 2, 2), entry.MegaSpecies);
            }
            return plaintext;
        }

        protected override void Write(PacketWriter w)
        {
            var plaintext = BuildPlaintext(party);
            w.WriteBytes(GlobalSessionCipher.EncryptBlocks(sessionKey, plaintext));
        }
    }
}
