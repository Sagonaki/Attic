using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Infrastructure.Audit;
using Attic.Infrastructure.Auth;
using Attic.Infrastructure.Clock;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Attic.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAtticInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<TimestampInterceptor>();
        services.AddSingleton<Microsoft.AspNetCore.Identity.IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        return services;
    }

    public static IHostApplicationBuilder AddAtticDbContext(this IHostApplicationBuilder builder, string connectionName)
    {
        // Aspire's AddNpgsqlDbContext enables DbContext pooling unconditionally, which
        // forbids setting options inside OnConfiguring. We register the DbContext
        // ourselves (non-pooled, so we can attach a DI-resolved interceptor at options-
        // build time) and then layer Aspire's telemetry / retries / health checks on top
        // via EnrichNpgsqlDbContext.
        var connectionString = builder.Configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException($"Connection string '{connectionName}' was not found.");

        builder.Services.AddScoped<AuditLogContext>();
        builder.Services.AddScoped<AuditLogInterceptor>();

        builder.Services.AddDbContext<AtticDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(
                sp.GetRequiredService<TimestampInterceptor>(),
                sp.GetRequiredService<AuditLogInterceptor>());
        });
        builder.EnrichNpgsqlDbContext<AtticDbContext>();
        return builder;
    }
}
