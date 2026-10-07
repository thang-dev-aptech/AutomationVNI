using System.Linq.Expressions;
using System.Reflection;
using Backend.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.SocialChannel;

/// <summary>
/// Thứ tự hiển thị Page/kênh (R-028): tên chứa "vni" (không phân biệt hoa/thường) lên trước, trong mỗi
/// nhóm A→Z theo tên rồi theo Id. Dịch được sang SQL SQLite để áp TRƯỚC Skip/Take.
///
/// Nhóm VNi dùng lower() ASCII (chuỗi "vni" không dấu). A→Z dùng collation SQLite "VI"
/// (CultureInfo vi-VN), khớp Intl.Collator('vi') trên bộ tên kiểm thử dùng chung với frontend.
/// </summary>
public static class PageOrdering
{
    public const string VietnameseCollation = "VI";

    private const string VniMarker = "vni";

    private static readonly MethodInfo ToLowerMethod =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo ContainsMethod =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    private static readonly MethodInfo CollateMethod =
        typeof(RelationalDbFunctionsExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(RelationalDbFunctionsExtensions.Collate)
                && m.GetParameters().Length == 3)
            .MakeGenericMethod(typeof(string));

    public static IOrderedQueryable<SocialChannelModel> OrderByVniFirst(
        this IQueryable<SocialChannelModel> source)
        => source.OrderByVniFirst(x => x.PageName, x => x.Id);

    /// <summary>Dùng cho thực thể hiển thị bằng tên kênh nhưng không phải kênh (vd. PageContext).</summary>
    public static IOrderedQueryable<T> OrderByVniFirst<T>(
        this IQueryable<T> source,
        Expression<Func<T, string>> name,
        Expression<Func<T, Guid>> id)
    {
        var lowered = Expression.Call(name.Body, ToLowerMethod);
        var isVni = Expression.Call(lowered, ContainsMethod, Expression.Constant(VniMarker));
        var group = Expression.Lambda<Func<T, int>>(
            Expression.Condition(isVni, Expression.Constant(0), Expression.Constant(1)),
            name.Parameters);
        var collated = Expression.Call(
            CollateMethod,
            Expression.Constant(EF.Functions),
            name.Body,
            Expression.Constant(VietnameseCollation));
        var alphabetical = Expression.Lambda<Func<T, string>>(collated, name.Parameters);

        return source.OrderBy(group).ThenBy(alphabetical).ThenBy(id);
    }

    /// <summary>
    /// Phân trang cho query ĐÃ có thứ tự riêng. GenericRepository.PaginateAsync luôn ghi đè bằng
    /// CreatedAt giảm dần nên không dùng được; cùng quy tắc chuẩn hoá index/size.
    /// </summary>
    public static async Task<PagedResult<T>> PaginateOrderedAsync<T>(
        this IOrderedQueryable<T> ordered, int index, int size, CancellationToken ct = default)
    {
        var safeIndex = index < 1 ? 1 : index;
        var safeSize = size < 1 ? 20 : size;

        var total = await ordered.CountAsync(ct);
        var items = await ordered
            .Skip((safeIndex - 1) * safeSize)
            .Take(safeSize)
            .ToListAsync(ct);

        return new PagedResult<T> { Items = items, Total = total, Index = safeIndex, Size = safeSize };
    }
}
