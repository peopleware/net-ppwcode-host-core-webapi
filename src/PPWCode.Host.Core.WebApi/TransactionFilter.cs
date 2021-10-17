// Copyright 2021 by PeopleWare n.v..
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
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Castle.Windsor;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

using NHibernate;

using PPWCode.Server.Core.Transactional;
using PPWCode.Vernacular.Exceptions.IV;

using ISession = NHibernate.ISession;

namespace PPWCode.Host.Core.WebApi
{
    /// <summary>
    ///     This filter is used to manage the transactions. The filter creates a transaction per WebAPI request.
    ///     The transaction is committed or rolled back based on the status code of the response.
    ///     A successful response (status code is between 200 and 299) will result in a commit.  If the response
    ///     is not successful, this will result in a rollback.
    /// </summary>
    /// <remarks>
    ///     This filter should be registered in the ASP.NET Core pipeline using <see cref="TransactionFilterProxy{T}" />.
    /// </remarks>
    public class TransactionFilter
        : Filter,
          IAsyncActionFilter,
          IAsyncAlwaysRunResultFilter
    {
        public const string RequestSimulation = "X-REQUEST-SIMULATION";
        public const string PpwRequestTransaction = "PPW_nhibernate_transaction";
        public const string PpwRequestSimulation = "PPW_request_simulation";

        protected static readonly ConcurrentDictionary<string, TransactionalAttribute> TransactionalCache =
            new ConcurrentDictionary<string, TransactionalAttribute>();

        private bool _logWarning = true;

        /// <inheritdoc />
        public TransactionFilter([NotNull] IWindsorContainer container)
            : base(container)
        {
        }

        /// <inheritdoc />
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            CancellationToken cancellationToken = context.HttpContext.RequestAborted;
            await InitiateTransaction(context, cancellationToken);

            await next();
        }

        /// <inheritdoc />
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            await next();

