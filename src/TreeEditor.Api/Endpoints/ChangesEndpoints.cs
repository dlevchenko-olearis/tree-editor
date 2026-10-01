using Microsoft.AspNetCore.Mvc;
using TreeEditor.Api.Composition;
using TreeEditor.Api.Http;
using TreeEditor.Application.Abstractions;
using TreeEditor.Application.Changes;
using TreeEditor.Application.Nodes;
using TreeEditor.Domain.Changes;

namespace TreeEditor.Api.Endpoints;

/// <summary>The Apply endpoint. The change set is validated here, before any handler or database access.</summary>
public static class ChangesEndpoints
{
    /// <summary>Header carrying the idempotency key of an Apply attempt.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Maps <c>POST /api/changes</c>.</summary>
    public static IEndpointRouteBuilder MapChangesEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/changes", ApplyAsync)
            .WithTags("Changes")
            .RequireRateLimiting(RateLimiting.MutationsPolicy);
        return routes;
    }

    private static async Task<IResult> ApplyAsync(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        ApplyRequest? request,
        ICommandHandler<ApplyChangesCommand, ApplyOutcome> handler,
        IQueryHandler<GetGenerationQuery, long> generation,
        CancellationToken cancellationToken)
    {
        var errors = ReadEnvelope(idempotencyKey, request, out var key);
        if (errors.Count > 0)
        {
            var current = await generation.HandleAsync(new GetGenerationQuery(), cancellationToken);
            return ApiProblems.Result(ApiProblems.Validation(errors, current));
        }

        if (!ChangeSet.TryCreate(
            (request!.Created ?? []).Select(node => new NodeCreationDraft(node.Id, node.ParentId, node.Value)).ToList(),
            (request.Updated ?? []).Select(edit => new NodeEditDraft(edit.Id, edit.Value, edit.ExpectedVersion ?? 0)).ToList(),
            request.Deleted ?? [],
            out var changeSet,
            out var setErrors))
        {
            var current = await generation.HandleAsync(new GetGenerationQuery(), cancellationToken);
            return ToResult(new ApplyOutcome.Invalid(current, setErrors));
        }

        var outcome = await handler.HandleAsync(
            new ApplyChangesCommand(key, request.Generation!.Value, changeSet),
            cancellationToken);
        return ToResult(outcome);
    }

    private static Dictionary<string, string[]> ReadEnvelope(string? idempotencyKey, ApplyRequest? request, out Guid key)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Guid.TryParse(idempotencyKey, out key) || key == Guid.Empty)
        {
            errors[IdempotencyKeyHeader] = ["A UUID idempotency key header is required."];
        }

        if (request is null)
        {
            errors["body"] = ["A JSON change set is required."];
            return errors;
        }

        if (request.Generation is null)
        {
            errors["generation"] = ["The tree generation the change set was built on is required."];
        }

        AddNullItemErrors(errors, "created", request.Created);
        AddNullItemErrors(errors, "updated", request.Updated);
        return errors;
    }

    private static void AddNullItemErrors<T>(Dictionary<string, string[]> errors, string name, IReadOnlyList<T?>? items)
        where T : class
    {
        for (var index = 0; index < (items?.Count ?? 0); index++)
        {
            if (items![index] is null)
            {
                errors[$"{name}[{index}]"] = ["Must be an object."];
            }
        }
    }

    private static IResult ToResult(ApplyOutcome outcome) => outcome switch
    {
        ApplyOutcome.Applied applied => TypedResults.Ok(new ApplyResponse(
            applied.Generation,
            applied.Created.Select(node => new VersionResponse(node.Id.Value, node.Version.Value)).ToList(),
            applied.Updated.Select(node => new VersionResponse(node.Id.Value, node.Version.Value)).ToList(),
            applied.Deleted.Select(id => id.Value).ToList())),
        ApplyOutcome.Conflicted conflicted => TypedResults.Conflict(new ConflictResponse(
            conflicted.Generation,
            conflicted.Conflicts
                .Select(conflict => new ConflictItemResponse(
                    conflict.Id?.Value,
                    conflict.Kind,
                    conflict.CurrentVersion?.Value,
                    conflict.ParentId?.Value))
                .ToList())),
        ApplyOutcome.Invalid { Errors: [{ Code: ChangeSetErrorCode.TooManyOperations }] } invalid =>
            ApiProblems.Result(ApiProblems.Create(
                StatusCodes.Status413PayloadTooLarge,
                "The change set is too large.",
                invalid.Generation,
                $"A change set may contain at most {ChangeSet.MaxOperations} operations.")),
        ApplyOutcome.Invalid invalid => ApiProblems.Result(ApiProblems.Validation(
            invalid.Errors
                .GroupBy(error => error.Path)
                .ToDictionary(group => group.Key, group => group.Select(error => Describe(error.Code)).ToArray()),
            invalid.Generation,
            "The change set is invalid.")),
        ApplyOutcome.KeyMismatch mismatch => ApiProblems.Result(ApiProblems.Create(
            StatusCodes.Status422UnprocessableEntity,
            "The idempotency key was already used for a different change set.",
            mismatch.Generation)),
        _ => throw new InvalidOperationException($"Unknown Apply outcome {outcome.GetType().Name}."),
    };

    private static string Describe(ChangeSetErrorCode code) => code switch
    {
        ChangeSetErrorCode.InvalidId => "Must be a version 7 UUID.",
        ChangeSetErrorCode.MissingParent => "A new node needs a parent; new roots cannot be created.",
        ChangeSetErrorCode.ForwardReference => "The parent must exist or be a new node listed earlier.",
        ChangeSetErrorCode.DuplicateId => "The id appears more than once.",
        ChangeSetErrorCode.CreatedAndChanged => "A new node cannot also be edited or deleted.",
        ChangeSetErrorCode.EmptyValue => "The value must not be empty.",
        ChangeSetErrorCode.ValueTooLong => "The value must be at most 200 characters.",
        ChangeSetErrorCode.ValueHasSurroundingWhitespace => "The value must not start or end with whitespace.",
        ChangeSetErrorCode.ValueHasNullCharacter => "The value must not contain the NUL character (U+0000).",
        ChangeSetErrorCode.InvalidVersion => "The expected version must be a positive number.",
        _ => $"A change set may contain at most {ChangeSet.MaxOperations} operations.",
    };
}
