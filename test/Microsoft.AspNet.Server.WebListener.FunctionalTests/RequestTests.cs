// Copyright (c) Microsoft Open Technologies, Inc.
// All Rights Reserved
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR
// CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING
// WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR CONDITIONS OF
// TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR
// NON-INFRINGEMENT.
// See the Apache 2 License for the specific language governing
// permissions and limitations under the License.

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNet.FeatureModel;
using Microsoft.AspNet.HttpFeature;
using Microsoft.AspNet.PipelineCore;
using Microsoft.Net.Http.Server;
using Xunit;

namespace Microsoft.AspNet.Server.WebListener
{
    using AppFunc = Func<object, Task>;

    public class RequestTests
    {
        [Fact]
        public async Task Request_SimpleGet_Success()
        {
            string root;
            using (Utilities.CreateHttpServerReturnRoot("/basepath", out root, env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                try
                {
                    // General keys
                    // TODO: Assert.True(env.Get<CancellationToken>("owin.CallCancelled").CanBeCanceled);

                    var requestInfo = httpContext.GetFeature<IHttpRequestFeature>();

                    // Request Keys
                    Assert.Equal("GET", requestInfo.Method);
                    Assert.Equal(Stream.Null, requestInfo.Body);
                    Assert.NotNull(requestInfo.Headers);
                    Assert.Equal("http", requestInfo.Scheme);
                    Assert.Equal("/basepath", requestInfo.PathBase);
                    Assert.Equal("/SomePath", requestInfo.Path);
                    Assert.Equal("?SomeQuery", requestInfo.QueryString);
                    Assert.Equal("HTTP/1.1", requestInfo.Protocol);

                    // Server Keys
                    // TODO: Assert.NotNull(env.Get<IDictionary<string, object>>("server.Capabilities"));

                    var connectionInfo = httpContext.GetFeature<IHttpConnectionFeature>();
                    Assert.Equal("::1", connectionInfo.RemoteIpAddress.ToString());
                    Assert.NotEqual(0, connectionInfo.RemotePort);
                    Assert.Equal("::1", connectionInfo.LocalIpAddress.ToString());
                    Assert.NotEqual(0, connectionInfo.LocalPort);
                    Assert.True(connectionInfo.IsLocal);

                    // Note: Response keys are validated in the ResponseTests
                }
                catch (Exception ex)
                {
                    byte[] body = Encoding.ASCII.GetBytes(ex.ToString());
                    httpContext.Response.Body.Write(body, 0, body.Length);
                }
                return Task.FromResult(0);
            }))
            {
                string response = await SendRequestAsync(root + "/basepath/SomePath?SomeQuery");
                Assert.Equal(string.Empty, response);
            }
        }

        [Theory]
        [InlineData("/", "/", "", "/")]
        [InlineData("/basepath/", "/basepath", "/basepath", "")]
        [InlineData("/basepath/", "/basepath/", "/basepath", "/")]
        [InlineData("/basepath/", "/basepath/subpath", "/basepath", "/subpath")]
        [InlineData("/base path/", "/base%20path/sub path", "/base path", "/sub path")]
        [InlineData("/base葉path/", "/base%E8%91%89path/sub%E8%91%89path", "/base葉path", "/sub葉path")]
        public async Task Request_PathSplitting(string pathBase, string requestPath, string expectedPathBase, string expectedPath)
        {
            string root;
            using (Utilities.CreateHttpServerReturnRoot(pathBase, out root, env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                try
                {
                    var requestInfo = httpContext.GetFeature<IHttpRequestFeature>();
                    var connectionInfo = httpContext.GetFeature<IHttpConnectionFeature>();

                    // Request Keys
                    Assert.Equal("http", requestInfo.Scheme);
                    Assert.Equal(expectedPath, requestInfo.Path);
                    Assert.Equal(expectedPathBase, requestInfo.PathBase);
                    Assert.Equal(string.Empty, requestInfo.QueryString);
                }
                catch (Exception ex)
                {
                    byte[] body = Encoding.ASCII.GetBytes(ex.ToString());
                    httpContext.Response.Body.Write(body, 0, body.Length);
                }
                return Task.FromResult(0);
            }))
            {
                string response = await SendRequestAsync(root + requestPath);
                Assert.Equal(string.Empty, response);
            }
        }

        [Theory]
        // The test server defines these prefixes: "/", "/11", "/2/3", "/2", "/11/2"
        [InlineData("/", "", "/")]
        [InlineData("/random", "", "/random")]
        [InlineData("/11", "/11", "")]
        [InlineData("/11/", "/11", "/")]
        [InlineData("/11/random", "/11", "/random")]
        [InlineData("/2", "/2", "")]
        [InlineData("/2/", "/2", "/")]
        [InlineData("/2/random", "/2", "/random")]
        [InlineData("/2/3", "/2/3", "")]
        [InlineData("/2/3/", "/2/3", "/")]
        [InlineData("/2/3/random", "/2/3", "/random")]
        public async Task Request_MultiplePrefixes(string requestPath, string expectedPathBase, string expectedPath)
        {
            string root;
            using (CreateServer(out root, env =>
            {
                var httpContext = new DefaultHttpContext((IFeatureCollection)env);
                var requestInfo = httpContext.GetFeature<IHttpRequestFeature>();
                try
                {
                    Assert.Equal(expectedPath, requestInfo.Path);
                    Assert.Equal(expectedPathBase, requestInfo.PathBase);
                }
                catch (Exception ex)
                {
                    byte[] body = Encoding.ASCII.GetBytes(ex.ToString());
                    httpContext.Response.Body.Write(body, 0, body.Length);
                }
                return Task.FromResult(0);
            }))
            {
                string response = await SendRequestAsync(root + requestPath);
                Assert.Equal(string.Empty, response);
            }
        }

        private IDisposable CreateServer(out string root, AppFunc app)
        {
            // TODO: We're just doing this to get a dynamic port. This can be removed later when we add support for hot-adding prefixes.
            var server = Utilities.CreateHttpServerReturnRoot("/", out root, app);
            server.Dispose();
            var rootUri = new Uri(root);
            var factory = new ServerFactory(loggerFactory: null);
            var serverInfo = (ServerInformation)factory.Initialize(configuration: null);

            foreach (string path in new[] { "/", "/11", "/2/3", "/2", "/11/2" })
            {
                serverInfo.Listener.UrlPrefixes.Add(UrlPrefix.Create(rootUri.Scheme, rootUri.Host, rootUri.Port, path));
            }

            return factory.Start(serverInfo, app);
        }

        private async Task<string> SendRequestAsync(string uri)
        {
            using (HttpClient client = new HttpClient())
            {
                return await client.GetStringAsync(uri);
            }
        }
    }
}
