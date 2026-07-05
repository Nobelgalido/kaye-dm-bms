namespace KayeDM.Application.Closing;

public interface IClosingService
{
    // Always computes for today — closing is only ever done for the current day.
    Task<TodaysFiguresDto> GetTodaysFiguresAsync();

    Task<DailyClosingDto> CreateClosingAsync(string closedById);

    Task<bool> IsDateClosedAsync(DateOnly date);
}
