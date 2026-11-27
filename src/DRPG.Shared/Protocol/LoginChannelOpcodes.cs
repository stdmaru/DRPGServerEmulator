namespace DRPG.Shared.Protocol;

/// <summary>Opcodes of the login server (✅ v322, docs/protocolo/servidores.md).</summary>
public enum LoginOpcode : ushort
{
    Handshake = 0x05,

    /// <summary>Launcher login (docs/protocolo/launcher.md); values from <see cref="Launcher.LauncherOpcode"/>.</summary>
    LauncherHello = (ushort)Launcher.LauncherOpcode.Hello,
    LauncherLogin = (ushort)Launcher.LauncherOpcode.Login,
}

/// <summary>Opcodes of the channel server (✅ v322, docs/protocolo/structs/channel.md).</summary>
public enum ChannelOpcode : ushort
{
    ChannelSelect = 0x03,
    Authenticate = 0x04,
    CharacterCreate = 0x10,
    CharacterDelete = 0x11,
    CharacterList = 0x12,
    UserCheck = 0x14,
    SecondaryPassword = 0x15,
    MapConnectionConfirm = 0x16,
    ClientHardwareInfo = 0x18,     // C2S: endereços MAC do cliente; sem resposta
    KeepAlive = 0xC4,
}
