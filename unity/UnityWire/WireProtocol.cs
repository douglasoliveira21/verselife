namespace LifeVerse.Wire;

/// <summary>Constantes do protocolo LifeVerse Wire.</summary>
public static class WireProtocol
{
    /// <summary>
    /// Versão do protocolo de jogo, enviada em <see cref="AuthenticateMessage"/> e conferida pelo servidor (como no protocolo 11 do Godot).
    /// 12 = primeiro protocolo LifeVerse Wire. O cliente Godot (11) e o cliente Unity (12) não se falam; a API emite o ticket com a versão da ilha.
    /// </summary>
    public const int Version = 12;

    /// <summary>Chave de conexão do LiteNetLib. Só muda se o formato de quadro mudar; a versão do jogo é conferida depois, em Authenticate, para o cliente poder mostrar "o servidor exige o protocolo N".</summary>
    public const string ConnectionKey = "LifeVerse.Wire";

    /// <summary>Canais: 0 = confiável (estado discreto), 1 = entradas do cliente, 2 = estado do servidor. Os mesmos três canais do protocolo 11.</summary>
    public const byte ChannelCount = 3;

    /// <summary>Um jogador que não envia Authenticate neste prazo é desconectado.</summary>
    public const double DefaultAuthenticationSeconds = 10;

    /// <summary>Maior quadro aceito, em bytes (o maior conteúdo é o mapa de terreno completo).</summary>
    public const int MaxFrameBytes = 1_200_000;
}

/// <summary>Limites de tamanho dos campos. Os de rede iguais ao protocolo 11 são conferidos por teste contra NetworkProtocol.</summary>
public static class WireLimits
{
    public const int Token = 4096, Id = 64, Code = 64, DisplayName = 128, VehicleName = 128, VehicleType = 32;
    /// <summary>MaxChatCharacters (256) vezes 4 bytes de UTF-8, com folga.</summary>
    public const int Chat = 1024;
    public const int DialogJson = 16_384, OperationJson = 65_536, AppearanceJson = 131_072;
    public const int Snapshot = 32 * 1024;          // = NetworkProtocol.MaxSnapshotBytes
    public const int TerrainImport = 300_000;       // = NetworkProtocol.MaxTerrainImportBytes
    public const int TerrainFull = 1_048_576, TerrainPatch = 262_144;
    public const int Voice = 400;                   // = VoiceSettings.MaxPacketBytes
    public const int AttachmentValues = 16;
}

public enum WireDirection : byte { ClientToServer, ServerToClient }

/// <summary>Como uma mensagem viaja. Sequenced descarta o que chega fora de ordem (é o "não confiável ordenado" do protocolo 11).</summary>
public enum WireDelivery : byte { Reliable, Sequenced, Unreliable }

public readonly record struct WireRoute(byte Channel, WireDelivery Delivery);

/// <summary>Toda mensagem do protocolo. O número que a identifica no quadro fica no <see cref="WireCodec"/>.</summary>
public interface IWireMessage { }
