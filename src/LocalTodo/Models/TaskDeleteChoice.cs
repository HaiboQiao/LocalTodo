namespace LocalTodo.Models;

/// <summary>
/// 用户删除任务时选择的删除方式。
/// </summary>
public enum TaskDeleteChoice
{
    /// <summary>
    /// 用户取消。
    /// </summary>
    Cancel = 0,

    /// <summary>
    /// 普通不循环任务的普通删除。
    /// </summary>
    DeleteSingleTask = 1,

    /// <summary>
    /// 删除当前循环周期。
    ///
    /// 当前这一期进入垃圾箱，
    /// 后续循环继续。
    /// </summary>
    DeleteCurrentOccurrence = 2,

    /// <summary>
    /// 删除整个循环。
    ///
    /// 当前这一期进入垃圾箱，
    /// 后续循环停止。
    /// </summary>
    DeleteEntireSeries = 3
}
