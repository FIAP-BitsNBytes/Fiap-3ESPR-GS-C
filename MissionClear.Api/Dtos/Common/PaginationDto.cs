namespace MissionClear.Api.Dtos.Common;

public sealed record PaginationDto(int Page, int Limit, int Total, int TotalPages)
{
    public static PaginationDto From(int page, int limit, int total)
    {
        var safeLimit = Math.Max(1, limit);
        return new(page, safeLimit, total, (int)Math.Ceiling(total / (double)safeLimit));
    }
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Data, PaginationDto Pagination);
