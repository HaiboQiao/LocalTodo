using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Data;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 每周计划的校验与持久化服务。
/// </summary>
public sealed class WeeklyPlanService
{
    private readonly WeeklyPlanRepository
        _repository;

    private readonly IClock
        _clock;

    public WeeklyPlanService(
        WeeklyPlanRepository repository,
        IClock? clock = null)
    {
        _repository = repository;
        _clock = clock ?? SystemClock.Instance;
    }

    public Task<IReadOnlyList<WeeklyPlanItem>>
        GetAllAsync(
            CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    public async Task<WeeklyPlanItem> SaveAsync(
        WeeklyPlanItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        NormalizeAndValidate(item);

        DateTimeOffset now = _clock.UtcNow;

        item.Id = string.IsNullOrWhiteSpace(item.Id)
            ? Guid.NewGuid().ToString("N")
            : item.Id;
        item.CreatedAt = item.CreatedAt == default
            ? now
            : item.CreatedAt;
        item.UpdatedAt = now;

        await _repository.UpsertAsync(
            item,
            cancellationToken);

        return item;
    }

    private static void NormalizeAndValidate(
        WeeklyPlanItem item)
    {

        string title = item.Title.Trim();
        string description = item.Description.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException(
                "请输入安排名称。");
        }

        if (title.Length > 120)
        {
            throw new InvalidOperationException(
                "安排名称不能超过 120 个字符。");
        }

        if (description.Length > 1000)
        {
            throw new InvalidOperationException(
                "安排说明不能超过 1000 个字符。");
        }

        if (!Enum.IsDefined(item.Day))
        {
            throw new InvalidOperationException(
                "请选择有效的星期。");
        }

        if (item.StartMinutes < 0 ||
            item.StartMinutes >= 1440 ||
            item.EndMinutes - item.StartMinutes <
                WeeklyPlanRules.MinimumDurationMinutes ||
            item.EndMinutes > 1440)
        {
            throw new InvalidOperationException(
                "结束时间必须至少比开始时间晚 15 分钟。");
        }

        if (!Enum.IsDefined(item.Color))
        {
            throw new InvalidOperationException(
                "请选择有效的安排颜色。");
        }

        item.Title = title;
        item.Description = description;
    }

    public Task DeleteAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        _repository.DeleteAsync(id, cancellationToken);
}
