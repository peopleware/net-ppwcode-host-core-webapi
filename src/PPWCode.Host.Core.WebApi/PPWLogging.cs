// Copyright 2026 by PeopleWare n.v..
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

using JetBrains.Annotations;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PPWCode.Host.Core.WebApi;

/// <summary>
///     Central logging configuration point for the PPWCode.Service.Core library.
/// </summary>
/// <remarks>
///     <para>
///         This static class provides access to a shared <see cref="ILoggerFactory" /> instance
///         used internally by the library to create loggers. It defaults to
///         <see cref="NullLoggerFactory.Instance" /> so that logging is silently suppressed
///         unless explicitly configured by the host application.
///     </para>
///     <para>
///         To enable logging, assign a configured <see cref="ILoggerFactory" /> during application
///         startup, before any NHibernate sessions are created:
///         <code>
///             PPWLogging.Factory = loggerFactory;
///         </code>
///     </para>
/// </remarks>
public static class PPWLogging
{
    /// <summary>
    ///     Gets or sets the <see cref="ILoggerFactory" /> used to create loggers throughout the library.
    /// </summary>
    /// <value>
    ///     Defaults to <see cref="NullLoggerFactory.Instance" />, which discards all log output.
    ///     Replace it with the application's logger factory to enable logging.
    /// </value>
    [NotNull]
    public static ILoggerFactory Factory { get; set; } = NullLoggerFactory.Instance;

    /// <summary>
    ///     Creates an <see cref="ILogger" /> for the specified type <typeparamref name="T" />
    ///     using the configured <see cref="Factory" />.
    /// </summary>
    /// <typeparam name="T">The type whose full name is used as the logger category name.</typeparam>
    /// <returns>An <see cref="ILogger" /> instance for <typeparamref name="T" />.</returns>
    [NotNull]
    public static ILogger GetLogger<T>()
        => Factory.CreateLogger<T>();

    /// <summary>
    ///     Creates an <see cref="ILogger" /> for the specified <see cref="Type" />
    ///     using the configured <see cref="Factory" />.
    /// </summary>
    /// <param name="type">The type whose full name is used as the logger category name.</param>
    /// <returns>An <see cref="ILogger" /> instance for the specified <see cref="Type" />.</returns>
    [NotNull]
    public static ILogger GetLogger(Type type)
        => Factory.CreateLogger(type);
}
