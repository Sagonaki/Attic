using Attic.Contracts.Auth;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountRequestValidator()
    {
        RuleFor(r => r.Password).NotEmpty().WithErrorCode("password_required");
    }
}
