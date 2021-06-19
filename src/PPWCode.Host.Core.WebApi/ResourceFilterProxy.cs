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

using System.Threading.Tasks;

using Castle.Windsor;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Mvc.Filters;

namespace PPWCode.Host.Core.WebApi
{
    public sealed class ResourceFilterProxy<TResourceFilter>
        : FilterProxy<TResourceFilter>,
          IAsyncResourceFilter
        where TResourceFilter : class, IAsyncResourceFilter, IOrderedFilter
    {
        /// <inheritdoc />
        public ResourceFilterProxy([NotNull] IWindsorContainer container, int order)
            : base(container, order)
        {
        }

        /// <inheritdoc />
        public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
            => CreateFilterInstance(Arguments).OnResourceExecutionAsync(context, next);
    }
}
