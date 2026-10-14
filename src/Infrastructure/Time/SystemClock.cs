using Application.Abstractions;

namespace Infrastructure.Time;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
