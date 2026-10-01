using Microsoft.AspNetCore.Mvc;

namespace TreeEditor.Api.Http;

/// <summary>Builds ProblemDetails responses that carry the tree generation.</summary>
public static class ApiProblems
{
    /// <summary>Name of the ProblemDetails extension holding the generation.</summary>
    public const string GenerationKey = "generation";

    /// <summary>A problem with the given status, title, and optional detail.</summary>
    public static ProblemDetails Create(int status, string title, long? generation, string? detail = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        AddGeneration(problem, generation);
        return problem;
    }

    /// <summary>A 400 problem that lists validation errors by request path.</summary>
    public static HttpValidationProblemDetails Validation(
        IDictionary<string, string[]> errors,
        long? generation,
        string title = "The request is invalid.")
    {
        var problem = new HttpValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest, Title = title };
        AddGeneration(problem, generation);
        return problem;
    }

    /// <summary>Wraps a problem into a result with the problem's status code.</summary>
    public static IResult Result(ProblemDetails problem) => TypedResults.Problem(problem);

    /// <summary>Writes a problem to a response that has not started yet.</summary>
    public static Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
        }).AsTask();
    }

    private static void AddGeneration(ProblemDetails problem, long? generation)
    {
        if (generation is { } value)
        {
            problem.Extensions[GenerationKey] = value;
        }
    }
}
