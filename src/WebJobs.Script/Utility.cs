// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.AspNet.Routing;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class Utility
    {
        public static string GetFunctionShortName(string functionName)
        {
            int idx = functionName.LastIndexOf('.');
            if (idx > 0)
            {
                functionName = functionName.Substring(idx + 1);
            }

            return functionName;
        }

        public static string FlattenException(Exception ex)
        {
            StringBuilder flattenedErrorsBuilder = new StringBuilder();
            string lastError = null;

            if (ex is AggregateException)
            {
                ex = ex.InnerException;
            }

            do
            {
                StringBuilder currentErrorBuilder = new StringBuilder();
                if (!string.IsNullOrEmpty(ex.Source))
                {
                    currentErrorBuilder.AppendFormat("{0}: ", ex.Source);
                }

                currentErrorBuilder.Append(ex.Message);

                if (!ex.Message.EndsWith("."))
                {
                    currentErrorBuilder.Append(".");
                }

                // sometimes inner exceptions are exactly the same
                // so first check before duplicating
                string currentError = currentErrorBuilder.ToString();
                if (lastError == null ||
                    string.Compare(lastError.Trim(), currentError.Trim()) != 0)
                {
                    if (flattenedErrorsBuilder.Length > 0)
                    {
                        flattenedErrorsBuilder.Append(" ");
                    }
                    flattenedErrorsBuilder.Append(currentError);
                }

                lastError = currentError;
            }
            while ((ex = ex.InnerException) != null);

            return flattenedErrorsBuilder.ToString();
        }

        public static string GetAppSettingOrEnvironmentValue(string name)
        {
            // first check app settings
            string value = ConfigurationManager.AppSettings[name];
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            // Check environment variables
            value = Environment.GetEnvironmentVariable(name);
            if (value != null)
            {
                return value;
            }

            return null;
        }

        public static bool IsJson(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            input = input.Trim();
            return (input.StartsWith("{", StringComparison.OrdinalIgnoreCase) && input.EndsWith("}", StringComparison.OrdinalIgnoreCase))
                || (input.StartsWith("[", StringComparison.OrdinalIgnoreCase) && input.EndsWith("]", StringComparison.OrdinalIgnoreCase));
        }

        public static IDictionary<string, string> ExtractPathParameterTypes(string path)
        {
            if (path == null)
            {
                return null;
            }
            path = path.Trim('/');

            Dictionary<string, string> pathParameters = new Dictionary<string, string>();
            string[] routeSegments = path.Split('/');
            //extract route parameters
            foreach (string segment in routeSegments)
            {
                if (segment.Length == 0)
                {
                    //two consecutive '/' characters, 
                    return null;
                }
                //if the segment starts with an open brace, then this segment describes a parameter
                if (segment.StartsWith("{", StringComparison.OrdinalIgnoreCase) &&
                     segment.EndsWith("}", StringComparison.OrdinalIgnoreCase))
                {
                    //grab everything in the segment except the first and last character i.e. the braces
                    string[] parameterParts = segment.Substring(1, segment.Length-2).Split(':');
                    if (parameterParts.Length == 2 && parameterParts[0].Length != 0 && parameterParts[1].Length != 0)
                    {
                        pathParameters.Add(parameterParts[0], parameterParts[1]);
                    }
                    else if (parameterParts.Length == 1)
                    {
                        pathParameters.Add(parameterParts[0], "string");
                    }
                    else
                    {
                        //parameter not of format {paramName:paramValue} or {paramName}
                        return null;
                    }
                }
            }

            //extract query string parameters
            int paramsBeginIndex = path.IndexOf("?", StringComparison.OrdinalIgnoreCase);
            if (paramsBeginIndex > 0)
            {
                string namedParameterString = path.Substring(paramsBeginIndex+1, path.Length - paramsBeginIndex-1);
                string[] parameters = namedParameterString.Split('&');
                foreach(string parameter in parameters)
                {
                    string[] paramParts = parameter.Split('=');
                    if (paramParts.Length != 2 || paramParts[0].Length == 0 || paramParts[1].Length == 0)
                    {
                        //not of the form paramName=paramValue
                        return null;
                    }
                    pathParameters.Add(paramParts[0], paramParts[1]);
                }
            } 
            
            return pathParameters;
        }

        public static IDictionary<string, string> FindQueryParameters(FunctionMetadata metadata)
        {
            var inputBindings = metadata.InputBindings;
            var httpTriggers = inputBindings.Where(p => p.Type == BindingType.HttpTrigger);
            var queryParameterPossibilities = new Collection<IDictionary<string, string>>();
            foreach (BindingMetadata trigger in httpTriggers)
            {
                string routeTemplate = ((HttpTriggerBindingMetadata) trigger).Route;
                if (routeTemplate != null)
                {
                    //add the query parameters of this http trigger to this list of possible parameters
                    queryParameterPossibilities.Add(ExtractPathParameterTypes(routeTemplate));
                }
            }
            try
            {
                var finalParameters = queryParameterPossibilities.SelectMany(dict => dict)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                return finalParameters;
            }
            catch
            {
                //TODO: create exception for multiple types for one parameter
                return null;
            }
        }

        private static Object CoerceArgumentType(string typeName, string value)
        {
            switch (typeName)
            {
                case "int":
                    return int.Parse(value);
                case "bool":
                    return bool.Parse(value);
                default:
                    return value;
            }
        }

        public static IDictionary<string, object> ExtractQueryArguments(FunctionMetadata metadata, HttpRequestMessage request)
        {
            IDictionary<string, object> arguments = new Dictionary<string, object>();
            //obtain types for each of the parameter names
            var inputBindings = metadata.InputBindings;
            var trigger = inputBindings.Where(p => p.Type == BindingType.HttpTrigger).First();
            string routeTemplate = ((HttpTriggerBindingMetadata)trigger).Route;
            if (routeTemplate == null)
            {
                return null;
            }
            var types = ExtractPathParameterTypes(routeTemplate);

            //parse through the requestUri to ensure it matches route template
            string requestUri = request.RequestUri.AbsoluteUri;
            int idx = requestUri.ToLowerInvariant().IndexOf("api", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                idx = requestUri.IndexOf('/', idx);
                requestUri = requestUri.Substring(idx + 1).Trim('/');
            }

            routeTemplate = routeTemplate.Trim('/');

            string[] templateSegments = routeTemplate.Split('/');
            string[] uriSegments = requestUri.Split('/');
            //extract route parameters
            if (templateSegments.Length != uriSegments.Length)
            {
                //the uri does not match the template
                return null;
            }
            for(int i = 0; i < templateSegments.Length; i++)
            {
                string templateSegment = templateSegments[i];
                string uriSegment = uriSegments[i];
                if (templateSegment.Length == 0)
                {
                    //two consecutive '/' characters, 
                    return null;
                }
                if (templateSegment.Equals(uriSegment))
                {
                    //this segment is not a parameter
                    continue;
                }
                //if the segment starts with an open brace, then this segment describes a parameter
                if (templateSegment.StartsWith("{", StringComparison.OrdinalIgnoreCase) &&
                     templateSegment.EndsWith("}", StringComparison.OrdinalIgnoreCase))
                {
                    //grab everything in the segment except the first and last character i.e. the braces
                    string[] parameterParts = templateSegment.Substring(1, templateSegment.Length - 2).Split(':');
                    if (parameterParts.Length == 2 && parameterParts[0].Length != 0 && parameterParts[1].Length != 0)
                    {
                        arguments.Add(parameterParts[0], CoerceArgumentType(parameterParts[1], uriSegment));
                    }
                    else if (parameterParts.Length == 1)
                    {
                        arguments.Add(parameterParts[0], uriSegment);
                    }
                    else
                    {
                        //parameter not of format {paramName:paramValue} or {paramName}
                        return null;
                    }
                }
            }

            //extract query string parameters
            int paramsBeginIndex = requestUri.IndexOf("?", StringComparison.OrdinalIgnoreCase);
            if (paramsBeginIndex > 0)
            {
                string namedParameterString = requestUri.Substring(paramsBeginIndex+1, requestUri.Length - paramsBeginIndex-1);
                string[] parameters = namedParameterString.Split('&');
                foreach (string parameter in parameters)
                {
                    string[] parameterParts = parameter.Split('=');
                    if (parameterParts.Length != 2 || parameterParts[0].Length == 0 || parameterParts[1].Length == 0)
                    {
                        //not of the form paramName=paramValue
                        return null;
                    }
                    string type = null;
                    if (types.TryGetValue(parameterParts[0], out type))
                    {
                        arguments.Add(parameterParts[0], CoerceArgumentType(type, parameterParts[1]));
                    }
                 
                }
            }
            return arguments;
        }
    }
}
