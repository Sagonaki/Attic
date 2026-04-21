using Attic.Contracts.Invitations;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class InviteToChannelRequestValidator : AbstractValidator<InviteToChannelRequest>
{
    public InviteToChannelRequestValidator()
    {
        RuleFor(r => r.Username)
            .NotEmpty()
            .Matches("^[A-Za-z0-9_-]{3,32}$")
            .WithErrorCode("invalid_username");
    }
}
