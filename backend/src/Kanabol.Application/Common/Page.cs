using System.Text.Json.Serialization;

namespace Kanabol.Application.Common;

/// <summary>Number has that name because a member can't share its type's, and the API calls it "page" (SPEC section 7.3).</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, [property: JsonPropertyName("page")] int Number, int PageSize, int TotalCount);
