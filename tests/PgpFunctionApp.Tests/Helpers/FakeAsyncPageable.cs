using Azure;
using Moq;

namespace PgpFunctionApp.Tests.Helpers;

/// <summary>
/// A test double for <see cref="AsyncPageable{T}"/> that yields items from an in-memory list.
/// </summary>
internal sealed class FakeAsyncPageable<T>(IEnumerable<T> items) : AsyncPageable<T> where T : notnull
{
    private readonly IReadOnlyList<T> _items = items.ToList().AsReadOnly();

    public override async IAsyncEnumerable<Page<T>> AsPages(
        string? continuationToken = null,
        int? pageSizeHint = null)
    {
        yield return Page<T>.FromValues(_items, continuationToken: null, response: Mock.Of<Response>());
        await Task.CompletedTask;
    }
}
