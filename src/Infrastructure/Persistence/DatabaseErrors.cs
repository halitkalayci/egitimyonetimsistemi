using Application.Common;

namespace Infrastructure.Persistence;

internal static class DatabaseErrors
{
    /// <summary>
    /// The one user-facing message for an unreachable database: no host, port, SQL or provider
    /// detail (docs/security.md, Input & output). The HTTP boundary turns it into 503.
    /// </summary>
    internal static Error Unavailable { get; } = Error.Unavailable(
        "database.unavailable",
        "Veritabanına şu anda ulaşılamıyor. Lütfen birkaç saniye sonra tekrar deneyin.");
}
