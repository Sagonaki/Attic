using System.Text;
using Attic.Contracts.Messages;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(r => r.ChannelId).NotEmpty();
        RuleFor(r => r.ClientMessageId).NotEmpty();
        RuleFor(r => r.Content)
            .NotEmpty()
            .Must(c => Encoding.UTF8.GetByteCount(c) <= 3072)
            .WithMessage("Message content exceeds 3 KB.");
    }
}
