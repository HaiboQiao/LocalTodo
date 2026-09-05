using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LocalTodo.Models;

namespace LocalTodo.ViewModels;

/// <summary>
/// 所有任务新增和详情界面共用的选项目录。
/// </summary>
public static class TaskEditorOptionCatalog
{
    public static IReadOnlyList<TaskQuadrantOption>
        Quadrants
    { get; } =
        Array.AsReadOnly<TaskQuadrantOption>(
        [
            new(
                QuadrantType.ImportantAndUrgent,
                "第一象限"),

            new(
                QuadrantType.ImportantNotUrgent,
                "第二象限"),

            new(
                QuadrantType.UrgentNotImportant,
                "第三象限"),

            new(
                QuadrantType.NotImportantNotUrgent,
                "第四象限")
        ]);

    public static IReadOnlyList<TaskDueTimeOption>
        DueTimes
    { get; } =
        CreateDueTimeOptions();

    public static IReadOnlyList<TaskReminderOption>
        Reminders
    { get; } =
        Array.AsReadOnly<TaskReminderOption>(
        [
            new(false, 0, "不提醒"),
            new(true, 0, "到点提醒"),
            new(true, 5, "提前5分钟"),
            new(true, 15, "提前15分钟"),
            new(true, 30, "提前30分钟"),
            new(true, 60, "提前1小时"),
            new(true, 240, "提前4小时"),
            new(true, 1440, "提前1天")
        ]);

    public static IReadOnlyList<TaskRepeatOption>
        Repeats
    { get; } =
        Array.AsReadOnly<TaskRepeatOption>(
        [
            new(TaskRepeatType.None, "不循环"),
            new(TaskRepeatType.Daily, "每天"),
            new(TaskRepeatType.Weekly, "每周"),
            new(TaskRepeatType.Monthly, "每月"),
            new(TaskRepeatType.Yearly, "每年"),
            new(TaskRepeatType.Weekdays, "每周工作日")
        ]);

    private static ReadOnlyCollection<TaskDueTimeOption>
        CreateDueTimeOptions()
    {
        List<TaskDueTimeOption> options =
        [
            new(null, "无")
        ];

        for (int hour = 0;
             hour < 24;
             hour++)
        {
            options.Add(
                new TaskDueTimeOption(
                    new TimeSpan(
                        hour,
                        0,
                        0),
                    $"{hour:00}:00"));

            options.Add(
                new TaskDueTimeOption(
                    new TimeSpan(
                        hour,
                        30,
                        0),
                    $"{hour:00}:30"));
        }

        return options.AsReadOnly();
    }
}

public sealed record TaskQuadrantOption(
    QuadrantType Value,
    string Title);

public sealed record TaskDueTimeOption(
    TimeSpan? Value,
    string Title);

public sealed record TaskReminderOption(
    bool Enabled,
    int MinutesBefore,
    string Title);

public sealed record TaskRepeatOption(
    TaskRepeatType Value,
    string Title);
