using System;

namespace LocalTodo.Models;

/// <summary>
/// 一次带基线版本和脏字段集合的普通详情保存请求。
/// </summary>
public sealed record TaskEditRequest(
    TaskEditBaseline Baseline,
    TaskEditDraft Draft,
    TaskEditFields ChangedFields)
{
    public TaskEditRequest Validate()
    {
        ArgumentNullException.ThrowIfNull(Baseline);
        ArgumentNullException.ThrowIfNull(Draft);

        if (ChangedFields ==
            TaskEditFields.None)
        {
            throw new ArgumentException(
                "任务编辑请求没有包含任何修改字段。",
                nameof(ChangedFields));
        }

        if ((ChangedFields &
             ~TaskEditFields.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ChangedFields),
                ChangedFields,
                "任务编辑请求包含无法识别的字段。 ");
        }

        return this;
    }
}
