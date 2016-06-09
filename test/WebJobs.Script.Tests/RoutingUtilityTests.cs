// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class RoutingUtilityTests
    {
        [Fact]
        public void ExtractQueryParameterTypes_ValidInput()
        {
            IDictionary<string, string> oracleDictionary = new Dictionary<string, string>();
            oracleDictionary.Add("add", "int");
            oracleDictionary.Add("log", "bool");
            oracleDictionary.Add("typeless", "string");

            Assert.Equal(oracleDictionary, RoutingUtility.ExtractQueryParameterTypes("/{add:int}/route/name/{typeless}?log=bool"));
        }

        [Fact]
        public void ExtractQueryParameterTypes_InvalidInput()
        {
            Assert.Null(RoutingUtility.ExtractQueryParameterTypes("/{a:}/route/name?log=bool"));
            Assert.Null(RoutingUtility.ExtractQueryParameterTypes("/{}/route/name?log=bool"));
        }

        [Fact]
        public void ExtractQueryArguments_AllTypesPresent()
        {
            var baseUri = new Uri("http://localhost/api");
            var relativeUri = new Uri("5/counterapi?log=true&message=helloworld", UriKind.Relative);
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativeUri));
            var routeArguments = RoutingUtility.ExtractQueryArguments("{add:int}/counterapi?log=bool&message=string",
                request);

            var expectedDictionary = new Dictionary<string, object>();
            expectedDictionary.Add("add", 5);
            expectedDictionary.Add("log", true);
            expectedDictionary.Add("message", "helloworld");

            Assert.Equal(expectedDictionary, routeArguments);
        }

        [Fact]
        public void ExtractRouteFromMetdataWithNoRoute()
        {
            var mockMetadata = new Mock<FunctionMetadata>();
            var sampleName = RoutingUtility.ExtractRouteFromMetadata(mockMetadata.Object);
            Assert.Null(sampleName);
        }
    }
}
