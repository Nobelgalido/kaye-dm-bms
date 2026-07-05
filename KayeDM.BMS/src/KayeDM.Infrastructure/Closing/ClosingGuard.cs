using KayeDM.Domain.Exceptions;
using KayeDM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KayeDM.Infrastructure.Closing;

public static class ClosingGuard
{
    // "On/before a closed date" — a closing on date D locks D and everything
    // before it, so the check is whether any closing exists on or after the
    // target date.
    public static async Task EnsureDateNotClosedAsync(AppDbContext db, DateTime date, string action)
    {
        var isClosed = await db.DailyClosings.AnyAsync(c => c.Date >= date.Date);
        if (isClosed)
        {
            throw new DateClosedException($"Cannot {action} — {date.Date:MMM d, yyyy} is on or before a closed date.");
        }
    }
}
