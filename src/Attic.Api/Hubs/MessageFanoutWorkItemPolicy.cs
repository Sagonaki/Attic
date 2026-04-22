using Microsoft.Extensions.ObjectPool;

namespace Attic.Api.Hubs;

internal sealed class MessageFanoutWorkItemPolicy : PooledObjectPolicy<MessageFanoutWorkItem>
{
    public override MessageFanoutWorkItem Create() => new();

    public override bool Return(MessageFanoutWorkItem obj)
    {
        obj.Reset();
        return true;
    }
}
