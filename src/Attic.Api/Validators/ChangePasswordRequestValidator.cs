using Attic.Contracts.Auth;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty().WithErrorCode("current_required");
        RuleFor(r => r.NewPassword).NotEmpty().MinimumLength(8).WithErrorCode("weak_password");
    }
}
