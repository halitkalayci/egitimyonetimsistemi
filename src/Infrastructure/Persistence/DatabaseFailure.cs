using System.Data.Common;
using System.Net.Sockets;

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
            if (current is DbException or SocketException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}
