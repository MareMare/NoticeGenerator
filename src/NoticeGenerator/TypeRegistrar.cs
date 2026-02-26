// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TypeRegistrar.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace NoticeGenerator;

/// <summary>
/// Microsoft.Extensions.DependencyInjection を Spectre.Console.Cli の
/// ITypeRegistrar / ITypeResolver に橋渡しする実装。
/// </summary>
internal sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    private ServiceProvider? _provider;

    public ITypeResolver Build()
    {
        this._provider = services.BuildServiceProvider();
        return new TypeResolver(this._provider);
    }

    public void Register(Type service, Type implementation) =>
        services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) =>
        services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) =>
        services.AddSingleton(service, _ => factory());
}

internal sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    public object? Resolve(Type? type) =>
        type is null ? null : provider.GetService(type);

    public void Dispose()
    {
        if (provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
