using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TreeEditor.Api.Http;

namespace TreeEditor.Api.Composition;

/// <summary>Fixed-window rate limiting of mutating requests, partitioned by client IP.</summary>
public static class RateLimiting
{
    /// <summary>Name of the policy applied to <c>POST</c> endpoints.</summary>
    public const string MutationsPolicy = "mutations";

    /// <summary>Registers the mutation policy and a ProblemDetails rejection with <c>Retry-After</c>.</summary>
    public static IServiceCollection AddMutationRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(MutationsPolicy, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<ApiOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.MutationPermitLimit,
                        Window = TimeSpan.FromSeconds(options.MutationWindowSeconds),
                        QueueLimit = 0,
                    });
            });
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                return new ValueTask(ApiProblems.WriteAsync(
                    context.HttpContext,
                    ApiProblems.Create(StatusCodes.Status429TooManyRequests, "Too many requests.", null)));
            };
        });
}
