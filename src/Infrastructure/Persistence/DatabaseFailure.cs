using System.Data.Common;
using System.Net.Sockets;

using Npgsql;

namespace Infrastructure.Persistence;

internal static class DatabaseFailure
{
    /// <summary>
    /// True when the exception (or anything in its inner chain) is a database/transport failure.
    /// EF Core wraps provider errors in InvalidOperationException ("likely due to a transient
    /// failure"), so matching only the outer type would let an unreachable database surface as a
    /// 500 instead of the 503 the contract promises. The chain is walked deliberately rather than
    /// catching InvalidOperationException wholesale, which would also swallow programming errors.
    /// </summary>
    internal static bool IsUnavailable(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: var state } && IsCausedByTheRequest(state))
            {
                // The server answered and refused the data: a request problem, never "unreachable".
                return false;
            }

            if (current is DbException or SocketException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The PostgreSQL error code of the first server-side error in the chain, if any.</summary>
    internal static string? SqlStateOf(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres.SqlState;
            }
        }

        return null;
    }

    /// <summary>Class 22 (data exception) and class 23 (integrity constraint violation).</summary>
    private static bool IsCausedByTheRequest(string state) =>
        state.StartsWith("22", StringComparison.Ordinal) || state.StartsWith("23", StringComparison.Ordinal);
}
