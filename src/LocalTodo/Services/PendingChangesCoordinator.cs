using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LocalTodo.Helpers;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 串行提交多个编辑器，确保共享 SQLite 写入不会互相争用。
/// </summary>
public sealed class PendingChangesCoordinator
{
    /// <summary>
    /// 尝试提交所有编辑器。
    ///
    /// 某个编辑器失败后仍继续处理其他编辑器，避免一个无效输入
    /// 阻止其他页面的有效修改被保存；最终返回遇到的第一个失败。
    /// </summary>
    public async Task<FlushResult> FlushAllAsync(
        IEnumerable<IPendingChanges> editors)
    {
        ArgumentNullException.ThrowIfNull(
            editors);

        FlushResult? firstFailure =
            null;

        HashSet<IPendingChanges> visited =
            new(
                ReferenceEqualityComparer.Instance);

        foreach (IPendingChanges editor
                 in editors)
        {
            if (editor is null ||
                !visited.Add(editor))
            {
                continue;
            }

            try
            {
                FlushResult result =
                    await editor
                        .FlushPendingChangesAsync();

                if (!result.Succeeded)
                {
                    firstFailure ??=
                        result;
                }
            }
            catch (Exception exception)
            {
                AppLog.Error(
                    "提交编辑器中的待保存内容失败。",
                    exception);

                firstFailure ??=
                    FlushResult.Failed(
                        $"保存失败：{exception.Message}");
            }
        }

        return firstFailure ??
            FlushResult.Success();
    }

    /// <summary>
    /// Windows 注销或关机使用的有界尽力保存。
    /// 超时后立即返回，不无限阻止系统退出。
    /// </summary>
    public async Task<FlushResult>
        FlushAllWithinAsync(
            IEnumerable<IPendingChanges> editors,
            TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout));
        }

        Task<FlushResult> flushTask =
            FlushAllAsync(editors);

        Task completedTask =
            await Task.WhenAny(
                flushTask,
                Task.Delay(timeout));

        if (ReferenceEquals(
                completedTask,
                flushTask))
        {
            return await flushTask;
        }

        return FlushResult.TimedOut(
            "保存尚未完成，Windows 会话即将结束");
    }

    /// <summary>
    /// 普通页面切换、隐藏或退出前的统一决策。
    /// 保存失败时必须由调用方明确确认，才会放弃输入并继续。
    /// </summary>
    public async Task<bool>
        PrepareForTransitionAsync(
            IEnumerable<IPendingChanges> editors,
            Func<FlushResult, bool>
                confirmDiscard)
    {
        ArgumentNullException.ThrowIfNull(
            editors);

        ArgumentNullException.ThrowIfNull(
            confirmDiscard);

        List<IPendingChanges> editorList =
            [.. editors];

        FlushResult result =
            await FlushAllAsync(
                editorList);

        if (result.Succeeded)
        {
            return true;
        }

        if (!confirmDiscard(result))
        {
            return false;
        }

        DiscardAll(
            editorList);

        return true;
    }

    public void DiscardAll(
        IEnumerable<IPendingChanges> editors)
    {
        ArgumentNullException.ThrowIfNull(
            editors);

        HashSet<IPendingChanges> visited =
            new(
                ReferenceEqualityComparer.Instance);

        foreach (IPendingChanges editor
                 in editors)
        {
            if (editor is null ||
                !visited.Add(editor))
            {
                continue;
            }

            editor.DiscardPendingChanges();
        }
    }
}
