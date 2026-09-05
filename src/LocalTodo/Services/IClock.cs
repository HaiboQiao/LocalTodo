using System;

namespace LocalTodo.Services;

/// <summary>
/// 提供当前绝对时间。
///
/// 业务代码通过该接口获取时间，测试可以使用固定时钟，避免依赖
/// 运行测试时的真实日期和速度。
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow
    { get; }
}

/// <summary>
/// 正式程序使用的系统时钟。
/// </summary>
public sealed class SystemClock : IClock
{
    public static SystemClock Instance
    { get; } = new();

    private SystemClock()
    {
    }

    public DateTimeOffset UtcNow =>
        DateTimeOffset.UtcNow;
}
