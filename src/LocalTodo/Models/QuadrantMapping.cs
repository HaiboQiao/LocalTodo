using System;

namespace LocalTodo.Models;

/// <summary>
/// 统一处理象限显示以及旧优先级兼容映射。
///
/// 新版本中，象限是用户真正使用的分类。
/// TaskPriority 仅暂时用于兼容旧数据库结构。
/// </summary>
public static class QuadrantMapping
{
    /// <summary>
    /// 将旧优先级转换为象限。
    /// </summary>
    public static QuadrantType
        FromLegacyPriority(
            TaskPriority priority)
    {
        return priority switch
        {
            TaskPriority.High =>
                QuadrantType.ImportantAndUrgent,

            TaskPriority.Medium =>
                QuadrantType.ImportantNotUrgent,

            TaskPriority.Low =>
                QuadrantType.UrgentNotImportant,

            _ =>
                QuadrantType.NotImportantNotUrgent
        };
    }

    /// <summary>
    /// 将象限转换为旧优先级值，
    /// 仅用于兼容现有数据库。
    /// </summary>
    public static TaskPriority
        ToLegacyPriority(
            QuadrantType quadrant)
    {
        ValidateQuadrant(quadrant);

        return quadrant switch
        {
            QuadrantType.ImportantAndUrgent =>
                TaskPriority.High,

            QuadrantType.ImportantNotUrgent =>
                TaskPriority.Medium,

            QuadrantType.UrgentNotImportant =>
                TaskPriority.Low,

            QuadrantType.NotImportantNotUrgent =>
                TaskPriority.None,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(quadrant),
                    quadrant,
                    "无法识别象限。")
        };
    }

    /// <summary>
    /// 第一、第二象限属于重要任务。
    /// </summary>
    public static bool IsImportant(
        QuadrantType quadrant)
    {
        ValidateQuadrant(quadrant);

        return quadrant is
            QuadrantType.ImportantAndUrgent or
            QuadrantType.ImportantNotUrgent;
    }

    public static string GetShortTitle(
        QuadrantType quadrant)
    {
        ValidateQuadrant(quadrant);

        return quadrant switch
        {
            QuadrantType.ImportantAndUrgent =>
                "第一象限",

            QuadrantType.ImportantNotUrgent =>
                "第二象限",

            QuadrantType.UrgentNotImportant =>
                "第三象限",

            QuadrantType.NotImportantNotUrgent =>
                "第四象限",

            _ =>
                "未知象限"
        };
    }

    public static string GetDescription(
        QuadrantType quadrant)
    {
        ValidateQuadrant(quadrant);

        return quadrant switch
        {
            QuadrantType.ImportantAndUrgent =>
                "重要且紧急",

            QuadrantType.ImportantNotUrgent =>
                "重要但不紧急",

            QuadrantType.UrgentNotImportant =>
                "紧急但不重要",

            QuadrantType.NotImportantNotUrgent =>
                "不重要且不紧急",

            _ =>
                string.Empty
        };
    }

    public static string GetDisplayText(
        QuadrantType quadrant)
    {
        return
            $"{GetShortTitle(quadrant)} · " +
            $"{GetDescription(quadrant)}";
    }

    public static void ValidateQuadrant(
        QuadrantType quadrant)
    {
        if (!Enum.IsDefined(
                typeof(QuadrantType),
                quadrant))
        {
            throw new ArgumentOutOfRangeException(
                nameof(quadrant),
                quadrant,
                "无法识别象限。");
        }
    }
}
