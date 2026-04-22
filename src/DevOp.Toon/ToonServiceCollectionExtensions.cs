#nullable enable
using System;
using Microsoft.Extensions.DependencyInjection;

namespace DevOp.Toon;

/// <summary>
/// Dependency injection registration helpers for TOON services.
/// </summary>
public static class ToonServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IToonService"/> and its options as singletons.
    /// </summary>
    /// <param name="services">The service collection that receives the TOON registrations.</param>
    /// <param name="configure">An optional callback used to configure the shared encode and decode defaults.</param>
    /// <returns>The same service collection so additional registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The configured indentation is less than or equal to zero.</exception>
    public static IServiceCollection AddToon(this IServiceCollection services, Action<ToonServiceOptions>? configure = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        var options = new ToonServiceOptions();
        configure?.Invoke(options);
        Validate(options);

        services.AddSingleton(options);
        services.AddSingleton<IToonService, ToonService>();

        return services;
    }

    private static void Validate(ToonServiceOptions options)
    {
        if (options.Indent <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.Indent, "Indent must be greater than 0.");
    }
}
