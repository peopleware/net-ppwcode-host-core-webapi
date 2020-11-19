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
    /// <inheritdoc />
    public sealed class ExceptionFilterProxy<TExceptionFilter>
        : IAsyncExceptionFilter
        where TExceptionFilter : class, IAsyncExceptionFilter, IOrderedFilter
    {
        // ReSharper disable once StaticMemberInGenericType
        private static readonly object _locker = new object();
        private bool? _canCache;
        private volatile TExceptionFilter _exceptionFilterInstance;

        public ExceptionFilterProxy([NotNull] IWindsorContainer container, int order)
        {
            Kernel = container.Kernel;
            Order = order;
        }

        [NotNull]
        public IKernel Kernel { get; }

        public int Order { get; }

        [NotNull]
        private Arguments Arguments
            => new Arguments().AddNamed("order", Order);

        /// <inheritdoc />
        public Task OnExceptionAsync(ExceptionContext context)
            => CreateExceptionFilterInstance(Arguments).OnExceptionAsync(context);

        [NotNull]
        private TExceptionFilter CreateExceptionFilterInstance(Arguments arguments)
        {
            if (_canCache == false)
            {
                return ResolveActionFilter(arguments);
            }

            if (_exceptionFilterInstance == null)
            {
                lock (_locker)
                {
                    if (_exceptionFilterInstance == null)
                    {
                        IHandler handler = Kernel.GetHandler(typeof(TExceptionFilter));
                        if (handler.ComponentModel.LifestyleType != LifestyleType.Singleton)
                        {
                            _canCache = false;
                            return ResolveActionFilter(arguments);
                        }

                        CreationContext creationContext = new CreationContext(handler, Kernel.ReleasePolicy, typeof(TExceptionFilter), Arguments, null, null);
                        _exceptionFilterInstance = (TExceptionFilter)handler.Resolve(creationContext);
                        _canCache = true;
                    }
                }
            }

            return _exceptionFilterInstance;
        }

        [NotNull]
        private TExceptionFilter ResolveActionFilter(Arguments arguments)
            => Kernel.Resolve<TExceptionFilter>(arguments);
    }
}
