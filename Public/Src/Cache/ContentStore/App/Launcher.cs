// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        /// Run the Deployment launcher verb for downloading and running deployments.
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Run deployment launcher")]
        internal void Launcher
            (
            [Required, Description("Path to LauncherSettings file")] string settingsPath,
            [DefaultValue(false)] bool debug,
            [DefaultValue(false)] bool shutdown = false
            )
        {
            Initialize();

            if (debug)
            {
                System.Diagnostics.Debugger.Launch();
            }

            try
            {
                Validate();

                var configJson = File.ReadAllText(settingsPath);

                var settings = JsonSerializer.Deserialize<LauncherApplicationSettings>(configJson, DeploymentUtilities.ConfigurationSerializationOptions);

                var launcher = new DeploymentLauncher(settings, _fileSystem);

                runAsync().GetAwaiter().GetResult();
                async Task runAsync()
                {
                    if (shutdown)
                    {
                        var context = new OperationContext(new Context(_logger), _cancellationToken);
                        await launcher.LifetimeManager.ShutdownServiceAsync(context, settings.LauncherServiceId);
                        return;
                    }

                    var host = new EnvironmentVariableHost();
                    settings.DeploymentParameters.AuthorizationSecret ??= await host.GetPlainSecretAsync(settings.DeploymentParameters.AuthorizationSecretName, _cancellationToken);

                    var arguments = new LoggerFactoryArguments(_logger, host, settings.LoggingSettings)
                    {
                        TelemetryFieldsProvider = new HostTelemetryFieldsProvider(settings.DeploymentParameters)
                    };

                    var replacementLogger = LoggerFactory.CreateReplacementLogger(arguments);
                    using (replacementLogger.DisposableToken)
                    {
                        var token = _cancellationToken;
                        var context = new OperationContext(new Context(replacementLogger.Logger), token);

                        await launcher.LifetimeManager.RunInterruptableServiceAsync(context, settings.LauncherServiceId, async token =>
                        {
                            try
                            {
                                await launcher.StartupAsync(context).ThrowIfFailureAsync();
                                var task = token.WaitForCancellationAsync();
                                await task;
                            }
                            finally
                            {
                                await launcher.ShutdownAsync(context).ThrowIfFailureAsync();
                            }

                            return true;
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}
