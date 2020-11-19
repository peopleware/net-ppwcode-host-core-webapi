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

using System.Threading.Tasks;

using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.Windsor;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Mvc.Filters;

namespace PPWCode.Host.Core.WebApi
{
    /// <inheritdoc cref="IAsyncActionFilter" />
    /// <inheritdoc cref="IOrderedFilter" />
    public sealed class ActionFilterProxy<TActionFilter>
        : IAsyncActionFilter,
          IOrderedFilter
        where TActionFilter : class, IAsyncActionFilter, IOrderedFilter
    {
        // ReSharper disable once StaticMemberInGenericType
        private static readonly object _locker = new object();
        private volatile TActionFilter _actionFilterInstance;
        private bool? _canCache;

        public ActionFilterProxy([NotNull] IWindsorContainer container, int order)
        {
            Container = container;
            Order = order;
        }

        [NotNull]
        private Arguments Arguments
            => new Arguments().AddNamed("order", Order);

        [NotNull]
        public IWindsorContainer Container { get; }

        /// <inheritdoc />
        public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            => CreateActionFilterInstance(Arguments).OnActionExecutionAsync(context, next);

        public int Order { get; }

        [NotNull]
        private TActionFilter CreateActionFilterInstance(Arguments arguments)
        {
            if (_canCache == false)
            {
                return ResolveActionFilter(arguments);
            }

            if (_actionFilterInstance == null)
            {
                lock (_locker)
                {
                    if (_actionFilterInstance == null)
                    {
                        IHandler handler = Container.Kernel.GetHandler(typeof(TActionFilter));
                        if (handler.ComponentModel.LifestyleType != LifestyleType.Singleton)
                        {
                            _canCache = false;
                            return ResolveActionFilter(arguments);
                        }

                        CreationContext creationContext =
                            new CreationContext(
                                handler,
                                Container.Kernel.ReleasePolicy,
                                typeof(TActionFilter),
                                Arguments,
                                null,
                                null);
                        _actionFilterInstance = (TActionFilter)handler.Resolve(creationContext);
                        _canCache = true;
                    }
                }
            }

            return _actionFilterInstance;
        }

        [NotNull]
        private TActionFilter ResolveActionFilter(Arguments arguments)
            => Container.Kernel.Resolve<TActionFilter>(arguments);
    }
}
