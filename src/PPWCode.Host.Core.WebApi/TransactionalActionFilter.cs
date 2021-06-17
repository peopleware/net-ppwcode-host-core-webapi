// Copyright 2020 by PeopleWare n.v..
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

using Castle.Windsor;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

using NHibernate;

using PPWCode.Vernacular.Exceptions.IV;

using ISession = NHibernate.ISession;

namespace PPWCode.Host.Core.WebApi
{
    /// <inheritdoc />
    public class TransactionalActionFilter
        : AsyncActionOrderedFilter
    {
        public const string RequestSimulation = "X-REQUEST-SIMULATION";
        public const string PpwRequestTransaction = "PPW_nhibernate_transaction";
        public const string PpwRequestSimulation = "PPW_request_simulation";

        /// <inheritdoc />
        public TransactionalActionFilter([NotNull] IWindsorContainer container, int order)
            : base(container, order)
        {
        }

        /// <inheritdoc />
        protected override async Task OnActionExecutedAsync(ActionExecutedContext context, CancellationToken cancellationToken)
        {
            Logger.Info("Check if there is a request transaction.");
            if (context.HttpContext.Items.TryGetValue(PpwRequestTransaction, out object transaction))
            {
                ISession session = Kernel.Resolve<ISession>();
                try
                {
                    if (!session.IsOpen)
                    {
                        throw new ProgrammingError("Current session is not opened.");
                    }

                    ITransaction nhTransaction = (ITransaction)transaction;
                    try
                    {
                        if ((context.Exception != null)
                            || ((context.Result != null) && !IsSuccessStatusCode(context.Result))
                            || context.HttpContext.Items.ContainsKey(PpwRequestSimulation))
                        {
                            try
                            {
                                try
                                {
                                    await OnRollbackAsync(context.HttpContext);
                                    if (context.HttpContext.Items.ContainsKey(PpwRequestSimulation))
                                    {
                                        Logger.Info("Simulation was requested, a flush is done.");
                                        await session.FlushAsync(cancellationToken);
                                    }
                                }
                                finally
                                {
                                    Logger.Info("Rollback our request transaction.");
                                    await nhTransaction.RollbackAsync();
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.Error("While rollback our request transaction, something went wrong.", e);
                            }
                        }
                        else
                        {
                            try
                            {
                                await OnCommitAsync(context.HttpContext, cancellationToken);

                                Logger.Info("Flush and commit our request transaction.");
                                await session.FlushAsync(cancellationToken);
                                await nhTransaction.CommitAsync(cancellationToken);

                                await OnAfterCommitAsync(context.HttpContext, cancellationToken);
                            }
                            catch (OperationCanceledException)
                            {
                                await nhTransaction.RollbackAsync();
                                throw;
                            }
                            catch (Exception e)
                            {
                                Logger.Error("While flush and committing our request transaction, something went wrong.", e);
                                try
                                {
                                    try
                                    {
                                        await OnRollbackAsync(context.HttpContext);
                                    }
                                    finally
                                    {
                                        await nhTransaction.RollbackAsync();
                                    }
                                }
                                catch (Exception e2)
                                {
                                    Logger.Error("While rollback our request transaction, something went wrong.", e2);
                                }

                                throw;
                            }
                        }
                    }
                    finally
                    {
                        Kernel.ReleaseComponent(session);
                    }
                }
                finally
                {
                    context.HttpContext.Items.Remove(PpwRequestTransaction);
                    context.HttpContext.Items.Remove(PpwRequestSimulation);
                }
            }
        }

        /// <inheritdoc />
        protected override Task OnActionExecutingAsync(ActionExecutingContext context, CancellationToken cancellationToken)
        {
            if (context.HttpContext.Items.ContainsKey(PpwRequestTransaction))
            {
                throw new ProgrammingError("Something went wrong, we have already started a transaction on the current session.");
            }

            ISession session = Kernel.Resolve<ISession>();
            try
            {
                if (!session.IsOpen)
                {
                    throw new ProgrammingError("Current session is not opened.");
                }

                Logger.Info("Start our request transaction.");
                context.HttpContext.Items[PpwRequestTransaction] = session.BeginTransaction(IsolationLevel.Unspecified);
            }
            finally
            {
                Kernel.ReleaseComponent(session);
            }

            if (context.HttpContext.Request.Headers.ContainsKey(RequestSimulation))
            {
                context.HttpContext.Items[PpwRequestSimulation] = "REQUEST-SIMULATION HEADER";
            }

            return Task.CompletedTask;
        }

        private bool IsSuccessStatusCode(IActionResult actionResult)
        {
            IStatusCodeActionResult statusCodeActionResult = actionResult as IStatusCodeActionResult;
            if (statusCodeActionResult == null)
            {
                Logger.Error(
                    $"Cannot determine HTTP status code, {nameof(actionResult)} is {actionResult?.GetType()}: "
                    + "Assume non-success status code.");
                return false;
            }

            int statusCode = statusCodeActionResult.StatusCode.GetValueOrDefault((int)HttpStatusCode.OK);
            return (statusCode >= (int)HttpStatusCode.OK) && (statusCode <= 299);
        }

        protected virtual Task OnCommitAsync(HttpContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        protected virtual Task OnAfterCommitAsync(HttpContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        protected virtual Task OnRollbackAsync(HttpContext context)
            => Task.CompletedTask;
    }
}