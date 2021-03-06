using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Launcher.Server.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = BuildXL.Cache.ContentStore.Interfaces.Logging.ILogger;

namespace BuildXL.Launcher.Server
{
    public abstract class StartupBase
    {
        public StartupBase(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        protected abstract ServerMode Mode { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers()
                .ConfigureApplicationPartManager(
                apm =>
                {
                    apm.FeatureProviders.Clear();
                    apm.FeatureProviders.Add(new ConfiguredControllerFeatureProvider(this));
                });
        }

        protected enum ServerMode
        {
            /// <summary>
            /// Running as global deployment service used to provide launch manifests to launchers
            /// </summary>
            DeploymentService,

            /// <summary>
            /// Running as deployment proxy
            /// </summary>
            DeploymentProxy,
        }

        private class ConfiguredControllerFeatureProvider : ControllerFeatureProvider
        {
            private readonly StartupBase _startupBase;

            public ConfiguredControllerFeatureProvider(StartupBase startupBase)
            {
                _startupBase = startupBase;
            }

            public ServerMode Mode => _startupBase.Mode;

            protected override bool IsController(TypeInfo typeInfo)
            {
                if (!base.IsController(typeInfo))
                {
                    return false;
                }

                // Allow specific controllers depending on the mode
                if (typeInfo == typeof(DeploymentController))
                {
                    return Mode == ServerMode.DeploymentService;
                }
                else if (typeInfo == typeof(DeploymentProxyController))
                {
                    return Mode == ServerMode.DeploymentProxy;
                }

                return true;
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //app.UseHttpsRedirection();

            app.Use(async (context, next) =>
            {
                Console.WriteLine("Before");

                await next.Invoke();

                Console.WriteLine("After");
            });

            app.UseRouting();

            //app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

        }
    }
}
