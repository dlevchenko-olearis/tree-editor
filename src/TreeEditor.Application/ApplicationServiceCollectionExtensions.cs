using Microsoft.Extensions.DependencyInjection;
using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Changes;
using TreeEditor.Application.Decorators;
using TreeEditor.Application.Nodes;
using TreeEditor.Application.Reset;

namespace TreeEditor.Application;

/// <summary>Registers the use-case handlers of the application layer.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers query and command handlers. Command handlers are decorated, from outermost to innermost, with
    /// logging, transaction, and (for Apply) idempotency.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<GetRootsQuery, NodePageResult>, GetRootsHandler>();
        services.AddScoped<IQueryHandler<GetChildrenQuery, NodePageResult>, GetChildrenHandler>();
        services.AddScoped<IQueryHandler<GetNodeQuery, NodeResult>, GetNodeHandler>();
        services.AddScoped<IQueryHandler<GetGenerationQuery, long>, GetGenerationHandler>();

        services.AddScoped<ICommandHandler<ApplyChangesCommand, ApplyOutcome>, ApplyChangesHandler>();
        services.AddScoped<ICommandHandler<ResetTreeCommand, ResetOutcome>, ResetTreeHandler>();

        services.Decorate<ICommandHandler<ApplyChangesCommand, ApplyOutcome>, IdempotencyDecorator>();
        services.Decorate(typeof(ICommandHandler<,>), typeof(TransactionDecorator<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator<,>));
        return services;
    }
}
