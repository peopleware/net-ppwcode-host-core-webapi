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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Castle.Windsor;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using PPWCode.API.Core;

namespace PPWCode.Host.Core.WebApi
{
    /// <summary>
    ///     This filter is used to validate the incoming data.  If the incoming
    ///     data is not valid, the status code is set to BadRequest (400) and
    ///     a body is created with the error messages.
    /// </summary>
    public class ValidateModelActionFilter
        : Filter,
          IAsyncActionFilter
    {
        /// <inheritdoc />
        public ValidateModelActionFilter([NotNull] IWindsorContainer container)
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
            await ValidateAsync(context, cancellationToken);

            await next();
        }

        protected Task ValidateAsync(ActionExecutingContext context, CancellationToken cancellationToken)
        {
            if (context.ModelState.IsValid == false)
            {
                Logger.Info("Our model-state is invalid, building a bad-request result ...");
                MessageList messageList = new MessageList();
                List<Message> messages = new List<Message>();
                foreach (KeyValuePair<string, ModelStateEntry> kv in context.ModelState)
                {
                    string key = kv.Key;
                    foreach (ModelError error in kv.Value.Errors)
                    {
                        string message = error.ErrorMessage;
                        if (string.IsNullOrWhiteSpace(message))
                        {
                            message = error.Exception?.Message;
                        }

                        if (Logger.IsInfoEnabled)
                        {
                            Logger.Info($"Invalid model: {key} => {message}");
                        }

                        messages.Add(Message.CreateUntranslatedMessage(InfoLevelEnum.ERROR, $"{key} => {message}"));
                    }
                }

                messageList.Messages = messages.ToArray();
                context.Result = new BadRequestObjectResult(messageList);
            }
            else
            {
                Logger.Info("Our model-state is valid.");
            }

            return Task.CompletedTask;
        }
    }
}
