﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// --------------------------------------------------------------------------------------------

using System;
using Amqp;
using app.Logging;
using app.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace app
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc()
                .AddJsonOptions(o =>
                {
                    o.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
                });

            services.AddOptions();

            // Enable CORS
            services.AddCors(options => 
                options.AddPolicy("MyPolicy", builder =>
                    {
                        builder.AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowAnyOrigin();
                    }
                )
            );
            
            services.Configure<CustomConfiguration>(Configuration.GetSection("CustomConfiguration"));

            // Configure application insights.
            var applicationInsights = new ApplicationInsights();
            Configuration.GetSection("ApplicationInsights").Bind(applicationInsights);
            var instrumentationKey = Environment.GetEnvironmentVariable(Constants.ApplicationInsightsKeyEnv) ?? applicationInsights.InstrumentationKey;
            services.AddApplicationInsightsTelemetry(instrumentationKey);


        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IApplicationLifetime lifetime)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseMiddleware(typeof(OperationContextMiddleware));
            app.UseMiddleware(typeof(RequestLoggingMiddleware));
            app.UseCors("MyPolicy");
            app.UseMvc();

            lifetime.ApplicationStopping.Register(() =>
            {
                LogUtility.Log("Graceful shutdown.");
            });   
        }
    }
}