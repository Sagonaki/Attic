using Attic.Contracts.Channels;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class CreateChannelRequestValidator : AbstractValidator<CreateChannelRequest>
{
    public CreateChannelRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().Matches("^[A-Za-z0-9_\\- ]{3,120}$").WithErrorCode("invalid_name");
        RuleFor(r => r.Description).MaximumLength(1024).WithErrorCode("description_too_long");
        RuleFor(r => r.Kind)
            .Must(k => k == "public" || k == "private")
            .WithErrorCode("invalid_kind");
    }
}
