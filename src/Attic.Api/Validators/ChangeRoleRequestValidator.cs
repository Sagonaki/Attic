using Attic.Contracts.Channels;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator()
    {
        RuleFor(r => r.Role)
            .Must(r => r == "admin" || r == "member")
            .WithErrorCode("invalid_role");
    }
}