            CancellationToken cancellationToken = context.HttpContext.RequestAborted;
            await CloseTransaction(context, cancellationToken);
        }

        protected virtual Task InitiateTransaction([NotNull] ActionExecutingContext context, CancellationToken cancellationToken)
        {
            if (context.HttpContext.Items.ContainsKey(PpwRequestTransaction))
            {
                throw new ProgrammingError($"{ActionContextDisplayName(context)} Something went wrong, we have already started a transaction on the current session.");
            }

            TransactionalAttribute TransactionalFactory(string key)
                => OnTransactionalAttribute(key, context);

            Logger.Info(() => $"Determine if we should use transactions using attribute {nameof(TransactionalAttribute)}");
            string actionId = context.ActionDescriptor.Id;
            TransactionalAttribute transactional = TransactionalCache.GetOrAdd(actionId, TransactionalFactory);
            if ((transactional != null) && transactional.Transactional)
            {
                ISession session = Container.Resolve<ISession>();
                try
                {
                    if (!session.IsOpen)
                    {
                        throw new ProgrammingError($"{ActionContextDisplayName(context)} Current session is not opened.");
                    }

                    Logger.Info(() => $"{ActionContextDisplayName(context)} Start our request transaction, with isolation level {transactional.IsolationLevel}.");
                    context.HttpContext.Items[PpwRequestTransaction] = session.BeginTransaction(transactional.IsolationLevel);

                    if (context.HttpContext.Request.Headers.ContainsKey(RequestSimulation))
                    {
                        context.HttpContext.Items[PpwRequestSimulation] = "REQUEST-SIMULATION HEADER";
                    }
                }
                finally
                {
                    Container.Release(session);
                }
            }
            else
            {
                Logger.Info(() => $"{ActionContextDisplayName(context)} No transaction is requested.");
            }

            return Task.CompletedTask;
        }

        protected virtual async Task CloseTransaction([NotNull] ResultExecutingContext context, CancellationToken cancellationToken)
        {
            if (context.HttpContext.Items.TryGetValue(PpwRequestTransaction, out object transaction))
            {
                Logger.Info(() => $"{ActionContextDisplayName(context)} A transaction request is made.");
                ISession session = Container.Resolve<ISession>();
                try
                {
                    if (!session.IsOpen)
                    {
                        throw new ProgrammingError($"{ActionContextDisplayName(context)} Current session is not opened.");
                    }

                    ITransaction nhTransaction = (ITransaction)transaction;
                    try
                    {
                        if (!IsSuccessStatusCode(context) || context.HttpContext.Items.ContainsKey(PpwRequestSimulation))
                        {
                            try
                            {
                                try
                                {
                                    await OnRollbackAsync(context.HttpContext);
                                    if (context.HttpContext.Items.ContainsKey(PpwRequestSimulation))
                                    {
                                        Logger.Info(() => $"{ActionContextDisplayName(context)} Simulation was requested, a flush is done.");
                                        await session.FlushAsync(cancellationToken);
                                    }
                                }
                                finally
                                {
                                    Logger.Info(() => $"{ActionContextDisplayName(context)} Rollback our request transaction.");
                                    await nhTransaction.RollbackAsync(CancellationToken.None);
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.Error($"{ActionContextDisplayName(context)} While rollback our request transaction, something went wrong.", e);
                            }
                        }
                        else
                        {
                            try
                            {
                                await OnCommitAsync(context.HttpContext, cancellationToken);

                                Logger.Info(() => $"{ActionContextDisplayName(context)} Flush and commit our request transaction.");
                                await session.FlushAsync(cancellationToken);
                                await nhTransaction.CommitAsync(cancellationToken);

                                await OnAfterCommitAsync(context.HttpContext, cancellationToken);
                            }
                            catch (OperationCanceledException)
                            {
                                await nhTransaction.RollbackAsync(CancellationToken.None);
                                throw;
                            }
                            catch (Exception e)
                            {
                                Logger.Error($"{ActionContextDisplayName(context)} While flush and committing our request transaction, something went wrong.", e);
                                try
                                {
                                    try
                                    {
                                        await OnRollbackAsync(context.HttpContext);
                                    }
                                    finally
                                    {
                                        await nhTransaction.RollbackAsync(CancellationToken.None);
                                    }
                                }
                                catch (Exception e2)
                                {
                                    Logger.Error($"{ActionContextDisplayName(context)} While rollback our request transaction, something went wrong.", e2);
                                }

                                throw;
                            }
                        }
                    }
                    finally
                    {
                        Container.Release(session);
                    }
                }
                finally
                {
                    context.HttpContext.Items.Remove(PpwRequestTransaction);
                    context.HttpContext.Items.Remove(PpwRequestSimulation);
                }
            }
            else
            {
                Logger.Info(() => $"{ActionContextDisplayName(context)} A none transaction request is made.");
            }
        }

        protected virtual string ActionContextDisplayName(FilterContext context)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Controller context: ");

            ActionDescriptor actionDescriptor = context.ActionDescriptor;
            sb.Append(actionDescriptor == null ? "ActionDescriptor is null." : actionDescriptor.DisplayName);

            return sb.ToString();
        }

        protected virtual bool IsSuccessStatusCode(ResultExecutingContext context)
        {
            int statusCode = context.HttpContext.Response.StatusCode;
            return (statusCode >= (int)HttpStatusCode.OK) && (statusCode <= 299);
        }

        protected virtual Task OnCommitAsync(HttpContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        protected virtual Task OnAfterCommitAsync(HttpContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        protected virtual Task OnRollbackAsync(HttpContext context)
            => Task.CompletedTask;

        [CanBeNull]
        protected virtual TransactionalAttribute OnTransactionalAttribute(
            [NotNull] string actionId,
            [NotNull] ActionExecutingContext context)
        {
            TransactionalAttribute attribute = null;

            if (context.ActionDescriptor is ControllerActionDescriptor controllerActionDescriptor)
            {
                attribute =
                    controllerActionDescriptor
                        .MethodInfo
                        .GetCustomAttributes(typeof(TransactionalAttribute), true)
                        .OfType<TransactionalAttribute>()
                        .SingleOrDefault()
                    ?? controllerActionDescriptor
                        .ControllerTypeInfo
                        .GetCustomAttributes(typeof(TransactionalAttribute), true)
                        .OfType<TransactionalAttribute>()
                        .SingleOrDefault();
            }
            else
            {
                if (_logWarning)
                {
                    Logger.Warn(
                        $"{nameof(TransactionFilter)} is not able to detect the {nameof(TransactionalAttribute)} usage on class / method for action: {actionId}." +
                        $"the context of type {nameof(ActionExecutingContext.ActionDescriptor)} was not implemented by {nameof(ControllerActionDescriptor)}.");
                    _logWarning = false;
                }
            }

            return attribute;
        }
    }
}
