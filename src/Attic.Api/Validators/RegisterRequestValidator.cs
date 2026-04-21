using Attic.Contracts.Auth;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(r => r.Username).NotEmpty().Matches(@"^[A-Za-z0-9_-]{3,32}$");
        RuleFor(r => r.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}
