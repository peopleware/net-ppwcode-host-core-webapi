// Copyright 2022 by PeopleWare n.v..
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Castle.MicroKernel;
using Castle.MicroKernel.Lifestyle.Scoped;
using Castle.Windsor;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

using PPWCode.Vernacular.Exceptions.IV;

namespace PPWCode.Host.Core.WebApi;

/// <inheritdoc />
[UsedImplicitly]
public class ControllerActivator([NotNull] IWindsorContainer container)
    : IControllerActivator
{
    [NotNull]
    public IKernel Kernel { get; } = container.Kernel;

    /// <inheritdoc />
    public object Create(ControllerContext context)
    {
        CallContextLifetimeScope currentScope = CallContextLifetimeScope.ObtainCurrentScope();
        if (currentScope == null)
        {
            throw new ProgrammingError("No scope found, probably the scoping middleware was not activated in your pipeline.");
        }

        return Kernel.Resolve(context.ActionDescriptor.ControllerTypeInfo.AsType());
    }

    /// <inheritdoc />
    public void Release(ControllerContext context, object controller)
        => Kernel.ReleaseComponent(controller);
}