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

using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.Windsor;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Mvc.Filters;

namespace PPWCode.Host.Core.WebApi
{
    public abstract class FilterProxy<TFilter> : IOrderedFilter
    where TFilter : class
    {
        // ReSharper disable once StaticMemberInGenericType
        private static readonly object _locker = new object();
        private volatile TFilter _filterInstance;
        private bool? _canCache;

        public FilterProxy([NotNull] IWindsorContainer container, int order)
        {
            Container = container;
            Order = order;
        }

        [NotNull]
        public IWindsorContainer Container { get; }

        public int Order { get; }

        [NotNull]
        protected Arguments Arguments
            => new Arguments().AddNamed("order", Order);

        [NotNull]
        protected TFilter CreateFilterInstance(Arguments arguments)
        {
            if (_canCache == false)
            {
                return ResolveActionFilter(arguments);
            }

            if (_filterInstance == null)
            {
                lock (_locker)
                {
                    if (_filterInstance == null)
                    {
                        IHandler handler = Container.Kernel.GetHandler(typeof(TFilter));
                        if (handler.ComponentModel.LifestyleType != LifestyleType.Singleton)
                        {
                            _canCache = false;
                            return ResolveActionFilter(arguments);
                        }

                        CreationContext creationContext =
                            new CreationContext(
                                handler,
                                Container.Kernel.ReleasePolicy,
                                typeof(TFilter),
                                Arguments,
                                null,
                                null);
                        _filterInstance = (TFilter)handler.Resolve(creationContext);
                        _canCache = true;
                    }
                }
            }

            return _filterInstance;
        }

        [NotNull]
        private TFilter ResolveActionFilter(Arguments arguments)
            => Container.Kernel.Resolve<TFilter>(arguments);
    }
}
