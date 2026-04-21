using Attic.Contracts.Friends;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class OpenPersonalChatRequestValidator : AbstractValidator<OpenPersonalChatRequest>
{
    public OpenPersonalChatRequestValidator()
    {
        RuleFor(r => r.Username)
            .NotEmpty()
            .Matches("^[A-Za-z0-9_-]{3,32}$")
            .WithErrorCode("invalid_username");
    }
}
