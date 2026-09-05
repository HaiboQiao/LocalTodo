using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Data;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 成果记录的校验与持久化服务。
/// </summary>
public sealed class AchievementService
{
    private readonly AchievementRepository
        _repository;

    private readonly IClock
        _clock;

    private readonly ILocalTimeService
        _localTimeService;

    public AchievementService(
        AchievementRepository repository,
        IClock? clock = null,
        ILocalTimeService? localTimeService = null)
    {
        _repository = repository;
        _clock = clock ?? SystemClock.Instance;
        _localTimeService =
            localTimeService ?? LocalTimeService.System;
    }

    public Task<IReadOnlyList<AchievementRecord>>
        GetAllAsync(
            CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    public async Task<AchievementRecord> SaveAsync(
        AchievementRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        string title = record.Title.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException(
                "请输入成果名称。");
        }

        if (title.Length > 200)
        {
            throw new InvalidOperationException(
                "成果名称不能超过 200 个字符。");
        }

        string details = record.Details.Trim();

        if (details.Length > 4000)
        {
            throw new InvalidOperationException(
                "成果说明不能超过 4000 个字符。");
        }

        if (!Enum.IsDefined(record.Category))
        {
            throw new InvalidOperationException(
                "请选择有效的成果分类。");
        }

        DateTime periodStart = record.PeriodStart.Date;
        DateTimeOffset now = _clock.UtcNow;
        DateTime localToday =
            _localTimeService.ToLocalDateTime(now).Date;
        DateTime completedOn =
            (record.CompletedOn ??
             record.PeriodEnd ??
             localToday).Date;

        if (completedOn < periodStart)
        {
            throw new InvalidOperationException(
                "完成日期不能早于开始日期。");
        }

        record.Id = string.IsNullOrWhiteSpace(record.Id)
            ? Guid.NewGuid().ToString("N")
            : record.Id;
        record.Title = title;
        record.Details = details;
        record.PeriodStart = periodStart;
        record.PeriodEnd = completedOn;
        record.CompletedOn = completedOn;
        record.Cycle = AchievementCycle.OneTime;
        record.Status = AchievementStatus.Completed;
        record.ProgressPercent = 100;

        record.CreatedAt = record.CreatedAt == default
            ? now
            : record.CreatedAt;
        record.UpdatedAt = now;

        await _repository.UpsertAsync(
            record,
            cancellationToken);

        return record;
    }

    public Task DeleteAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        _repository.DeleteAsync(id, cancellationToken);
}
