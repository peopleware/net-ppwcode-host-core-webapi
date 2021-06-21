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

using Castle.Core.Logging;
using Castle.Windsor;

using JetBrains.Annotations;

namespace PPWCode.Host.Core.WebApi
{
    public abstract class Filter
    {
        private ILogger _logger = NullLogger.Instance;

        protected Filter([NotNull] IWindsorContainer container)
        {
            Container = container;
        }

        [NotNull]
        public IWindsorContainer Container { get; }

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
    }
}
