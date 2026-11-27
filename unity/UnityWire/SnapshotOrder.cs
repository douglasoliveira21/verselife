namespace LifeVerse.Wire;

/// <summary>
/// A ordem dos snapshots no lado de quem recebe: vale o que for do tick mais novo já visto ou do mesmo (os outros pacotes de um snapshot grande);
/// o que chegar atrasado, de um tick anterior, é descartado. Um para cada tipo de snapshot.
/// </summary>
public struct SnapshotOrder
{
    private uint _newest;
    private bool _any;

    public bool Accept(uint tick)
    {
        if (_any && unchecked((int)(tick - _newest)) < 0) return false;   // a diferença com sinal continua certa quando o contador dá a volta
        _any = true; _newest = tick; return true;
    }
}
