using System.Threading.Tasks;
using LocalTodo.Models;

namespace LocalTodo.Services;

/// <summary>
/// 拥有尚未持久化编辑内容的页面或编辑器。
/// </summary>
public interface IPendingChanges
{
    /// <summary>
    /// 等待正在执行的保存，并立即提交所有等待中的修改。
    /// </summary>
    Task<FlushResult> FlushPendingChangesAsync();

    /// <summary>
    /// 用户明确确认放弃时，清除尚未保存的编辑内容。
    /// </summary>
    void DiscardPendingChanges();
}
