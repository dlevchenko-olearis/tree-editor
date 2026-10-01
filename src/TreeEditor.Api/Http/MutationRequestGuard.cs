using Microsoft.Extensions.Options;
using TreeEditor.Api.Composition;

namespace TreeEditor.Api.Http;

/// <summary>
/// Rejects mutating API requests that a cross-site form could send: every <c>POST</c> under <c>/api</c> must have a
/// JSON content type and the <c>X-Requested-With: tree-editor</c> header. Oversized bodies with a declared length
/// are rejected before they are read.
/// </summary>
public sealed class MutationRequestGuard(RequestDelegate next, IOptions<ApiOptions> options, GenerationProbe probe)
{
    /// <summary>Name of the anti-CSRF header.</summary>
    public const string HeaderName = "X-Requested-With";

    /// <summary>Required value of the anti-CSRF header.</summary>
    public const string HeaderValue = "tree-editor";

    /// <summary>Checks the request and passes it on only when it is acceptable.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var rejection = Check(context.Request);
        if (rejection is null)
        {
            await next(context);
            return;
        }

        var generation = await probe.TryReadAsync(context.RequestAborted);
        var (status, title) = rejection.Value;
        await ApiProblems.WriteAsync(context, ApiProblems.Create(status, title, generation));
    }

    /// <summary>Tells whether the guard applies to <paramref name="context"/>.</summary>
    public static bool AppliesTo(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api");

    private (int Status, string Title)? Check(HttpRequest request)
    {
        if (request.ContentLength > options.Value.MaxRequestBodyBytes)
        {
            return (StatusCodes.Status413PayloadTooLarge, "The request body is too large.");
        }

        if (!request.HasJsonContentType())
        {
            return (StatusCodes.Status415UnsupportedMediaType, "The request must have the application/json content type.");
        }

        if (!string.Equals(request.Headers[HeaderName], HeaderValue, StringComparison.Ordinal))
        {
            return (StatusCodes.Status400BadRequest, $"The request must carry the header {HeaderName}: {HeaderValue}.");
        }

        return null;
    }
}
