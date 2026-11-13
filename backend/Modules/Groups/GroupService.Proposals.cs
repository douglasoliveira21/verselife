using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Groups;

public sealed record ProposalRequest(string? Title, string? Description, string[]? Options, DateTimeOffset? EndsAt, int? MinRank, int? QuorumPercent, string? Criterion);
public sealed record VoteRequest(int Option);

/// <summary>
/// Votes of a group. People with <c>manage_proposals</c> create proposals (2 to 8 answers, a deadline up to 30 days, who may vote, a quorum and the criterion that makes an answer win); members vote once and
/// cannot change it. While a proposal is open only the number of votes is public (managers also see the split); when it closes everybody sees the result. A proposal closes by itself at the deadline, when everybody
/// who may vote has voted, or when a manager closes it.
/// </summary>
public static partial class GroupService
{
    public static readonly string[] Criteria = ["majority", "two_thirds", "unanimous", "plurality"];

    public sealed record ProposalItem(Guid Id, string Title, string Description, Guid AuthorUserId, string Author, string Status, DateTimeOffset CreatedAt, DateTimeOffset EndsAt, string Criterion, int QuorumPercent, int MinRank,
        string[] Options, int Votes, int Eligible, int? MyVote, bool CanVote, bool CanManage, IReadOnlyList<int>? Counts, int? WinningOption);

    private static string[] OptionsOf(GroupProposal p) { try { return JsonSerializer.Deserialize<string[]>(p.OptionsJson) ?? []; } catch (JsonException) { return []; } }

