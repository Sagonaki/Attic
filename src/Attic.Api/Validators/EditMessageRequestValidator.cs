using System.Text;
using Attic.Contracts.Messages;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class EditMessageRequestValidator : AbstractValidator<EditMessageRequest>
{
    public EditMessageRequestValidator()
    {
        RuleFor(r => r.MessageId).GreaterThan(0).WithErrorCode("invalid_message_id");
        RuleFor(r => r.Content)
            .NotEmpty().WithErrorCode("empty_content")
            .Must(c => Encoding.UTF8.GetByteCount(c) <= 3072).WithErrorCode("content_too_large");
    }
}
