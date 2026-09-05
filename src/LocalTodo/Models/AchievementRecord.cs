using System;

namespace LocalTodo.Models;

/// <summary>
/// 一条已经取得并归档的成果记录。
/// Cycle、Status 和 ProgressPercent 仅为旧数据库兼容字段，
/// 新界面不再把成果当作计划或进度项目使用。
/// </summary>
public sealed class AchievementRecord
{
    public string Id
    { get; set; } = string.Empty;

    public string Title
    { get; set; } = string.Empty;

    public string Details
    { get; set; } = string.Empty;

    public AchievementCategory Category
    { get; set; }

    public string CategoryId
    { get; set; } = AchievementCategoryDefinition.OtherCategoryId;

    public string CategoryName
    { get; set; } = string.Empty;

    public string CategoryColor
    { get; set; } = "#7C8598";

    public AchievementCycle Cycle
    { get; set; }

    public AchievementStatus Status
    { get; set; }

    public int ProgressPercent
    { get; set; }

    public DateTime PeriodStart
    { get; set; } = DateTime.Today;

    public DateTime? PeriodEnd
    { get; set; }

    public DateTime? CompletedOn
    { get; set; }

    public DateTimeOffset CreatedAt
    { get; set; }

    public DateTimeOffset UpdatedAt
    { get; set; }

    public string CategoryText =>
        string.IsNullOrWhiteSpace(CategoryName)
            ? Category.GetTitle()
            : CategoryName;

    public string CategoryColorHex =>
        AchievementCategoryColor.Normalize(CategoryColor);

    public string CategorySoftColorHex =>
        AchievementCategoryColor.CreateSoftColor(CategoryColorHex);

    public string CycleText =>
        Cycle.GetTitle();

    public string StatusText =>
        Status.GetTitle();

    public DateTime CompletedDate =>
        (CompletedOn ?? PeriodEnd ?? PeriodStart).Date;

    public string PeriodText =>
        $"{PeriodStart:yyyy-MM-dd} 至 " +
        $"{CompletedDate:yyyy-MM-dd}";

    public string CompactPeriodText =>
        $"{PeriodStart:yyyy.MM.dd}  —  " +
        $"{CompletedDate:yyyy.MM.dd}";

    public int DurationDays =>
        Math.Max(
            1,
            (CompletedDate - PeriodStart.Date).Days);

    public string DurationText =>
        CompletedDate == PeriodStart.Date
            ? "当天完成"
            : $"持续 {DurationDays} 天";

    public string CompletedDateText =>
        CompletedDate.ToString("yyyy年M月d日");

    public string DetailsDisplayText =>
        string.IsNullOrWhiteSpace(Details)
            ? "未填写成果描述。"
            : Details;

    public string ProgressText =>
        $"{ProgressPercent}%";
}

public enum AchievementCategory
{
    Other = 0,
    Work = 1,
    Learning = 2,
    Health = 3,
    Life = 4,
    Project = 5,
    Skill = 6,
    Breakthrough = 7
}

public enum AchievementCycle
{
    OneTime = 0,
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4,
    LongTerm = 5,
    Custom = 6
}

public enum AchievementStatus
{
    InProgress = 0,
    Completed = 1,
    Paused = 2
}

public static class AchievementDisplayExtensions
{
    public static string GetTitle(
        this AchievementCategory category) =>
        category switch
        {
            AchievementCategory.Skill => "技能成长",
            AchievementCategory.Project => "项目成果",
            AchievementCategory.Learning => "学习成果",
            AchievementCategory.Work => "工作成果",
            AchievementCategory.Life => "生活体验",
            AchievementCategory.Health => "健康成长",
            AchievementCategory.Breakthrough => "个人突破",
            _ => "其他"
        };

    public static string GetColorHex(
        this AchievementCategory category) =>
        category switch
        {
            AchievementCategory.Skill => "#4F6BED",
            AchievementCategory.Project => "#7357E6",
            AchievementCategory.Learning => "#35A77B",
            AchievementCategory.Work => "#4B6B9A",
            AchievementCategory.Life => "#E58A45",
            AchievementCategory.Health => "#3B82F6",
            AchievementCategory.Breakthrough => "#D05A8A",
            _ => "#7C8598"
        };

    public static string GetSoftColorHex(
        this AchievementCategory category) =>
        category switch
        {
            AchievementCategory.Skill => "#EEF1FF",
            AchievementCategory.Project => "#F2EFFF",
            AchievementCategory.Learning => "#ECF8F3",
            AchievementCategory.Work => "#EFF3F8",
            AchievementCategory.Life => "#FFF4EA",
            AchievementCategory.Health => "#EDF5FF",
            AchievementCategory.Breakthrough => "#FCEFF5",
            _ => "#F2F4F7"
        };

    public static string GetTitle(
        this AchievementCycle cycle) =>
        cycle switch
        {
            AchievementCycle.OneTime => "一次性",
            AchievementCycle.Weekly => "每周",
            AchievementCycle.Monthly => "每月",
            AchievementCycle.Quarterly => "每季度",
            AchievementCycle.Yearly => "每年",
            AchievementCycle.LongTerm => "长期",
            AchievementCycle.Custom => "自定义周期",
            _ => "未设置"
        };

    public static string GetTitle(
        this AchievementStatus status) =>
        status switch
        {
            AchievementStatus.Completed => "已完成",
            AchievementStatus.Paused => "已暂停",
            _ => "进行中"
        };
}
