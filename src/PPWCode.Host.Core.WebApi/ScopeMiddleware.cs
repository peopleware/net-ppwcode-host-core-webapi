// Copyright 2024 by PeopleWare n.v..
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Threading.Tasks;

using Castle.MicroKernel.Lifestyle;
using Castle.Windsor;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace PPWCode.Host.Core.WebApi;

/// <summary>
///     The <see cref="ScopeMiddleware" /> initializes both a MS.DI and a Castle Windsor scope. The scope is created and
///     becomes active when the request passes through the middleware, and the scopes are disposed when the response
///     returns back through the middleware.
/// </summary>
/// <remarks>
///     <p>
///         The middleware must be put in the pipeline before the  <see cref="TransactionMiddleware" />.
///     </p>
///     <p>
///         This middleware must be used together with the custom <see cref="ControllerActivator" />.
///     </p>
/// </remarks>
[UsedImplicitly]
public class ScopeMiddleware : IMiddleware
{
    /// <inheritdoc />
    [NotNull]
    public async Task InvokeAsync([NotNull] HttpContext context, [NotNull] RequestDelegate next)
    {
        // retrieve MS DI & CW containers
        IServiceProvider serviceProvider = context.RequestServices;
        IWindsorContainer container = serviceProvider.GetRequiredService<IWindsorContainer>();

        // create both MS DI & CW scopes
        using IServiceScope msScope = serviceProvider.CreateScope();
        using IDisposable scope = container.BeginScope();
        await next(context).ConfigureAwait(false);
    }
}
