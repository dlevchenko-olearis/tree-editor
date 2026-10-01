using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;
using TreeEditor.Api.Endpoints;
using TreeEditor.Api.Http;
using TreeEditor.Application;
using TreeEditor.Infrastructure;

namespace TreeEditor.Api.Composition;

/// <summary>Composition root: wires every layer and builds the HTTP pipeline.</summary>
public static class ApiComposition
{
    /// <summary>Registers application, infrastructure, and HTTP services from configuration.</summary>
    public static WebApplicationBuilder AddTreeEditor(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var apiOptions = configuration.GetSection(ApiOptions.Section).Get<ApiOptions>() ?? new ApiOptions();
        builder.Services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.Section));
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = apiOptions.MaxRequestBodyBytes);

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(new InfrastructureOptions
        {
            ConnectionString = configuration.GetConnectionString("TreeEditor")
                ?? throw new InvalidOperationException("The connection string 'TreeEditor' is not configured."),
        });

        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddProblemDetails(problems => problems.CustomizeProblemDetails = context =>
        {
            if (builder.Environment.IsDevelopment() && context.Exception is { } exception)
            {
                context.ProblemDetails.Detail = exception.ToString();
            }
        });
        builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();
        builder.Services.AddSingleton<GenerationProbe>();
        builder.Services.AddMutationRateLimiting();
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
        builder.Services.AddOpenApi();
        return builder;
    }

    /// <summary>
    /// Builds the middleware pipeline and maps the endpoints. No CORS policy is registered. The mutation guard runs
    /// before the rate limiter, so requests a cross-site page can forge never use up a client's quota.
    /// </summary>
    public static WebApplication UseTreeEditor(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(WriteStatusProblemAsync);
        app.UseWhen(MutationRequestGuard.AppliesTo, guarded => guarded.UseMiddleware<MutationRequestGuard>());
        app.UseRateLimiter();

        app.MapOpenApi();
        app.MapHealthChecks("/health");
        app.MapNodesEndpoints();
        app.MapChangesEndpoints();
        app.MapResetEndpoints();
        return app;
    }

    private static async Task WriteStatusProblemAsync(StatusCodeContext context)
    {
        var http = context.HttpContext;
        var status = http.Response.StatusCode;
        var generation = await http.RequestServices.GetRequiredService<GenerationProbe>().TryReadAsync(http.RequestAborted);
        await ApiProblems.WriteAsync(http, ApiProblems.Create(status, ReasonPhrases.GetReasonPhrase(status), generation));
    }
}
