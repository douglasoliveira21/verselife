using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LifeVerse.Api.Data;

/// <summary>
/// PostgreSQL's "timestamp with time zone" only takes UTC through Npgsql: a moment sent with an offset (a client at -03:00 creating an event) was refused
/// and the person saw an error. The instant is the same, so every date parameter goes to the database as UTC, whether it is being written or compared.
/// </summary>
internal sealed class UtcParameters : DbCommandInterceptor
{
    public static readonly UtcParameters Instance = new();

    private static void Fix(DbCommand command)
    {
        foreach (DbParameter parameter in command.Parameters)
            if (parameter.Value is DateTimeOffset moment && moment.Offset != TimeSpan.Zero) parameter.Value = moment.ToUniversalTime();
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result) { Fix(command); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default) { Fix(command); return ValueTask.FromResult(result); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result) { Fix(command); return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default) { Fix(command); return ValueTask.FromResult(result); }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result) { Fix(command); return result; }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken ct = default) { Fix(command); return ValueTask.FromResult(result); }
}
