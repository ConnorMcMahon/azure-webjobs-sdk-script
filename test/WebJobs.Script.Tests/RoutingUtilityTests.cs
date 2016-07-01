// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class RoutingUtilityTests
    {
        [Fact]
        public void ExtractQueryArguments_AllTypesPresent()
        {
            var baseUri = new Uri("http://localhost/api/");
            var relativeUri = new Uri("mainpage/5/true", UriKind.Relative);
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativeUri));
            var routeArguments = RoutingUtility.ExtractRouteParameters("{countername}/{add:int}/{log:bool}",
                request);

            var expectedDictionary = new RouteValueDictionary();
            expectedDictionary.Add("countername", "mainpage");
            expectedDictionary.Add("add", 5);
            expectedDictionary.Add("log", true);
            
            Assert.Equal(expectedDictionary, routeArguments);
        }

        [Fact]
        public void ExtractRouteFromMetdataWithNoRoute()
        {
            var mockMetadata = new Mock<FunctionMetadata>();
            var sampleName = RoutingUtility.ExtractRouteTemplateFromMetadata(mockMetadata.Object);
            Assert.Null(sampleName);
        }
    }
}
