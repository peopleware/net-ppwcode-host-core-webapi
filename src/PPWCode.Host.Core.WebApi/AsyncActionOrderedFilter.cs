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
using System.Threading;
using System.Threading.Tasks;

using Castle.Core.Logging;
using Castle.MicroKernel;
using Castle.Windsor;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Mvc.Filters;

namespace PPWCode.Host.Core.WebApi
{
    /// <inheritdoc cref="IAsyncActionFilter" />
    /// <inheritdoc cref="IOrderedFilter" />
    public abstract class AsyncActionOrderedFilter
        : IAsyncActionFilter,
          IOrderedFilter
    {
        private ILogger _logger = NullLogger.Instance;

        protected AsyncActionOrderedFilter([NotNull] IWindsorContainer container, int order)
        {
            Kernel = container.Kernel;
            Order = order;
        }

        [NotNull]
        public IKernel Kernel { get; }

        [UsedImplicitly]
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

            if (Logger.IsInfoEnabled)
            {
                Logger.Info($"On Executing filter {GetType().Name} on order {Order}.");
            }

            CancellationToken cancellationToken = context.HttpContext.RequestAborted;
            await OnActionExecutingAsync(context, cancellationToken);
            if (context.Result == null)
            {
                ActionExecutedContext actionExecutedContext = await next();
                if (Logger.IsInfoEnabled)
                {
                    Logger.Info($"On Executed for filter {GetType().Name} on order {Order}.");
                }

                await OnActionExecutedAsync(actionExecutedContext, cancellationToken);
            }
        }

        /// <inheritdoc />
        public int Order { get; }

        protected abstract Task OnActionExecutedAsync(ActionExecutedContext context, CancellationToken cancellationToken);

        protected abstract Task OnActionExecutingAsync(ActionExecutingContext context, CancellationToken cancellationToken);
    }
}
