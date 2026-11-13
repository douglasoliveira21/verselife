using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Scripts.Groups;

/// <summary>The calls of the group profile window (all under <c>/api/v1/groups</c>).</summary>
public sealed class GroupProfileApi(IslandManagementApi inner)
{
    private static string G(Guid id) => $"/api/v1/groups/{id}";

    public Task<IslandReply> Get(Guid id) => inner.Send("GET", G(id));
    public Task<IslandReply> Update(Guid id, object body) => inner.Send("PUT", G(id), body);
    public Task<IslandReply> Create(object body) => inner.Send("POST", "/api/v1/groups", body);
    public Task<IslandReply> Search(string query, int page, string category, string language, string policy, bool free) =>
        inner.Send("GET", $"/api/v1/groups/search?q={Uri.EscapeDataString(query)}&page={page}&category={Uri.EscapeDataString(category)}&language={Uri.EscapeDataString(language)}&policy={Uri.EscapeDataString(policy)}&free={(free ? "true" : "false")}");
    public Task<IslandReply> Island(Guid island) => inner.Send("GET", $"/api/v1/worlds/islands/{island}");

    // coming in and out
    public Task<IslandReply> Join(Guid id, string? message, long? confirmFee) => inner.Send("POST", $"{G(id)}/join", new { message = message ?? "", confirmFee });
    public Task<IslandReply> CancelRequest(Guid id) => inner.Send("DELETE", $"{G(id)}/join");
    public Task<IslandReply> Leave(Guid id) => inner.Send("POST", $"{G(id)}/leave", new { });
    public Task<IslandReply> Invite(Guid id, string username, string? message, Guid? roleId) => inner.Send("POST", $"{G(id)}/invites", new { username, message = message ?? "", roleId });
    public Task<IslandReply> Requests(Guid id) => inner.Send("GET", $"{G(id)}/requests");
    public Task<IslandReply> Decide(Guid id, Guid request, bool approve) => inner.Send("POST", $"{G(id)}/requests/{request}/{(approve ? "approve" : "deny")}", new { });

    // people and cargos
    public Task<IslandReply> Members(Guid id) => inner.Send("GET", $"{G(id)}/members");
    public Task<IslandReply> Kick(Guid id, Guid user) => inner.Send("POST", $"{G(id)}/members/{user}/kick", new { reason = "" });
    public Task<IslandReply> Ban(Guid id, Guid user, int? days) => inner.Send("POST", $"{G(id)}/members/{user}/ban", new { reason = "", days });
    public Task<IslandReply> Mute(Guid id, Guid user, int minutes) => inner.Send("POST", $"{G(id)}/members/{user}/mute", new { minutes, reason = "" });
    public Task<IslandReply> SetRole(Guid id, Guid user, Guid role) => inner.Send("PUT", $"{G(id)}/members/{user}/role", new { roleId = role });
    public Task<IslandReply> Roles(Guid id) => inner.Send("GET", $"{G(id)}/roles");
    public Task<IslandReply> CreateRole(Guid id, string name, int rank, IEnumerable<string> permissions) => inner.Send("POST", $"{G(id)}/roles", new { name, rank, permissions });
    public Task<IslandReply> UpdateRole(Guid id, Guid role, object body) => inner.Send("PUT", $"{G(id)}/roles/{role}", body);
    public Task<IslandReply> RoleTitle(Guid id, Guid role, string title) => inner.Send("PUT", $"{G(id)}/roles/{role}/title", new { title });
    public Task<IslandReply> DeleteRole(Guid id, Guid role) => inner.Send("DELETE", $"{G(id)}/roles/{role}");

    // talking
    public Task<IslandReply> Announcements(Guid id) => inner.Send("GET", $"{G(id)}/announcements");
    public Task<IslandReply> PostAnnouncement(Guid id, string title, string body, DateTimeOffset? expiresAt, string? attachment, bool pinned) => inner.Send("POST", $"{G(id)}/announcements", new { title, body, pinned, expiresAt, attachmentUrl = attachment ?? "" });
    public Task<IslandReply> DeleteAnnouncement(Guid id, Guid announcement) => inner.Send("DELETE", $"{G(id)}/announcements/{announcement}");
    public Task<IslandReply> Chat(Guid id, int take) => inner.Send("GET", $"{G(id)}/chat?take={take}");
    public Task<IslandReply> Send(Guid id, string content) => inner.Send("POST", $"{G(id)}/chat", new { content, clientMessageId = Guid.NewGuid().ToString("N") });

    // land and money
    public Task<IslandReply> Lands(Guid id) => inner.Send("GET", $"{G(id)}/lands");
    public Task<IslandReply> Treasury(Guid id) => inner.Send("GET", $"{G(id)}/treasury");
    public Task<IslandReply> Ledger(Guid id, int take) => inner.Send("GET", $"{G(id)}/treasury/ledger?take={take}");
    public Task<IslandReply> Contribute(Guid id, long amount, string note) => inner.Send("POST", $"{G(id)}/treasury/contribute", new { amount, note, idempotencyKey = Guid.NewGuid().ToString() });
    public Task<IslandReply> Withdraw(Guid id, long amount, Guid? user, string reason) => inner.Send("POST", $"{G(id)}/treasury/withdraw", new { amount, userId = user, reason, idempotencyKey = Guid.NewGuid().ToString() });

    // votes
    public Task<IslandReply> Proposals(Guid id) => inner.Send("GET", $"{G(id)}/proposals?take=50");
    public Task<IslandReply> Proposal(Guid id, Guid proposal) => inner.Send("GET", $"{G(id)}/proposals/{proposal}");
    public Task<IslandReply> CreateProposal(Guid id, string title, string description, string[] options, DateTimeOffset endsAt, int minRank, int quorum, string criterion) =>
        inner.Send("POST", $"{G(id)}/proposals", new { title, description, options, endsAt, minRank, quorumPercent = quorum, criterion });
    public Task<IslandReply> Vote(Guid id, Guid proposal, int option) => inner.Send("POST", $"{G(id)}/proposals/{proposal}/vote", new { option });
    public Task<IslandReply> CloseProposal(Guid id, Guid proposal) => inner.Send("POST", $"{G(id)}/proposals/{proposal}/close", new { });
    public Task<IslandReply> CancelProposal(Guid id, Guid proposal) => inner.Send("DELETE", $"{G(id)}/proposals/{proposal}");
}
