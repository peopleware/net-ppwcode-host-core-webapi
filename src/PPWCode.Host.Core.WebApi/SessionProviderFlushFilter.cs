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

using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Castle.Core.Logging;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

using NHibernate;

using PPWCode.Vernacular.Exceptions.IV;
using PPWCode.Vernacular.NHibernate.III.Async.Interfaces.Providers;

namespace PPWCode.Host.Core.WebApi;

/// <summary>
///     The <see cref="SessionProviderFlushFilter" /> is an <see cref="IAsyncActionFilter" /> that executes a <c>Flush</c>
///     on the NHibernate <see cref="NHibernate.ISession" /> right after the action method is executed.
/// </summary>
/// <remarks>
///     <p>
///         Note that this action filter is best placed as the first action filter: no database actions should be performed
///         after the execution (in the 'after' part) of this action filter.
///     </p>
///     <p>
///         Note that the <c>Flush</c> is only executed if the request is not cancelled and if the response has a success
///         status code.
///     </p>
/// </remarks>
[UsedImplicitly]
public class SessionProviderFlushFilter : IAsyncActionFilter
{
    [NotNull]
    private ILogger _logger = NullLogger.Instance;

    public SessionProviderFlushFilter([NotNull] ISessionProviderAsync sessionProviderAsync)
    {
        SessionProviderAsync = sessionProviderAsync;
    }

    [NotNull]
    public ISessionProviderAsync SessionProviderAsync { get; }

    [UsedImplicitly]
    [NotNull]
    public ILogger Logger
    {
        get => _logger;
        set
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (value != null)
            {
                _logger = value;
            }
        }
    }

    /// <inheritdoc />
    [NotNull]
    public async Task OnActionExecutionAsync([NotNull] ActionExecutingContext context, [NotNull] ActionExecutionDelegate next)
    {
        if (!SessionProviderAsync.Session.IsOpen)
        {
            throw new ProgrammingError($"{ActionContextDisplayName(context)} Current session is not opened.");
        }

        ActionExecutedContext executedContext = await next().ConfigureAwait(false);

        // We will only flush our pending action if and only if:
        // * No cancellation was requested
        // * we have still a success code
        // * no exception was thrown by the endpoint
        CancellationToken cancellationToken = executedContext.HttpContext.RequestAborted;
        if (!cancellationToken.IsCancellationRequested
            && IsSuccessStatusCode(executedContext.HttpContext)
            && (executedContext.Exception == null))
        {
            ITransaction currentTransaction = SessionProviderAsync.Session.GetCurrentTransaction();
            if (currentTransaction?.IsActive == true)
            {
                Logger.Info(() => $"{ActionContextDisplayName(context)} Flush our request to the database.");
                await SessionProviderAsync.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [NotNull]
    protected virtual string ActionContextDisplayName([NotNull] FilterContext context)
        => context.ActionDescriptor.DisplayName;

    protected virtual bool IsSuccessStatusCode([NotNull] HttpContext httpContext)
    {
        int statusCode = httpContext.Response.StatusCode;
        return statusCode is >= (int)HttpStatusCode.OK and <= 299;
    }
}
