using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Infrastructure.Auth;
using Attic.Infrastructure.Clock;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
        builder.AddNpgsqlDbContext<AtticDbContext>(connectionName, configureDbContextOptions: options =>
        {
            options.UseSnakeCaseNamingConvention();
        });
        return builder;
    }
}