    private static async Task<int> EligibleAsync(LifeVerseDbContext db, Guid groupId, int minRank, Dictionary<Guid, GroupRole> roles, CancellationToken ct) =>
        (await db.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId).ToListAsync(ct)).Count(m => RoleOf(m, roles).Rank >= minRank);

    // ---- creating and closing --------------------------------------------------------------------------------------------------------------------------

    public static async Task<GroupResult> CreateProposalAsync(LifeVerseDbContext db, Guid me, Guid groupId, ProposalRequest r, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageProposals, now, ct); if (error is not null) return error;
        var title = CleanText(r.Title, newLines: false); if (title.Length is < 3 or > GroupRules.MaxProposalTitle) return GroupResult.Fail("bad_title");
        var description = CleanText(r.Description, newLines: true); if (description.Length > GroupRules.MaxProposalBody) return GroupResult.Fail("bad_description");
        var options = (r.Options ?? []).Select(o => CleanText(o, newLines: false)).ToList();
        if (options.Count is < GroupRules.MinProposalOptions or > GroupRules.MaxProposalOptions || options.Any(o => o.Length is 0 or > GroupRules.MaxOptionLength) || options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count) return GroupResult.Fail("bad_options");
        var ends = r.EndsAt ?? now.AddDays(3); if (ends < now.AddMinutes(5) || ends > now.AddDays(GroupRules.MaxProposalDays)) return GroupResult.Fail("bad_deadline");
        var minRank = r.MinRank ?? 0; if (minRank is < 0 or > GroupRules.MaxCustomRank) return GroupResult.Fail("bad_rank");
        var quorum = r.QuorumPercent ?? 0; if (quorum is < 0 or > 100) return GroupResult.Fail("bad_quorum");
        var criterion = (r.Criterion ?? "majority").Trim().ToLowerInvariant(); if (!Criteria.Contains(criterion)) return GroupResult.Fail("bad_criterion");
        if (await db.GroupProposals.CountAsync(p => p.GroupId == groupId && p.Status == "open", ct) >= GroupRules.MaxOpenProposals) return GroupResult.Fail("too_many_proposals");
        var proposal = new GroupProposal { Id = Guid.NewGuid(), GroupId = groupId, AuthorUserId = me, Title = title, Description = description, OptionsJson = JsonSerializer.Serialize(options), CreatedAt = now, EndsAt = ends, MinRank = minRank, QuorumPercent = quorum, Criterion = criterion };
        db.GroupProposals.Add(proposal); Log(db, groupId, me, "proposal_created", null, new { proposalId = proposal.Id, title, options = options.Count, endsAt = ends }, now);
        var pushed = await AddNotificationsAsync(db, groupId, null, me, "group_proposal", $"Nova votação: {title}", $"Vote até {ends.ToLocalTime():dd/MM/yyyy HH:mm}.", now, ct);
        var saved = await SaveAsync(db, ct, new { id = proposal.Id }); if (!saved.Ok) return saved;
        foreach (var user in pushed) await TellAsync(notify, user, "group_proposal", new { groupId, groupName = ctx!.Group.Name, id = proposal.Id, title, endsAt = ends });
        return saved;
    }

    /// <summary>Counts the votes and decides. Pure: the same votes always give the same answer.</summary>
    public static (string Status, int? Winner, int[] Counts, int Total) Decide(string[] options, IReadOnlyCollection<int> votes, int eligible, int quorumPercent, string criterion)
    {
        var counts = new int[options.Length]; foreach (var v in votes) if (v >= 0 && v < counts.Length) counts[v]++;
        var total = counts.Sum();
        if (total == 0) return ("rejected", null, counts, 0);
        if (eligible > 0 && total * 100L < (long)eligible * quorumPercent) return ("rejected", null, counts, total); // not enough people took part
        var top = counts.Max(); var leaders = counts.Select((c, i) => (c, i)).Where(x => x.c == top).Select(x => x.i).ToList();
        if (leaders.Count != 1) return ("rejected", null, counts, total); // a tie decides nothing
        var wins = criterion switch { "two_thirds" => top * 3L >= total * 2L, "unanimous" => top == total, "plurality" => true, _ => top * 2L > total };
        return wins ? ("approved", leaders[0], counts, total) : ("rejected", null, counts, total);
    }

    private static async Task CloseAsync(LifeVerseDbContext db, GroupProposal proposal, Dictionary<Guid, GroupRole> roles, DateTimeOffset now, GroupNotifier? notify, string how, CancellationToken ct)
    {
        var options = OptionsOf(proposal); var votes = await db.GroupVotes.AsNoTracking().Where(v => v.ProposalId == proposal.Id).Select(v => v.Option).ToListAsync(ct);
        var eligible = await EligibleAsync(db, proposal.GroupId, proposal.MinRank, roles, ct);
        var (status, winner, _, total) = Decide(options, votes, eligible, proposal.QuorumPercent, proposal.Criterion);
        proposal.Status = status; proposal.WinningOption = winner; proposal.ClosedAt = now; proposal.EligibleAtClose = eligible;
        Log(db, proposal.GroupId, proposal.AuthorUserId, "proposal_closed", null, new { proposalId = proposal.Id, status, winner = winner is { } w ? options[w] : null, votes = total, eligible, how }, now);
        var body = status == "approved" ? $"Venceu: {options[winner!.Value]} ({total} voto(s))." : total == 0 ? "Ninguém votou." : "Nenhuma opção atingiu o critério.";
        var pushed = await AddNotificationsAsync(db, proposal.GroupId, null, Guid.Empty, "group_proposal_result", $"Resultado: {proposal.Title}", body, now, ct);
        var name = (await db.SocialGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == proposal.GroupId, ct))?.Name ?? "";
        await db.SaveChangesAsync(ct);
        foreach (var user in pushed) await TellAsync(notify, user, "group_proposal_result", new { groupId = proposal.GroupId, groupName = name, id = proposal.Id, title = proposal.Title, status, winner = winner is { } x ? options[x] : null });
    }

    /// <summary>Closes (and tells the members) the proposals whose deadline has passed. Returns how many.</summary>
    public static async Task<int> ProcessDueProposalsAsync(LifeVerseDbContext db, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var due = await db.GroupProposals.Where(p => p.Status == "open" && p.EndsAt <= now).OrderBy(p => p.EndsAt).Take(50).ToListAsync(ct); var closed = 0;
        foreach (var proposal in due)
        {
            var roles = await db.GroupRoles.AsNoTracking().Where(r => r.GroupId == proposal.GroupId).ToDictionaryAsync(r => r.Id, ct);
            try { await CloseAsync(db, proposal, roles, now, notify, "deadline", ct); closed++; }
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException) { foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; }
        }
        return closed;
    }

    /// <summary>Closes a proposal now (a manager) or throws it away (cancel: the votes are kept but nothing was decided).</summary>
    public static async Task<GroupResult> EndProposalAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid proposalId, bool cancel, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageProposals, now, ct); if (error is not null) return error;
        var proposal = await db.GroupProposals.SingleOrDefaultAsync(p => p.Id == proposalId && p.GroupId == groupId, ct); if (proposal is null) return GroupResult.Fail("not_found");
        if (proposal.Status != "open") return GroupResult.Fail("not_open");
        if (cancel)
        {
            proposal.Status = "cancelled"; proposal.ClosedAt = now; Log(db, groupId, me, "proposal_cancelled", null, new { proposalId, proposal.Title }, now);
            return await SaveAsync(db, ct, new { proposalId, status = "cancelled" });
        }
        await CloseAsync(db, proposal, ctx!.Roles, now, notify, "manager", ct);
        return GroupResult.Done(new { proposalId, status = proposal.Status });
    }

    // ---- voting --------------------------------------------------------------------------------------------------------------------------------------------

    public static async Task<GroupResult> VoteAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid proposalId, int option, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var proposal = await db.GroupProposals.SingleOrDefaultAsync(p => p.Id == proposalId && p.GroupId == groupId, ct); if (proposal is null) return GroupResult.Fail("not_found");
        if (proposal.Status == "open" && proposal.EndsAt <= now) { await CloseAsync(db, proposal, ctx!.Roles, now, notify, "deadline", ct); return GroupResult.Fail("not_open"); }
        if (proposal.Status != "open") return GroupResult.Fail("not_open");
        if (RoleOf(ctx!.Member!, ctx.Roles).Rank < proposal.MinRank) return GroupResult.Fail("not_eligible");
        var options = OptionsOf(proposal); if (option < 0 || option >= options.Length) return GroupResult.Fail("bad_option");
        if (await db.GroupVotes.AnyAsync(v => v.ProposalId == proposalId && v.UserId == me, ct)) return GroupResult.Fail("already_voted");
        db.GroupVotes.Add(new GroupVote { Id = Guid.NewGuid(), ProposalId = proposalId, UserId = me, Option = option, CreatedAt = now });
        var saved = await SaveAsync(db, ct, new { proposalId, voted = true }); if (!saved.Ok) return saved.Error == "conflict" ? GroupResult.Fail("already_voted") : saved; // the unique index answers a double click
        var eligible = await EligibleAsync(db, groupId, proposal.MinRank, ctx.Roles, ct);
        if (await db.GroupVotes.CountAsync(v => v.ProposalId == proposalId, ct) >= eligible) await CloseAsync(db, proposal, ctx.Roles, now, notify, "everyone_voted", ct); // nobody left to wait for
        return saved;
    }

    // ---- reading ------------------------------------------------------------------------------------------------------------------------------------------------

    private static async Task<List<ProposalItem>> ItemsAsync(LifeVerseDbContext db, Ctx ctx, Guid me, List<GroupProposal> list, DateTimeOffset now, CancellationToken ct)
    {
        var ids = list.Select(p => p.Id).ToList();
        var votes = await db.GroupVotes.AsNoTracking().Where(v => ids.Contains(v.ProposalId)).ToListAsync(ct);
        var names = await NamesAsync(db, list.Select(p => p.AuthorUserId), ct); var manage = GroupRules.Has(ctx.Actor!, GroupPermissions.ManageProposals);
        var members = await db.GroupMembers.AsNoTracking().Where(m => m.GroupId == ctx.Group.Id).ToListAsync(ct);
        return [.. list.Select(p =>
        {
            var options = OptionsOf(p); var mine = votes.FirstOrDefault(v => v.ProposalId == p.Id && v.UserId == me); var all = votes.Where(v => v.ProposalId == p.Id).ToList();
            var eligible = p.Status == "open" ? members.Count(m => RoleOf(m, ctx.Roles).Rank >= p.MinRank) : p.EligibleAtClose; var canVote = p.Status == "open" && p.EndsAt > now && mine is null && RoleOf(ctx.Member!, ctx.Roles).Rank >= p.MinRank;
            var showSplit = p.Status != "open" || manage; // while it runs, only managers see how the votes split
            var counts = showSplit ? Enumerable.Range(0, options.Length).Select(i => all.Count(v => v.Option == i)).ToList() : null;
            return new ProposalItem(p.Id, p.Title, p.Description, p.AuthorUserId, names.GetValueOrDefault(p.AuthorUserId, "?"), p.Status, p.CreatedAt, p.EndsAt, p.Criterion, p.QuorumPercent, p.MinRank, options, all.Count, eligible, mine?.Option, canVote, manage, counts, p.WinningOption);
        })];
    }

    public static async Task<GroupResult> ProposalsAsync(LifeVerseDbContext db, Guid me, Guid groupId, string? status, int skip, int take, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var query = db.GroupProposals.AsNoTracking().Where(p => p.GroupId == groupId);
        if (status is "open" or "approved" or "rejected" or "cancelled") query = query.Where(p => p.Status == status); else if (status == "closed") query = query.Where(p => p.Status != "open");
        var total = await query.CountAsync(ct);
        var list = await query.OrderBy(p => p.Status == "open" ? 0 : 1).ThenByDescending(p => p.CreatedAt).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 50)).ToListAsync(ct);
        return GroupResult.Done(new { total, items = await ItemsAsync(db, ctx!, me, list, now, ct) });
    }

    public static async Task<GroupResult> ProposalAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid proposalId, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var proposal = await db.GroupProposals.AsNoTracking().SingleOrDefaultAsync(p => p.Id == proposalId && p.GroupId == groupId, ct); if (proposal is null) return GroupResult.Fail("not_found");
        return GroupResult.Done((await ItemsAsync(db, ctx!, me, [proposal], now, ct))[0]);
    }
}

/// <summary>Closes the proposals whose deadline has passed, once a minute, and tells the members the result.</summary>
public sealed class GroupProposalWorker(IServiceScopeFactory scopes, ILogger<GroupProposalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var closed = await GroupService.ProcessDueProposalsAsync(scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>(), DateTimeOffset.UtcNow, null, stoppingToken);
                if (closed > 0) logger.LogInformation("event=group_proposals_closed count={Count}", closed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning(exception, "event=group_proposals_failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
