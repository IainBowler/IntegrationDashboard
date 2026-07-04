namespace Api.Contracts;

public record PageVisitSummaryResponse(IReadOnlyList<PageVisitSummaryItem> Pages);

public record PageVisitSummaryItem(string PagePath, long Count);
