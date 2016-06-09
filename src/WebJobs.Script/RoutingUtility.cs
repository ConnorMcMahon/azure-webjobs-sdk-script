using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class RoutingUtility
    {
        public static IDictionary<string, string> ExtractQueryParameterTypes(string queryTemplate)
        {
            Dictionary<string, string> pathParameters = new Dictionary<string, string>();
            if (queryTemplate == null)
            {
                return null;
            }
            queryTemplate = queryTemplate.Trim('/');

            string[] queryParts = queryTemplate.Split('?');
            string routeTemplate = queryParts[0];
            string queryParameters = null;
            if (queryParts.Length == 2)
            {
                queryParameters = queryParts[1];
            }

            string[] routeSegments = routeTemplate.Split('/');
            //extract parameter types embedded within the route
            foreach (string segment in routeSegments)
            {
                if (segment.Length == 0)
                {
                    //two consecutive '/' characters, 
                    //todo: probably create an exception
                    return null;
                }
                //if the segment starts with an open brace, then this segment describes a parameter
                if (segment.StartsWith("{", StringComparison.OrdinalIgnoreCase) &&
                     segment.EndsWith("}", StringComparison.OrdinalIgnoreCase))
                {
                    //grab everything in the segment except the first and last character i.e. the braces
                    string[] parameterParts = segment.Substring(1, segment.Length - 2).Split(':');
                    if (parameterParts.Length == 2 && parameterParts[0].Length != 0 && parameterParts[1].Length != 0)
                    {
                        //parameter of form 'parameterName:parameterType'
                        pathParameters.Add(parameterParts[0], parameterParts[1]);
                    }
                    else if (parameterParts.Length == 1 && parameterParts[0].Length != 0)
                    {
                        //parameter is of the form 'parameterName', so just default it to string
                        pathParameters.Add(parameterParts[0], "string");
                    }
                    else
                    {
                        //parameter not of format {paramName:paramValue} or {paramName}
                        //todo: probably create an exception
                        return null;
                    }
                }
            }

            //extract traditional query string parameter types
            if (queryParameters != null)
            {
                var queryParameterTypes = HttpUtility.ParseQueryString(queryParameters);
                foreach (var parameter in queryParameterTypes.AllKeys)
                {
                    pathParameters.Add(parameter, queryParameterTypes[parameter]);
                }
            }

            return pathParameters;
        }

        private static object CoerceArgumentType(string typeName, string value)
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

        public static string ExtractRouteFromMetadata(FunctionMetadata metadata)
        {
            try
            {
                var inputBindings = metadata.InputBindings;
                var trigger = inputBindings.First(p => p.Type == BindingType.HttpTrigger);
                return ((HttpTriggerBindingMetadata) trigger).Route;
            }
            catch
            {
                return null;
            }

        }

        public static IDictionary<string, object> ExtractQueryArguments(string template, HttpRequestMessage request)
        {
            IDictionary<string, object> arguments = new Dictionary<string, object>();

            if (template == null)
            {
                return arguments;
            }

            string[] templateParts = template.Split('?');
            string routeTemplate = templateParts[0];
            string paramsTemplate = null;
            if (templateParts.Length == 2)
            {
                paramsTemplate = templateParts[1];
            }

            string requestUri = request.RequestUri.AbsolutePath;
            int idx = requestUri.ToLowerInvariant().IndexOf("api", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                idx = requestUri.IndexOf('/', idx);
                requestUri = requestUri.Substring(idx + 1).Trim('/');
            }
            routeTemplate = routeTemplate.Trim('/');

            if (routeTemplate != requestUri)
            {
                //there are parameters embedded in the route, so parse the route template alongside the
                //request URI to find a match for these parameters
                string[] templateSegments = routeTemplate.Split('/');
                string[] uriSegments = requestUri.Split('/');
                //extract route parameters
                if (templateSegments.Length != uriSegments.Length)
                {
                    //the uri does not match the template
                    return null;
                }
                for (int i = 0; i < templateSegments.Length; i++)
                {
                    string templateSegment = templateSegments[i];
                    string uriSegment = uriSegments[i];
                    if (templateSegment.Length == 0 || uriSegment.Length == 0)
                    {
                        //two consecutive '/' characters, 
                        //todo: probably create an exception
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
            }

            //extract traditional query string parameters
            string absoluteUri = request.RequestUri.AbsoluteUri;
            int paramsBeginQueryIndex = absoluteUri.IndexOf("?", StringComparison.OrdinalIgnoreCase);
            if (paramsBeginQueryIndex > 0 && paramsTemplate != null)
            {
                string namedParameterQueryString = absoluteUri.Substring(paramsBeginQueryIndex + 1,
                    absoluteUri.Length - paramsBeginQueryIndex - 1);

                var queryParameters = HttpUtility.ParseQueryString(namedParameterQueryString);
                var parameterTypes = HttpUtility.ParseQueryString(paramsTemplate);
                foreach (var parameter in queryParameters.AllKeys)
                {
                    string type = parameterTypes.Get(parameter);
                    if (type != null)
                    {
                        arguments.Add(parameter, CoerceArgumentType(type, queryParameters[parameter]));
                    }
                    //todo: how to handle queryParameters that do not exist in route template, as making them arguments could create issues
                }

            }

            return arguments;
        }
    }
}