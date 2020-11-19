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
using System.IO;
using System.Linq;
using System.Reflection;

using Castle.MicroKernel.Registration;
using Castle.Windsor.Installer;

using JetBrains.Annotations;

namespace PPWCode.Host.Core.WebApi
{
    public static class WindsorContainerUtilities
    {
        [NotNull]
        public static IWindsorInstaller[] GetAssemblies(
            [NotNull] string dllPrefix,
            [CanBeNull] InstallerFactory installerFactory = null)
        {
            ISet<Assembly> assemblies = new HashSet<Assembly>();
            Assembly root = Assembly.GetExecutingAssembly();

            IEnumerable<Assembly> candidateAssemblies =
                AppDomain
                    .CurrentDomain
                    .GetAssemblies()
                    .Where(a => (a.FullName != null)
                                && a.FullName.StartsWith(dllPrefix, StringComparison.OrdinalIgnoreCase));
            foreach (Assembly assembly in candidateAssemblies)
            {
                assemblies.Add(assembly);
            }

            string directoryName = Path.GetDirectoryName(root.Location);
            if (!string.IsNullOrWhiteSpace(directoryName))
            {
                IEnumerable<string> candidateDirectories =
                    Directory
                        .EnumerateFiles(directoryName)
                        .Where(x => (x != null)
                                    && Path.HasExtension(x)
                                    && string.Equals(Path.GetExtension(x), ".dll",
                                                     StringComparison.InvariantCultureIgnoreCase)
                                    && Path.GetFileName(x).StartsWith(dllPrefix, StringComparison.OrdinalIgnoreCase));
                foreach (string file in candidateDirectories)
                {
                    assemblies.Add(Assembly.LoadFrom(file));
                }
            }

            return
                assemblies
                    .Select(a => FromAssembly.Instance(a, installerFactory ?? new InstallerFactory()))
                    .ToArray();
        }
    }
}
