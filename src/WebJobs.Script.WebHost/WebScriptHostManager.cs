// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.WebHooks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostManager : ScriptHostManager
    {
        private static Lazy<MethodInfo> _getWebHookDataMethod = new Lazy<MethodInfo>(CreateGetWebHookDataMethodInfo);
        private readonly IMetricsLogger _metricsLogger;
        private readonly SecretManager _secretManager;

        public WebScriptHostManager(ScriptHostConfiguration config, SecretManager secretManager) : base(config)
        {
            _metricsLogger = new WebHostMetricsLogger();
            _secretManager = secretManager;
        }

        private IDictionary<string, FunctionDescriptor> HttpFunctions { get; set; }

        public async Task<HttpResponseMessage> HandleRequestAsync(FunctionDescriptor function, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // All authentication is assumed to have been done on the request
            // BEFORE this method is called

            Dictionary<string, object> arguments = await GetFunctionArgumentsAsync(function, request);

            // Suspend the current synchronization context so we don't pass the ASP.NET
            // context down to the function.
            using (var syncContextSuspensionScope = new SuspendedSynchronizationContextScope())
            {
                await Instance.CallAsync(function.Name, arguments, cancellationToken);
            }

            // Get the response
            HttpResponseMessage response = null;
            if (!request.Properties.TryGetValue<HttpResponseMessage>("MS_AzureFunctionsHttpResponse", out response))
            {
                // the function was successful but did not write an explicit response
                response = new HttpResponseMessage(HttpStatusCode.OK);
            }

            return response;
        }

        private static MethodInfo CreateGetWebHookDataMethodInfo()
        {
            return typeof(WebHookHandlerContextExtensions).GetMethod("GetDataOrDefault", BindingFlags.Public | BindingFlags.Static);
        }

        private static async Task<Dictionary<string, object>> GetFunctionArgumentsAsync(FunctionDescriptor function, HttpRequestMessage request)
        {
            ParameterDescriptor triggerParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>();
            object triggerArgument = null;
            if (triggerParameter.Type == typeof(HttpRequestMessage))
            {
                triggerArgument = request;
            }
            else
            {
                // We'll replace the trigger argument but still want to flow the request
                // so add it to the arguments, as a system argument
                arguments.Add(ScriptConstants.DefaultSystemTriggerParameterName, request);

                HttpTriggerBindingMetadata httpFunctionMetadata = (HttpTriggerBindingMetadata)function.Metadata.InputBindings.FirstOrDefault(p => p.Type == BindingType.HttpTrigger);
                if (!string.IsNullOrEmpty(httpFunctionMetadata.WebHookType))
                {
                    WebHookHandlerContext webHookContext;
                    if (request.Properties.TryGetValue(ScriptConstants.AzureFunctionsWebHookContextKey, out webHookContext))
                    {
                        triggerArgument = GetWebHookData(triggerParameter.Type, webHookContext);
                    }
                }

                if (triggerArgument == null)
                {
                    triggerArgument = await request.Content.ReadAsAsync(triggerParameter.Type);
                }
            }

            arguments.Add(triggerParameter.Name, triggerArgument);

            //add arguments from query string to avoid having WebJobs SDK throw an error for any parameters
            //it doesn't find values from through bindings.
            var otherArguments = RoutingUtility.ExtractQueryArguments(function.Metadata, request);
            if (otherArguments != null)
            {
                foreach (var argument in otherArguments)
                {
                    arguments.Add(argument.Key, argument.Value);
                }
            }

            return arguments;
        }

        private static object GetWebHookData(Type dataType, WebHookHandlerContext context)
        {
            MethodInfo getDataMethod = _getWebHookDataMethod.Value.MakeGenericMethod(dataType);
            return getDataMethod.Invoke(null, new object[] { context });
        }

        public FunctionDescriptor GetHttpFunctionOrNull(Uri uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException("uri");
            }

            FunctionDescriptor function = null;

            if (HttpFunctions == null || HttpFunctions.Count == 0)
            {
                return null;
            }

            // Parse the route (e.g. "api/myfunc") to get 'myfunc"
            // including any path after "api/"
            string route = uri.AbsolutePath;
            int idx = route.ToLowerInvariant().IndexOf("api", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                idx = route.IndexOf('/', idx);
                route = route.Substring(idx + 1).Trim('/');

                //attempt to find if any of the keys exactly match this route
                HttpFunctions.TryGetValue(route.ToLowerInvariant(), out function);

                //if still haven't found a function look at all of the regex patterns representing 
                //routes in HttpFunctions.
                if (function == null)
                {
                    function = (from func in HttpFunctions
                                where Regex.Matches(route, func.Key).Count > 0
                                select func.Value)
                                .FirstOrDefault();
                }   
            }
            return function;
        }

        protected override void OnInitializeConfig(JobHostConfiguration config)
        {
            base.OnInitializeConfig(config);
            
            // Add our WebHost specific services
            config.AddService<IMetricsLogger>(_metricsLogger);

            // Register the new "FastLogger" for Dashboard support
            var dashboardString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Dashboard);
            if (dashboardString != null)
            {
                var fastLogger = new FastLogger(dashboardString);
                config.AddService<IAsyncCollector<FunctionInstanceLogEntry>>(fastLogger);
            }
            config.DashboardConnectionString = null; // disable slow logging 
        }

        private static string QueryStringToRegexString(string queryTemplate)
        {   
            if (queryTemplate == null)
            {
                return null;
            }
            //ensure that the format doesn't start with a '/'. maybe should enforce as a rule for route templates.
            queryTemplate = queryTemplate.Trim('/');
            //strip off the query parameters, as they are not technically part of the route we will recieve.
            int queryParamsIndex = queryTemplate.IndexOf("?", StringComparison.OrdinalIgnoreCase);
            if (queryParamsIndex > 0)
            {
                queryTemplate = queryTemplate.Substring(0, queryParamsIndex);
            }

            StringBuilder queryBuilder = new StringBuilder();

            IDictionary<string, string> paramTypes = RoutingUtility.ExtractPathParameterTypes(queryTemplate);
            var templateSections = queryTemplate.Split('/');
            foreach (string segment in templateSections)
            {
                string sectionString;
                if (segment.StartsWith("{", StringComparison.OrdinalIgnoreCase) &&
                     segment.EndsWith("}", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parameterParts = segment.Substring(1, segment.Length - 2).Split(':');
                    //find the type for this parameter, defaulting to string
                    string parameterType;
                    paramTypes.TryGetValue(parameterParts[0], out parameterType);
                    //generate a regular expression for this section that matches the appropriate type  
                    switch (parameterType)
                    {
                        case "string":
                            sectionString = @"/\w+/";
                            break;
                        case "int":
                            sectionString = @"/\d+/";
                            break;
                        case "bool":
                            sectionString = @"/{true|false}/";
                            break;
                        default:
                            sectionString = @"/\w+/";
                            break;
                    }
                }
                else
                {
                    sectionString = "/" + segment + "/";
                }
                queryBuilder.Append(sectionString);
            }
            
            return queryBuilder.ToString().Trim('/');
        }

        protected override void OnHostStarted()
        {
            base.OnHostStarted();

            // whenever the host is created (or recreated) we build a cache map of
            // all http function routes
            HttpFunctions = new Dictionary<string, FunctionDescriptor>();
            foreach (var function in Instance.Functions)
            {
                HttpTriggerBindingMetadata httpTriggerBinding = (HttpTriggerBindingMetadata)function.Metadata.InputBindings.SingleOrDefault(p => p.Type == BindingType.HttpTrigger);
                if (httpTriggerBinding != null)
                {
                    string route = httpTriggerBinding.Route;
                    route = QueryStringToRegexString(route);

                    //if no route found default to the name of the function
                    if (string.IsNullOrEmpty(route))
                    {
                        route = function.Name;
                    }

                    //add a regex prefix to the route based on what performance
                    var methods = httpTriggerBinding.Methods;
                    string methodPrefix = methods == null
                        ? @"/w+"
                        : @"{" + String.Join("|", methods.Select(p => p.Method).ToArray()) + "}";
                    route = methodPrefix + route;

                    HttpFunctions.Add(route.ToLowerInvariant(), function);
                }
            }

            // Purge any old Function secrets
            _secretManager.PurgeOldFiles(Instance.ScriptConfig.RootScriptPath, Instance.TraceWriter);
        }
    }
}