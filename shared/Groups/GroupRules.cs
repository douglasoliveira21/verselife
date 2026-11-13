using System.Text.RegularExpressions;

namespace LifeVerse.Shared.Groups;

/// <summary>Everything a role of a group can allow. A person may do something in a group only when their role has the permission (the leader has them all).</summary>
public static class GroupPermissions
{
    public const string Invite = "invite", ApproveRequests = "approve_requests", Kick = "kick", Ban = "ban", Mute = "mute",
        EditProfile = "edit_profile", ManageRoles = "manage_roles", AssignRoles = "assign_roles", ViewAudit = "view_audit",
        Announce = "announce", ManageEvents = "manage_events", ModerateChat = "moderate_chat",
        ManageLand = "manage_land", BuildOnLand = "build_on_land", ReleaseLand = "release_land",
        ViewTreasury = "view_treasury", ManageTreasury = "manage_treasury", ManageStore = "manage_store", ManageProposals = "manage_proposals";

    /// <summary>Every permission with the name shown to people, in the order the role editor lists them.</summary>
    public static readonly IReadOnlyList<(string Key, string Label, string Help)> All =
    [
        (Invite, "Convidar pessoas", "Mandar convites para o grupo."),
        (ApproveRequests, "Aprovar pedidos de entrada", "Aceitar ou recusar quem pediu para entrar."),
        (Kick, "Expulsar membros", "Tirar alguém do grupo (a pessoa pode voltar)."),
        (Ban, "Banir membros", "Tirar alguém e impedir a volta, por um tempo ou para sempre."),
        (Mute, "Silenciar membros", "Impedir alguém de falar no grupo por um tempo."),
        (EditProfile, "Editar o perfil do grupo", "Mudar nome, descrição, imagem, cores e como se entra."),
        (ManageRoles, "Gerenciar cargos", "Criar, mudar e apagar cargos abaixo do seu."),
        (AssignRoles, "Dar cargos aos membros", "Mudar o cargo de membros abaixo do seu."),
        (ViewAudit, "Ver o histórico", "Ler o que foi feito na administração do grupo."),
        (Announce, "Publicar anúncios", "Escrever, fixar e apagar anúncios do grupo."),
        (ManageEvents, "Gerenciar eventos", "Criar e cancelar eventos exclusivos do grupo."),
        (ModerateChat, "Moderar o chat", "Apagar mensagens de membros com cargo abaixo do seu."),
        (ManageLand, "Administrar os terrenos", "Colocar terrenos no grupo e administrá-los: acesso, opções, divisão, objetos, ambiente."),
        (BuildOnLand, "Construir nos terrenos", "Construir nos terrenos do grupo, mesmo quando só o dono pode."),
        (ReleaseLand, "Devolver terrenos", "Tirar um terreno do grupo e passá-lo a um membro."),
        (ViewTreasury, "Ver o cofre", "Ver o saldo do cofre do grupo e todo o seu extrato."),
        (ManageTreasury, "Retirar do cofre", "Passar moedas do cofre para um membro, com motivo, dentro dos limites diários."),
        (ManageStore, "Gerenciar lojas e produtos", "Publicar, mudar e retirar produtos do grupo no mercado e montar lojas do grupo."),
        (ManageProposals, "Gerenciar propostas", "Criar propostas de votação do grupo e encerrá-las antes do prazo.")
    ];

    public static readonly IReadOnlySet<string> Known = All.Select(p => p.Key).ToHashSet();
}

/// <summary>A role as the rules need it: how high it ranks and what it allows.</summary>
public sealed record RoleView(Guid Id, string Name, int Rank, IReadOnlySet<string> Permissions, bool IsLeader = false, bool IsDefault = false);

/// <summary>
/// The rules that stop a group from being abused, shared by the server (which enforces them) and the client (which hides what would be refused).
/// The leader outranks everybody and has every permission. For everybody else: a person acts only on people who rank strictly below them, may
/// only create or give roles below their own, and may never hand out a permission they do not have themselves, so nobody can climb by
/// editing roles.
/// </summary>
public static class GroupRules
{
    public const int LeaderRank = 1000, MemberRank = 0, MaxCustomRank = 99, ModeratorRank = 40, AdminRank = 80;
    public const int MaxRoles = 20, MaxPendingInvites = 100, MaxPendingRequestsPerUser = 10, MaxGroupsCreated = 5, MaxMemberships = 30, DefaultMemberLimit = 500, MaxMemberLimit = 5_000;
    public const long MaxCoinsPerOperation = 1_000_000_000;
    public const int ContributionsPerHour = 20, MaxWithdrawalsPerDay = 10, WithdrawalDailyShareBps = 2_000, MaxGroupProducts = 100, MaxGroupStores = 50;
    public const int MaxProposalTitle = 120, MaxProposalBody = 4000, MinProposalOptions = 2, MaxProposalOptions = 8, MaxOptionLength = 80, MaxOpenProposals = 10, MaxProposalDays = 30, MaxRoleTitle = 24;
    public const int MaxGroupParcels = 50, MaxLandReleasesPerDay = 3, MaxGroupAccessPerParcel = 20;
    public const int MaxChatLength = 1000, ChatBurst = 20, MaxAnnouncementTitle = 80, MaxAnnouncementBody = 2000, MaxPinned = 3, MaxAnnouncementsPerHour = 10, MaxUpcomingEvents = 20, MaxEventCapacity = 5_000;
    public static readonly TimeSpan ChatBurstWindow = TimeSpan.FromSeconds(30);
    public const int ModerationBurstLimit = 30; // moderation actions one person may take in ten minutes
    public static readonly TimeSpan ModerationBurstWindow = TimeSpan.FromMinutes(10);

