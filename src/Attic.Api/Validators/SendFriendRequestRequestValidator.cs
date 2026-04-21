using Attic.Contracts.Friends;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class SendFriendRequestRequestValidator : AbstractValidator<SendFriendRequestRequest>
{
    public SendFriendRequestRequestValidator()
    {
        RuleFor(r => r.Username)
            .NotEmpty()
            .Matches("^[A-Za-z0-9_-]{3,32}$")
            .WithErrorCode("invalid_username");
        RuleFor(r => r.Text).MaximumLength(500).WithErrorCode("text_too_long");
    }
}
