using System;

namespace LocalTodo.Models;

/// <summary>
/// 详情编辑器中真正被用户修改过的字段组。
/// </summary>
[Flags]
public enum TaskEditFields
{
    None = 0,

    Title = 1 << 0,

    Description = 1 << 1,

    /// <summary>
    /// 截止日期、具体时间及提醒设置。
    /// 这些字段存在联动，作为一个不可拆分的保存单元。
    /// </summary>
    Schedule = 1 << 2,

    Repeat = 1 << 3,

    IsImportant = 1 << 4,

    Quadrant = 1 << 5,

    All =
        Title |
        Description |
        Schedule |
        Repeat |
        IsImportant |
        Quadrant
}
