using System.Text;
using Attic.Contracts.Messages;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(r => r.ChannelId).NotEmpty().WithErrorCode("invalid_channel");
        RuleFor(r => r.ClientMessageId).NotEmpty().WithErrorCode("invalid_client_message_id");
        RuleFor(r => r.Content).NotEmpty().WithErrorCode("empty_content");
        RuleFor(r => r.Content)
            .Must(c => c is null || Encoding.UTF8.GetByteCount(c) <= 3072)
            .WithErrorCode("content_too_large");
    }
}
