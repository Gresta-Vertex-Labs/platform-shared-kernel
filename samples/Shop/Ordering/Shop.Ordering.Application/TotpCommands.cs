using SharedKernel.Application.Messaging;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Shop.Ordering.Domain;

namespace Shop.Ordering.Application;

/// <summary>Stores (or replaces) a user's authenticator secret; the column is encrypted at rest.</summary>
public sealed record SaveTotpEnrollmentCommand(string SubjectId, string SecretBase32) : ICommand;

public sealed class SaveTotpEnrollmentHandler(
    IRepository<TotpEnrollment, TotpEnrollmentId> enrollments,
    IClock clock
) : ICommandHandler<SaveTotpEnrollmentCommand>
{
    public async Task<Result> Handle(SaveTotpEnrollmentCommand command, CancellationToken ct)
    {
        string subject = command.SubjectId;
        var existing = await enrollments.FirstOrDefaultAsync(
            Specification<TotpEnrollment>.Create(e => e.SubjectId == subject),
            ct
        );
        if (existing is null)
        {
            await enrollments.AddAsync(
                TotpEnrollment.Create(subject, command.SecretBase32, clock),
                ct
            );
        }
        else
        {
            existing.Replace(command.SecretBase32);
        }

        return Result.Success();
    }
}

/// <summary>A user's authenticator secret (base32), decrypted.</summary>
public sealed record GetTotpSecretQuery(string SubjectId) : IQuery<string>;

public sealed class GetTotpSecretHandler(
    IReadRepository<TotpEnrollment, TotpEnrollmentId> enrollments
) : IQueryHandler<GetTotpSecretQuery, string>
{
    public async Task<Result<string>> Handle(GetTotpSecretQuery query, CancellationToken ct)
    {
        string subject = query.SubjectId;
        var enrollment = await enrollments.FirstOrDefaultAsync(
            Specification<TotpEnrollment>.Create(e => e.SubjectId == subject),
            ct
        );
        return enrollment is null
            ? Result<string>.Failure(
                Error.NotFound("ordering.totp.not_enrolled", "No authenticator app is enrolled.")
            )
            : Result<string>.Success(enrollment.SecretBase32);
    }
}
