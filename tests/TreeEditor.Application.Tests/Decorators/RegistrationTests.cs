using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Changes;
using TreeEditor.Application.Decorators;
using TreeEditor.Application.Ports;
using TreeEditor.Application.Reset;
using TreeEditor.Application.Tests.Fakes;

namespace TreeEditor.Application.Tests.Decorators;

public sealed class RegistrationTests
{
    [Fact]
    public void Apply_is_decorated_with_logging_then_transaction_then_idempotency()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var logging = Assert.IsType<LoggingDecorator<ApplyChangesCommand, ApplyOutcome>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<ApplyChangesCommand, ApplyOutcome>>());
        var transaction = Assert.IsType<TransactionDecorator<ApplyChangesCommand, ApplyOutcome>>(logging.Inner);
        var idempotency = Assert.IsType<IdempotencyDecorator>(transaction.Inner);
        Assert.IsType<ApplyChangesHandler>(idempotency.Inner);
    }

    [Fact]
    public void Reset_is_decorated_with_logging_then_transaction()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var logging = Assert.IsType<LoggingDecorator<ResetTreeCommand, ResetOutcome>>(
            scope.ServiceProvider.GetRequiredService<ICommandHandler<ResetTreeCommand, ResetOutcome>>());
        var transaction = Assert.IsType<TransactionDecorator<ResetTreeCommand, ResetOutcome>>(logging.Inner);
        Assert.IsType<ResetTreeHandler>(transaction.Inner);
    }

    private static ServiceProvider BuildProvider()
    {
        var tree = new InMemoryTree();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new CapturingLoggerFactory());
        services.AddSingleton<ITreeReader>(tree);
        services.AddSingleton<ITreeWriter>(tree);
        services.AddSingleton<IUnitOfWork>(new RecordingUnitOfWork());
        services.AddSingleton<IIdempotencyStore>(new InMemoryIdempotencyStore());
        services.AddSingleton<ISampleTreeSource>(new EmptySource());
        services.AddApplication();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private sealed class EmptySource : ISampleTreeSource
    {
        public IReadOnlyList<SeedNode> GetNodes() => [];
    }
}
