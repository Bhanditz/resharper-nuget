﻿/*
 * Copyright 2012-2013 JetBrains s.r.o.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Collections.Generic;
using JetBrains.Application;
using JetBrains.ProjectModel.Model2.Assemblies.Interfaces;
using JetBrains.ReSharper.Psi;
#if RESHARPER_8
using JetBrains.ReSharper.Psi.Modules;
#endif
using JetBrains.TextControl;
using JetBrains.Util;
using System.Linq;

namespace JetBrains.ReSharper.Plugins.NuGet
{
    [PsiSharedComponent]
    public class NuGetModuleReferencerImpl
    {
        private readonly NuGetApi nuget;
        private readonly ITextControlManager textControlManager;
        private readonly IShellLocks shellLocks;

        public NuGetModuleReferencerImpl(NuGetApi nuget, ITextControlManager textControlManager, IShellLocks shellLocks)
        {
            this.nuget = nuget;
            this.textControlManager = textControlManager;
            this.shellLocks = shellLocks;
        }

        public bool CanReferenceModule(IPsiModule module, IPsiModule moduleToReference)
        {
            if (!IsProjectModule(module) || !IsAssemblyModule(moduleToReference))
                return false;

            var assemblyLocations = GetAllAssemblyLocations(moduleToReference);
            return nuget.AreAnyAssemblyFilesNuGetPackages(assemblyLocations);
        }

        public bool ReferenceModule(IPsiModule module, IPsiModule moduleToReference)
        {
            if (!IsProjectModule(module) || !IsAssemblyModule(moduleToReference))
                return false;

            var assemblyLocations = GetAllAssemblyLocations(moduleToReference);
            var projectModule = (IProjectPsiModule)module;

            string packageLocation;
            var handled = nuget.InstallNuGetPackageFromAssemblyFiles(assemblyLocations, projectModule.Project, out packageLocation);
            if (handled)
            {
                Hacks.PokeReSharpersAssemblyReferences(module, assemblyLocations, packageLocation, projectModule);
                Hacks.HandleFailureToReference(packageLocation, textControlManager, shellLocks);
            }

            return handled;
        }

        private static bool IsProjectModule(IPsiModule module)
        {
            return module is IProjectPsiModule;
        }

        private static bool IsAssemblyModule(IPsiModule module)
        {
            return module is IAssemblyPsiModule;
        }

        private static IList<FileSystemPath> GetAllAssemblyLocations(IPsiModule psiModule)
        {
            var projectModelAssembly = psiModule.ContainingProjectModule as IAssembly;
            if (projectModelAssembly == null)
                return EmptyList<FileSystemPath>.InstanceList;

            // ReSharper maintains a list of unique assemblies, and each assembly keeps a track of
            // all of the file copies of itself that the solution knows about. This list of file
            // locations includes sources for references (including NuGet packages), but can also
            // include outputs, e.g. if CopyLocal is set to True. The IAssemblyPsiModule.Assembly.Location
            // returns back the file location of the first copy of the assembly, but the order of
            // the list is undefined - it is entirely possible to get back a file location in a bin\Debug
            // folder. This doesn't help us when trying to add NuGet references - we need to look
            // at all of the locations to try and find the NuGet package location. So we use the
            // ProjectModel instead of the PSI, and get all file locations of the IAssembly
            return (from f in projectModelAssembly.GetFiles()
                    select f.Location).ToList();
        }
    }
}