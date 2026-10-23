namespace CodexVtt.Core;

/// <summary>
/// Dove vive il progetto. È l'unico punto da cambiare se il repository si sposta: l'aggiornamento
/// dell'app (ADR-0008) cerca le nuove versioni nelle release di questo indirizzo.
/// </summary>
public static class ProjectInfo
{
    public const string Name = "CodexVTT";

    public const string RepositoryUrl = "https://github.com/CodexCircle/CodexVTT";

    public const string ReleasesUrl = RepositoryUrl + "/releases";
}
