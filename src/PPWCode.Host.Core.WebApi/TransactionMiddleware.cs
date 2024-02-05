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

using System.Data;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Castle.Core.Logging;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

using NHibernate;

using PPWCode.Server.Core.Transactional;
using PPWCode.Vernacular.Exceptions.IV;
using PPWCode.Vernacular.NHibernate.III.Async.Interfaces.Providers;

using ISession = NHibernate.ISession;

namespace PPWCode.Host.Core.WebApi;

/// <summary>
///     This middleware handles transactions.  The middleware depends on the ScopeMiddleware which must be placed in
///     front of this middleware in the pipeline.
/// </summary>
/// <remarks>
///     <p>
///         The transaction is created, taking into account the TransactionalAttribute that might be placed on the
///         controller or on the action method.
///     </p>
///     <p>
///         The transaction is created when the request passes through the middleware, and before the next middleware in
///         the pipeline is called.
///     </p>
///     <p>
///         The transaction is closed either when ASP.NET Core attempts to start writing the response to the client,
///         or when the response comes back through this middleware; whichever happens earlier.
///     </p>
/// </remarks>
[UsedImplicitly]
public class TransactionMiddleware : IMiddleware
{
    public const string RequestSimulation = "X-REQUEST-SIMULATION";

    [NotNull]
    private ILogger _logger = NullLogger.Instance;

    public TransactionMiddleware([NotNull] ISessionProviderAsync sessionProvider)
    {
        SessionProvider = sessionProvider;
        Session = sessionProvider.Session;
    }

    [NotNull]
    public ISession Session { get; }

    [NotNull]
    public ISessionProviderAsync SessionProvider { get; }

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
    public async Task InvokeAsync([NotNull] HttpContext httpContext, [NotNull] RequestDelegate next)
    {
        Endpoint endPoint = httpContext.GetEndpoint();
        if (endPoint == null)
        {
            // It is possible that no endpoint is found when the backend is presented with a path that is not supported
            // by any controller.  When no endpoint is found, the response will likely be NotFound or another 4xx status
            // and in that case no transaction handling is done.
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        ControllerActionDescriptor controllerActionDescriptor = endPoint.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (controllerActionDescriptor == null)
        {
            // It is possible that an endpoint is found, but that no ControllerActionDescriptor is found.  This could be
            // the case for a path that is supported for a number of HTTP verbs, but is called with another HTTP verb.
            // This is likely an internal endpoint added by ASP.NET Core.  When this is the case, the response will
            // likely be a 4xx status and in that case no transaction handling is done.
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        TransactionalAttribute transactionalAttribute = endPoint.Metadata.GetMetadata<TransactionalAttribute>();
        ITransaction transaction = InitiateTransaction(controllerActionDescriptor, transactionalAttribute);
        if (transaction == null)
        {
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        httpContext.Response.OnStarting(() => CloseTransactionAsync(httpContext, transaction));
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        finally
        {
            await CloseTransactionAsync(httpContext, transaction).ConfigureAwait(false);
        }
    }

    [CanBeNull]
    protected virtual ITransaction InitiateTransaction(
        [NotNull] ControllerActionDescriptor controllerActionDescriptor,
        [CanBeNull] TransactionalAttribute transactionalAttribute)
    {
        Logger.Info(() => $"Determine if we should use transactions using attribute {nameof(TransactionalAttribute)}");

        string displayName = controllerActionDescriptor.DisplayName;
        IsolationLevel isolationLevel = transactionalAttribute?.IsolationLevel ?? IsolationLevel.Unspecified;

        if (transactionalAttribute is { Transactional: true })
        {
            if (!Session.IsOpen)
            {
                throw new ProgrammingError($"{displayName} Current session is not opened.");
            }

            Logger.Info(() => $"{displayName} Start our request transaction, with isolation level {isolationLevel}.");
            return Session.BeginTransaction(transactionalAttribute.IsolationLevel);
        }

        Logger.Info(() => $"{displayName} No transaction is requested.");
        return null;
    }

    [NotNull]
    protected virtual async Task CloseTransactionAsync(
        [NotNull] HttpContext httpContext,
        [NotNull] ITransaction transaction)
    {
        if (transaction.IsActive)
        {
            CancellationToken cancellationToken = httpContext.RequestAborted;
            bool shouldRollback =
                !IsSuccessStatusCode(httpContext)
                || httpContext.Request.Headers.ContainsKey(RequestSimulation)
                || cancellationToken.IsCancellationRequested;
            if (shouldRollback)
            {
                // A rollback shouldn't be canceled!
                cancellationToken = CancellationToken.None;
                await OnRollbackAsync(httpContext, cancellationToken).ConfigureAwait(false);
                await SessionProvider
                    .SafeEnvironmentProviderAsync
                    .RunAsync(
                        nameof(ITransaction.RollbackAsync),
                        can => transaction.RollbackAsync(can),
                        cancellationToken)
                    .ConfigureAwait(false);
                await OnAfterRollbackAsync(httpContext, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await OnCommitAsync(httpContext, cancellationToken).ConfigureAwait(false);
                await SessionProvider
                    .SafeEnvironmentProviderAsync
                    .RunAsync(
                        nameof(ITransaction.CommitAsync),
                        can => transaction.CommitAsync(can),
                        cancellationToken)
                    .ConfigureAwait(false);
                await OnAfterCommitAsync(httpContext, cancellationToken).ConfigureAwait(false);
            }
        }
    }
    
    protected virtual bool IsSuccessStatusCode([NotNull] HttpContext httpContext)
    {
        int statusCode = httpContext.Response.StatusCode;
        return statusCode is >= (int)HttpStatusCode.OK and <= 299;
    }
    
    [NotNull]
    protected virtual Task OnCommitAsync([NotNull] HttpContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    [NotNull]
    protected virtual Task OnAfterCommitAsync([NotNull] HttpContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;

    [NotNull]
    protected virtual Task OnRollbackAsync([NotNull] HttpContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;
    
    [NotNull]
    protected virtual Task OnAfterRollbackAsync([NotNull] HttpContext context, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
