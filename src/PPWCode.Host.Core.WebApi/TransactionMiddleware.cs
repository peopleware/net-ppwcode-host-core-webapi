// Copyright 2026 by PeopleWare n.v..
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
using System.Data;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging;

using NHibernate;

using PPWCode.Server.Core.Transactional;
using PPWCode.Vernacular.Exceptions.IV;
using PPWCode.Vernacular.NHibernate.III.Async.Interfaces.Providers;

using ISession = NHibernate.ISession;

namespace PPWCode.Host.Core.WebApi;

/// <summary>
///     The <see cref="TransactionMiddleware" /> handles transactions.
/// </summary>
/// <remarks>
///     <p>
///         The transaction is created, taking into account the <see cref="TransactionalAttribute" /> that might be placed
///         on the controller or on the action method.
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
public class TransactionMiddleware([NotNull] ISessionProviderAsync sessionProvider)
    : IMiddleware
{
    public const string RequestSimulation = "X-REQUEST-SIMULATION";

    private bool _isTransactionClosed = false;

    [CanBeNull]
    private ILogger _logger;

    [NotNull]
    public ISession Session { get; } = sessionProvider.Session;

    [NotNull]
    public ISessionProviderAsync SessionProvider { get; } = sessionProvider;

    [NotNull]
    public ILogger Logger
        => _logger ??= PPWLogging.GetLogger(GetType());

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
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
        if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation($"Determine if we should use transactions using attribute {nameof(TransactionalAttribute)}");
        }

        string displayName = controllerActionDescriptor.DisplayName;
        IsolationLevel isolationLevel = transactionalAttribute?.IsolationLevel ?? IsolationLevel.Unspecified;

        if (transactionalAttribute is { Transactional: true })
        {
            if (!Session.IsOpen)
            {
                throw new ProgrammingError($"{displayName} Current session is not opened.");
            }

            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation(
                    "{ActionContext} Start our request transaction, with isolation level {IsolationLevel}",
                    displayName,
                    isolationLevel);
            }

            ITransaction transaction = Session.BeginTransaction(transactionalAttribute.IsolationLevel);
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("Created transaction {TransactionHashCode}", transaction.GetHashCode());
            }

            return transaction;
        }

        if (Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation(
                "{ActionContext} No transaction is requested",
                displayName);
        }

        return null;
    }

    /// <summary>
    ///     This closes the active <see cref="ITransaction"/>, if there is one, and if there was no earlier attempt
    ///     to close the transaction.
    /// </summary>
    /// <param name="httpContext">the given <see cref="HttpContext"/></param>
    /// <param name="transaction">the given <see cref="ITransaction"/></param>
    /// <returns>
    ///     A <see cref="Task"/> representing the asynchronous action.
    /// </returns>
    [NotNull]
    protected virtual async Task CloseTransactionAsync(
        [NotNull] HttpContext httpContext,
        [NotNull] ITransaction transaction)
    {
        if (_isTransactionClosed)
        {
            if (Logger.IsEnabled(LogLevel.Information))
            {
                Logger.LogInformation("Transaction {TransactionHashCode} is already closed; no further action performed", transaction.GetHashCode());
            }

            return;
        }

        // mark the transaction as handled before handling it as this prevents recursive calls
        _isTransactionClosed = true;

        // only do something when the transaction is still active
        if (transaction.IsActive)
        {
            // decide whether a rollback is needed
            CancellationToken cancellationToken = httpContext.RequestAborted;
            bool shouldRollback =
                !IsSuccessStatusCode(httpContext)
                || httpContext.Request.Headers.ContainsKey(RequestSimulation)
                || cancellationToken.IsCancellationRequested;
            if (shouldRollback)
            {
                // A rollback shouldn't be canceled!
                cancellationToken = CancellationToken.None;
                await HandleRollbackAsync(httpContext, transaction, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Decided to go through with a commit: do not cancel once started
                cancellationToken = CancellationToken.None;
                await HandleCommitAsync(httpContext, transaction, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     This method does a best-effort attempt to roll back the given transaction.  Note that two hooks are provided:
    ///     <see cref="OnRollbackAsync"/> and <see cref="OnAfterRollbackAsync"/>.  The former is always called before
    ///     the rollback is executed, and the latter is always called after the rollback was executed, whether that
    ///     execution was successful or not.
    /// </summary>
    /// <remarks>
    ///     The method performs the rollback on a best-effort basis: when something goes wrong, the exception is properly
    ///     logged, but the exception is silenced: the calling code will not see any exception originating from the
    ///     actual rollback.  Exceptions thrown by any of the hooks <see cref="OnRollbackAsync"/> or
    ///     <see cref="OnAfterRollbackAsync"/> are logged, but also silenced!
    /// </remarks>
    /// <param name="httpContext">the given <see cref="HttpContext"/></param>
    /// <param name="transaction">the given <see cref="ITransaction"/></param>
    /// <param name="cancellationToken">the given <see cref="CancellationToken"/></param>
    /// <returns>
    ///     A <see cref="Task"/> representing the asynchronous action.
    /// </returns>
    protected async Task HandleRollbackAsync(
        [NotNull] HttpContext httpContext,
        [NotNull] ITransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            // hook before rollback
            await OnRollbackAsync(httpContext, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // log, but swallow exception
            Logger.LogError(e, "OnRollbackAsync hook failed with exception: exception is logged but swallowed");
        }

        try
        {
            // execute rollback
            await SessionProvider
                .SafeEnvironmentProviderAsync
                .RunAsync(
                    nameof(ITransaction.RollbackAsync),
                    transaction.RollbackAsync,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // log, but swallow exception
            Logger.LogError(e, "Actual rollback failed with exception: exception is logged but swallowed");
        }

        try
        {
            // hook after rollback
            await OnAfterRollbackAsync(httpContext, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // log, but swallow exception
            Logger.LogError(e, "OnAfterRollbackAsync hook failed with exception: exception is logged but swallowed");
        }
    }

    /// <summary>
    ///     This method handles the commit of the given transaction.  Note that two hooks are provided:
    ///     <see cref="OnCommitAsync"/> and <see cref="OnAfterCommitAsync"/>.  The former is always called before
    ///     the commit is executed, and the latter is only called when the commit was successfully executed!  When the
    ///     <see cref="OnCommitAsync"/> hook or the commit call itself fails, the transaction is rolled back on a
    ///     best-effort basis and the <see cref="OnAfterCommitAsync"/> hook is not called.
    /// </summary>
    /// <param name="httpContext">the given <see cref="HttpContext"/></param>
    /// <param name="transaction">the given <see cref="ITransaction"/></param>
    /// <param name="cancellationToken">the given <see cref="CancellationToken"/></param>
    /// <returns>
    ///     A <see cref="Task"/> representing the asynchronous action.
    /// </returns>
    protected async Task HandleCommitAsync(
        [NotNull] HttpContext httpContext,
        [NotNull] ITransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await OnCommitAsync(httpContext, cancellationToken).ConfigureAwait(false);
            await SessionProvider
                .SafeEnvironmentProviderAsync
                .RunAsync(
                    nameof(ITransaction.CommitAsync),
                    transaction.CommitAsync,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // log exception first
            Logger.LogError(e, "HandleCommit failed with exception");

            // next, do a best-effort rollback
            await HandleRollbackAsync(httpContext, transaction, CancellationToken.None).ConfigureAwait(false);

            // throw the original exception for correct exception handling
            throw;
        }

        try
        {
            // hook after commit
            await OnAfterCommitAsync(httpContext, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // log, but swallow exception
            Logger.LogError(e, "OnAfterCommitAsync hook failed with exception: exception is logged but swallowed");
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