    public const string Open = "open", Request = "request", InviteOnly = "invite";
    public static readonly string[] JoinPolicies = [Open, Request, InviteOnly];
    public static readonly string[] Privacies = ["public", "private"];
    public static readonly string[] Categories = ["social", "construcao", "comercio", "jogos", "educacao", "musica", "arte", "eventos", "outros"];
    public static readonly string[] Ratings = ["livre", "moderado", "adulto"];
    public const long MaxJoinFee = 1_000_000;
    public static bool ValidLanguage(string? language) => language is { Length: >= 2 and <= 12 } && Regex.IsMatch(language, "^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$");
    public static readonly string[] Emblems = ["people", "star", "hammer", "bag", "coin", "map", "camera", "chat", "bulb", "wing", "cube", "run"];

    private static readonly Regex Colour = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private static readonly Regex Tag = new("^[A-Za-z0-9]{2,5}$", RegexOptions.Compiled);

    public static bool ValidName(string? name, out string clean)
    {
        clean = string.Join(' ', (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length is >= 3 and <= 40 && !clean.Any(char.IsControl);
    }

    public static bool ValidRoleName(string? name, out string clean)
    {
        clean = string.Join(' ', (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length is >= 2 and <= 32 && !clean.Any(char.IsControl);
    }

    public static bool ValidTag(string? tag) => string.IsNullOrEmpty(tag) || Tag.IsMatch(tag);
    public static bool ValidColour(string? colour) => string.IsNullOrEmpty(colour) || Colour.IsMatch(colour);
    public static bool ValidImageUrl(string? url) => string.IsNullOrEmpty(url) || url.Length <= 300 && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0;

    /// <summary>Whether somebody of this role may act (remove, ban, mute, change the role of) somebody of that rank: only on people strictly below.</summary>
    public static bool CanActOn(RoleView actor, int targetRank) => actor.IsLeader || actor.Rank > targetRank;

    public static bool Has(RoleView role, string permission) => role.IsLeader || role.Permissions.Contains(permission);

    /// <summary>Whether the actor may hand out exactly these permissions: only ones they hold themselves.</summary>
    public static bool CanGrant(RoleView actor, IEnumerable<string> permissions) => actor.IsLeader || permissions.All(actor.Permissions.Contains);

    /// <summary>Whether the actor may create, change or delete a role of this rank: below their own (the leader may touch every custom rank).</summary>
    public static bool CanManageRank(RoleView actor, int rank) => actor.IsLeader ? rank <= MaxCustomRank : rank < actor.Rank;

    /// <summary>The permissions as clean data: known names only, no repeats, sorted.</summary>
    public static IReadOnlySet<string>? CleanPermissions(IEnumerable<string>? permissions)
    {
        if (permissions is null) return new HashSet<string>();
        var list = permissions.Select(p => p?.Trim() ?? string.Empty).ToList();
        return list.Any(p => !GroupPermissions.Known.Contains(p)) ? null : new SortedSet<string>(list, StringComparer.Ordinal);
    }

    public static string JoinPermissions(IEnumerable<string> permissions) => string.Join(',', permissions.OrderBy(p => p, StringComparer.Ordinal));
    public static IReadOnlySet<string> SplitPermissions(string? text) => (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(GroupPermissions.Known.Contains).ToHashSet();

    /// <summary>The roles every new group starts with (leader, administrator, moderator, member), each with the permissions that suit it.</summary>
    public static readonly IReadOnlyList<(string Key, string Name, int Rank, string[] Permissions)> StartingRoles =
    [
        ("owner", "Líder", LeaderRank, [.. GroupPermissions.Known]),
        ("admin", "Administrador", AdminRank, [GroupPermissions.Invite, GroupPermissions.ApproveRequests, GroupPermissions.Kick, GroupPermissions.Ban, GroupPermissions.Mute, GroupPermissions.EditProfile, GroupPermissions.ManageRoles, GroupPermissions.AssignRoles, GroupPermissions.ViewAudit, GroupPermissions.Announce, GroupPermissions.ManageEvents, GroupPermissions.ModerateChat, GroupPermissions.ManageLand, GroupPermissions.BuildOnLand, GroupPermissions.ViewTreasury, GroupPermissions.ManageStore, GroupPermissions.ManageProposals]),
        ("moderator", "Moderador", ModeratorRank, [GroupPermissions.Invite, GroupPermissions.ApproveRequests, GroupPermissions.Kick, GroupPermissions.Mute, GroupPermissions.ModerateChat]),
        ("member", "Membro", MemberRank, [])
    ];
}
