namespace LocalTodo.Models;

/// <summary>
/// 一个窗口的位置和尺寸。
/// </summary>
public sealed record WindowPlacement(
    double Left,
    double Top,
    double Width,
    double Height);
