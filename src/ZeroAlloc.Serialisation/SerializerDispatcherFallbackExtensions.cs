using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace ZeroAlloc.Serialisation;

/// <summary>
/// DI extension methods for opting in to <see cref="SystemTextJsonFallbackDispatcher"/>.
/// </summary>
public static class SerializerDispatcherFallbackExtensions
{
    /// <summary>
    /// Wraps the already-registered <see cref="ISerializerDispatcher"/> with a
    /// <see cref="SystemTextJsonFallbackDispatcher"/> so that types not covered by the
    /// source-generated dispatcher are serialized via <c>System.Text.Json</c>.
    /// </summary>
    /// <remarks>
    /// <strong>Call after</strong> <c>AddSerializerDispatcher()</c>. Throws
    /// <see cref="InvalidOperationException"/> if no <see cref="ISerializerDispatcher"/>
    /// registration is found — this is intentional so that a missing
    /// <c>AddSerializerDispatcher()</c> call fails loudly rather than silently.
    /// </remarks>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="options">
    /// Optional <see cref="JsonSerializerOptions"/> forwarded to the fallback path.
    /// <see langword="null"/> uses the default <c>System.Text.Json</c> options.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "SystemTextJsonFallbackDispatcher uses reflection-based System.Text.Json APIs.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "SystemTextJsonFallbackDispatcher uses reflection-based System.Text.Json APIs.")]
    public static IServiceCollection WithSystemTextJsonFallback(
        this IServiceCollection services,
        JsonSerializerOptions? options = null)
    {
        var descriptor = services.LastOrDefault(
            d => d.ServiceType == typeof(ISerializerDispatcher))
            ?? throw new InvalidOperationException(
                "No ISerializerDispatcher registration found. " +
                "Call AddSerializerDispatcher() before WithSystemTextJsonFallback().");

        services.Remove(descriptor);
        services.AddSingleton<ISerializerDispatcher>(sp =>
        {
            var inner = descriptor switch
            {
                { ImplementationInstance: { } inst }    => (ISerializerDispatcher)inst,
                { ImplementationFactory:  { } factory } => (ISerializerDispatcher)factory(sp),
                _                                       => (ISerializerDispatcher)
                    ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!)
            };
            return new SystemTextJsonFallbackDispatcher(inner, options);
        });

        return services;
    }
}
