namespace LifeVerse.Shared.Building;

public enum TutorialStep { Create, Move, Resize, Rotate, Texture, Duplicate, Link, Save, Done }

/// <summary>
/// The first-use tutorial of the build window: eight small tasks, in order. Each step finishes by itself when the person actually does
/// it (the editor reports what they did); doing something out of order does not count, so the card always shows what to try next.
/// The tutorial can be skipped at any time and started again from the window.
/// </summary>
public sealed class BuildTutorial
{
    public const int Steps = 8;

    public TutorialStep Current { get; private set; } = TutorialStep.Create;
    public bool Skipped { get; private set; }
    public bool Active => !Skipped && Current != TutorialStep.Done;
    public bool Finished => Current == TutorialStep.Done;

    /// <summary>The person did something. Returns true when it was the step being asked for (and the tutorial moved on).</summary>
    public bool Notify(TutorialStep done)
    {
        if (!Active || done != Current) return false;
        Current = (TutorialStep)((int)Current + 1);
        return true;
    }

    public void Skip() => Skipped = true;
    public void Restart() { Skipped = false; Current = TutorialStep.Create; }

    public static string Text(TutorialStep step) => step switch
    {
        TutorialStep.Create => "1/8 · Crie uma caixa: na aba Geral escolha Caixa, clique no botão do cubo e depois clique no chão onde quer colocá-la.",
        TutorialStep.Move => "2/8 · Mova a caixa: use Mover (2) e arraste uma seta, ou digite a posição na aba Objeto.",
        TutorialStep.Resize => "3/8 · Mude o tamanho: use Esticar (4) e arraste um cubo, ou digite o tamanho na aba Objeto.",
        TutorialStep.Rotate => "4/8 · Gire a caixa: use Girar (3) e arraste um anel, ou digite a rotação na aba Objeto.",
        TutorialStep.Texture => "5/8 · Dê cor ou textura: na aba Textura escolha uma cor ou uma textura da biblioteca e aplique.",
        TutorialStep.Duplicate => "6/8 · Duplique a caixa: selecione-a e aperte Ctrl+D.",
        TutorialStep.Link => "7/8 · Vincule duas peças: selecione as duas (Shift soma) e clique em Vincular.",
        TutorialStep.Save => "8/8 · Guarde no inventário: na aba Geral dê um nome e use Salvar cópia.",
        _ => "Tutorial concluído! Agora é com você. Dá para repeti-lo pelo botão Tutorial."
    };
}
