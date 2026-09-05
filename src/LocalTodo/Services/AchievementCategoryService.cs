using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LocalTodo.Data;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 自定义成果分类的校验与持久化服务。
/// </summary>
public sealed class AchievementCategoryService
{
    private readonly AchievementCategoryRepository _repository;
    private readonly IClock _clock;

    public AchievementCategoryService(
        AchievementCategoryRepository repository,
        IClock? clock = null)
    {
        _repository = repository;
        _clock = clock ?? SystemClock.Instance;
    }

    public Task<IReadOnlyList<AchievementCategoryDefinition>>
        GetAllAsync(
            CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    public async Task<AchievementCategoryDefinition> SaveAsync(
        AchievementCategoryDefinition category,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        string name = category.Name.Trim();
        string color = category.ColorHex.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("请输入分类名称。");
        }

        if (name.Length > 30)
        {
            throw new InvalidOperationException("分类名称不能超过 30 个字符。");
        }

        if (!AchievementCategoryColor.IsValidHex(color))
        {
            throw new InvalidOperationException(
                "分类颜色必须是 #RRGGBB 格式，例如 #4F6BED。");
        }

        IReadOnlyList<AchievementCategoryDefinition> all =
            await _repository.GetAllAsync(cancellationToken);

        if (all.Any(existing =>
                !string.Equals(
                    existing.Id,
                    category.Id,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    existing.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("已经存在同名分类。");
        }

        category.Id = string.IsNullOrWhiteSpace(category.Id)
            ? $"custom-{Guid.NewGuid():N}"
            : category.Id;
        category.Name = name;
        category.ColorHex = color.ToUpperInvariant();

        if (category.SortOrder <= 0)
        {
            category.SortOrder =
                (all.Count == 0 ? 0 : all.Max(item => item.SortOrder)) + 10;
        }

        await _repository.UpsertAsync(
            category,
            _clock.UtcNow,
            cancellationToken);

        return category;
    }

    public Task DeleteAsync(
        AchievementCategoryDefinition category,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (!category.CanDelete)
        {
            throw new InvalidOperationException(
                "“其他”是未分类成果的保底分类，不能删除。你仍可以修改它的名称和颜色。");
        }

        return _repository.DeleteAndReassignAsync(
            category.Id,
            cancellationToken);
    }

    public async Task ReorderAsync(
        IReadOnlyList<string> orderedCategoryIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedCategoryIds);

        IReadOnlyList<AchievementCategoryDefinition> current =
            await _repository.GetAllAsync(cancellationToken);
        HashSet<string> expectedIds = current
            .Select(category => category.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> submittedIds = orderedCategoryIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (orderedCategoryIds.Count != current.Count ||
            submittedIds.Count != orderedCategoryIds.Count ||
            !expectedIds.SetEquals(submittedIds))
        {
            throw new InvalidOperationException(
                "分类列表已发生变化，请重新打开分类管理后再试。");
        }

        await _repository.UpdateSortOrdersAsync(
            orderedCategoryIds,
            _clock.UtcNow,
            cancellationToken);
    }
}
