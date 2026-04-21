using Attic.Contracts.Channels;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class UpdateChannelRequestValidator : AbstractValidator<UpdateChannelRequest>
{
    public UpdateChannelRequestValidator()
    {
        RuleFor(r => r.Name)
            .Matches("^[A-Za-z0-9_\\- ]{3,120}$").When(r => r.Name is not null)
            .WithErrorCode("invalid_name");
        RuleFor(r => r.Description).MaximumLength(1024).WithErrorCode("description_too_long");
    }
}
