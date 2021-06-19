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

using Castle.Windsor;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Mvc.Filters;

namespace PPWCode.Host.Core.WebApi
{
    /// <inheritdoc cref="IAsyncActionFilter" />
    /// <inheritdoc cref="IOrderedFilter" />
    public sealed class ActionFilterProxy<TActionFilter>
        : FilterProxy<TActionFilter>,
          IAsyncActionFilter
        where TActionFilter : class, IAsyncActionFilter, IOrderedFilter
    {
        /// <inheritdoc />
        public ActionFilterProxy([NotNull] IWindsorContainer container, int order)
            : base(container, order)
        {
        }

        /// <inheritdoc />
        public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            => CreateFilterInstance(Arguments).OnActionExecutionAsync(context, next);
    }
}
