using Attic.Contracts.Auth;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().WithErrorCode("invalid_email");
    }
}
