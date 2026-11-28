using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Web.Extensions;

/// <summary>
/// Turns a use case's Ardalis <see cref="Result{T}"/> into the HTTP response: the value on success, otherwise a
/// problem-details response with the result's messages as <c>detail</c>. The typed results keep the response type in
/// the OpenAPI document (Ok&lt;T&gt; is 200 with T, Created&lt;T&gt; is 201 with T).
/// </summary>
public static class ResultExtensions
{
    public const string ProblemsKey = "problems";

    public static Results<Ok<T>, ProblemHttpResult> ToOkResult<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem();

    public static Results<Created<T>, ProblemHttpResult> ToCreatedResult<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : result.ToProblem();

    public static Results<NoContent, ProblemHttpResult> ToNoContentResult(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.ToProblem();

    /// <summary>
    /// The problem response. DynamicFields' problems with a content, as Content refused with them
    /// (<see cref="ProblemErrors"/>), are 422 with a <c>problems</c> list, as Content answers them.
    /// </summary>
    public static ProblemHttpResult ToProblem(this Ardalis.Result.IResult result) =>
        ProblemErrors.Of(result.ValidationErrors) is { Count: > 0 } problems
            ? TypedResults.Problem(detail: Detail(result), statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { [ProblemsKey] = problems })
            : TypedResults.Problem(detail: Detail(result), statusCode: result.Status switch
        {
            ResultStatus.NotFound => StatusCodes.Status404NotFound,
            ResultStatus.Forbidden => StatusCodes.Status403Forbidden,
            ResultStatus.Unauthorized => StatusCodes.Status401Unauthorized,
            ResultStatus.Conflict => StatusCodes.Status409Conflict,
            ResultStatus.Invalid => StatusCodes.Status400BadRequest,
            ResultStatus.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        });

    private static string? Detail(Ardalis.Result.IResult result)
    {
        var messages = result.Errors.Concat(result.ValidationErrors.Select(e => e.ErrorMessage)).ToArray();
        return messages.Length == 0 ? null : string.Join("; ", messages);
    }
}
