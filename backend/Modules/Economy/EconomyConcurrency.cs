using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LifeVerse.Api.Modules.Economy;

/// <summary>Two people (or two retries) touching the same wallet at the same moment: the database lets only one of them win (the balance and the order state are concurrency tokens). The one that loses must not turn into a server error: it is told to try again, and nothing it did is kept.</summary>
public static class EconomyConcurrency
{
    public const string TryAgain = "try_again";

    /// <summary>Saves and commits. False when another operation changed the same wallet or order first (everything this one did is rolled back with the transaction).</summary>
    public static async Task<bool> CommitAsync(DbContext db, IDbContextTransaction tx, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true; }
        catch (Exception exception) when (IsRace(exception)) { await tx.RollbackAsync(CancellationToken.None); return false; }
    }

    /// <summary>A lost race: the concurrency token of a wallet or order moved, the same idempotency key was written a moment before (unique violation), or the database picked this operation to give way (serialization failure, deadlock). Other database errors are real errors and stay errors.</summary>
    public static bool IsRace(Exception exception) => exception is DbUpdateConcurrencyException || exception is DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" or "40001" or "40P01" } };
}
