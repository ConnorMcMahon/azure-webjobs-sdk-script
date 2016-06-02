// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;

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

        public static Dictionary<string, string> ExtractPathParameters(string path)
        {
            if (path == null)
            {
                return null;
            }
            
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
                string namedParameterString = path.Substring(paramsBeginIndex, path.Length - paramsBeginIndex);
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
    }
}
