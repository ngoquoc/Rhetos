﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using Rhetos;
using Rhetos.Logging;
using Rhetos.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeployPackages
{
    public static class Program
    {
        private readonly static Dictionary<string, string> _validArguments =  new Dictionary<string, string>()
        {
            { "/StartPaused", "Use for debugging with Visual Studio (Attach to Process)." },
            { "/Debug", "Generates unoptimized dlls (ServerDom.*.dll, e.g.) for debugging." },
            { "/NoPause", "Don't pause on error. Use this switch for build automation." },
            { "/IgnoreDependencies", "Allow installing incompatible versions of Rhetos packages." },
            { "/ShortTransactions", "Commit transaction after creating or dropping each database object." },
            { "/DatabaseOnly", "Keep old plugins and files in bin\\Generated." },
            { "/SkipRecompute", "Skip automatic update of computed data with KeepSynchronized." }
        };

        public static int Main(string[] args)
        {
            var logProvider = new NLogProvider();
            var logger = logProvider.GetLogger("DeployPackages");
            var pauseOnError = false;

            logger.Info(() => "Logging configured.");

            try
            {
                if (!ValidateArguments(args))
                    return 1;

                string rhetosAppRootPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));

                Build(args, logProvider, rhetosAppRootPath, out pauseOnError);
                DbUpdate(args, logProvider);

                logger.Info("Done.");
            }
            catch (Exception e)
            {
                logger.Error(e.ToString());

                string typeLoadReport = CsUtility.ReportTypeLoadException(e);
                if (typeLoadReport != null)
                    logger.Error(typeLoadReport);

                if (Environment.UserInteractive)
                    InteractiveExceptionInfo(e, pauseOnError);

                return 1;
            }

            return 0;
        }

        private static void Build(string[] args, NLogProvider logProvider, string rhetosAppRootPath, out bool pauseOnError)
        {
            var logger = logProvider.GetLogger("DeployPackages");

            var configurationProvider = new ConfigurationBuilder()
                   .AddOptions(new RhetosBuildEnvironment
                   {
                       ProjectFolder = rhetosAppRootPath,
                       OutputAssemblyName = null,
                       CacheFolder = Path.Combine(rhetosAppRootPath, "GeneratedFilesCache"),
                       GeneratedAssetsFolder = Path.Combine(rhetosAppRootPath, "bin", "Generated"),
                       GeneratedSourceFolder = null,
                   })
                   .AddOptions(new LegacyPathsOptions
                   {
                       BinFolder = Path.Combine(rhetosAppRootPath, "bin"),
                       PluginsFolder = Path.Combine(rhetosAppRootPath, "bin", "Plugins"),
                       ResourcesFolder = Path.Combine(rhetosAppRootPath, "Resources"),
                   })
                   .AddKeyValue(nameof(BuildOptions.GenerateAppSettings), false)
                   .AddKeyValue(nameof(BuildOptions.Legacy__BuildResourcesFolder).Replace("__", "."), true) // This legacy option is hardcorded for legacy build. This option can be set in rhetos-build.settings.json, but that JSON file does not need to be shipped with DeployPackages zip package.
                   .AddWebConfiguration(rhetosAppRootPath)
                   .AddConfigurationManagerConfiguration()
                   .AddCommandLineArguments(args, "/")
                   .Build();

            var deployOptions = configurationProvider.GetOptions<DeployOptions>();
            pauseOnError = !deployOptions.NoPause;
            if (deployOptions.StartPaused)
                StartPaused();

            var build = new ApplicationBuild(configurationProvider, logProvider, () => GetBuildPlugins(Path.Combine(rhetosAppRootPath, "bin", "Plugins")));
            if (!deployOptions.DatabaseOnly)
            {
                DeleteObsoleteFiles(logProvider, logger);
                var installedPackages = build.DownloadPackages(deployOptions.IgnoreDependencies);
                build.GenerateApplication(installedPackages);
            }
            else
            {
                logger.Info("Skipped deleting old generated files (DeployDatabaseOnly).");
                logger.Info("Skipped download packages (DeployDatabaseOnly).");
                logger.Info("Skipped code generators (DeployDatabaseOnly).");
            }
        }

        private static IEnumerable<string> GetBuildPlugins(string pluginsFolder)
        {
            return Directory.GetFiles(pluginsFolder, "*.dll", SearchOption.TopDirectoryOnly);
        }

        /// <summary>
        /// This feature is intended to simplify attaching debugger to the process that was run from a build script.
        /// </summary>
        private static void StartPaused()
        {
            if (!Environment.UserInteractive)
                throw new UserException("DeployPackages parameter 'StartPaused' must not be set, because the application is executed in a non-interactive environment.");

            Console.WriteLine("Press any key to continue . . .");
            Console.ReadKey(true);
        }

        private static bool ValidateArguments(string[] args)
        {
            if (args.Contains("/?"))
            {
                ShowHelp();
                return false;
            }

            var invalidArgument = args.FirstOrDefault(arg => !_validArguments.Keys.Contains(arg, StringComparer.InvariantCultureIgnoreCase));
            if (invalidArgument != null)
            {
                ShowHelp();
                throw new FrameworkException($"Unexpected command-line argument: '{invalidArgument}'.");
            }
            return true;
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Command-line arguments:");
            foreach (var argument in _validArguments)
                Console.WriteLine($"{argument.Key.PadRight(20)} {argument.Value}");
        }

        /// <summary>
        /// Deletes left-over files from old versions of Rhetos framework.
        /// Throws an exception if important data might be lost.
        /// </summary>
        private static void DeleteObsoleteFiles(ILogProvider logProvider, ILogger logger)
        {
            var filesUtility = new FilesUtility(logProvider);

            var obsoleteFolders = new string[]
            {
                Path.Combine(Paths.RhetosServerRootPath, "DslScripts"),
                Path.Combine(Paths.RhetosServerRootPath, "DataMigration"),
            };
            var obsoleteFolder = obsoleteFolders.FirstOrDefault(folder => Directory.Exists(folder));
            if (obsoleteFolder != null)
                throw new UserException("Please backup all Rhetos server folders and delete obsolete folder '" + obsoleteFolder + "'. It is no longer used.");

            var deleteObsoleteFiles = new string[]
            {
                Path.Combine(Paths.RhetosServerRootPath, "bin", "ServerDom.cs"),
                Path.Combine(Paths.RhetosServerRootPath, "bin", "ServerDom.dll"),
                Path.Combine(Paths.RhetosServerRootPath, "bin", "ServerDom.pdb"),
            };

            foreach (var path in deleteObsoleteFiles)
                if (File.Exists(path))
                {
                    logger.Warning($"Deleting obsolete file '{path}'.");
                    filesUtility.SafeDeleteFile(path);
                }
        }

        private static void DbUpdate(string[] args, NLogProvider logProvider)
        {
            var host = Host.Find(AppDomain.CurrentDomain.BaseDirectory, logProvider);
            var configurationProvider = host.RhetosRuntime
                .BuildConfiguration(logProvider, host.ConfigurationFolder, (builder) =>
                {
                    builder.AddCommandLineArguments(args, "/");
                });

            var deployment = new ApplicationDeployment(configurationProvider, logProvider, LegacyUtilities.GetRuntimeAssembliesDelegate(configurationProvider));

            deployment.UpdateDatabase();
            deployment.InitializeGeneratedApplication(host.RhetosRuntime);
            deployment.RestartWebServer();
        }

        private static void InteractiveExceptionInfo(Exception e, bool pauseOnError)
        {
            DeploymentUtility.PrintErrorSummary(e);

            if (pauseOnError)
            {
                Console.WriteLine("Press any key to continue . . .  (use /NoPause switch to avoid pause on error)");
                Console.ReadKey(true);
            }
        }
    }
}
