using System.Reflection;
using Microsoft.AspNetCore.SignalR;

namespace Attic.Api.Hubs;

public sealed class GlobalHubFilter(ILogger<GlobalHubFilter> logger) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            return await next(invocationContext);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub method {Method} threw. correlationId={CorrelationId} user={UserId}",
                invocationContext.HubMethodName,
                correlationId,
                invocationContext.Context.User?.Identity?.Name);

            var returnType = invocationContext.HubMethod.ReturnType;
            // Unwrap Task<T> / ValueTask<T>.
            var underlying = returnType;
            if (returnType.IsGenericType)
            {
                var def = returnType.GetGenericTypeDefinition();
                if (def == typeof(Task<>) || def == typeof(ValueTask<>))
                    underlying = returnType.GetGenericArguments()[0];
            }
            else if (returnType == typeof(Task) || returnType == typeof(ValueTask))
            {
                // Void-returning method — rethrow to let SignalR surface it as a hub error event.
                throw;
            }

            // Object / anonymous — return a generic failure shape.
            if (underlying == typeof(object))
                return new { ok = false, code = "server_error", correlationId };

            // Strongly-typed record with (bool Ok, ..., string? Error) positional pattern.
            var ctor = underlying.GetConstructors(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault();
            if (ctor is null)
            {
                logger.LogWarning("GlobalHubFilter: cannot synthesize failure response for {Type}", underlying);
                throw;
            }

            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.ParameterType == typeof(bool)) args[i] = false;
                else if (p.ParameterType == typeof(string) && p.Name?.Equals("error", StringComparison.OrdinalIgnoreCase) == true)
                    args[i] = "server_error";
                else args[i] = p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) is null
                    ? Activator.CreateInstance(p.ParameterType)
                    : null;
            }

            return ctor.Invoke(args);
        }
    }
}
