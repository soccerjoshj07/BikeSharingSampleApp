﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// --------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace app
{
    public static class HttpHelper
    {
        private static HttpClient _httpClient = new HttpClient();

        public static Task<HttpResponseMessage> GetAsync(Guid requestId, string url, HttpRequest originRequest)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(url)
            };

            return SendAndLogAsync(requestId, request, originRequest);
        }

        public static Task<HttpResponseMessage> PostAsync(Guid requestId, string url, HttpContent content, HttpRequest originRequest)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(url),
                Content = content
            };

            return SendAndLogAsync(requestId, request, originRequest);
        }

        public static Task<HttpResponseMessage> PatchAsync(Guid requestId, string url, HttpContent content, HttpRequest originRequest)
        {
            var request = new HttpRequestMessage
            {
                Method = new HttpMethod("PATCH"),
                RequestUri = new Uri(url),
                Content = content
            };

            return SendAndLogAsync(requestId, request, originRequest);
        }

        private static async Task<HttpResponseMessage> SendAndLogAsync(Guid requestId, HttpRequestMessage request, HttpRequest originRequest)
        {
            Stopwatch stopWatch = Stopwatch.StartNew();
            request.Headers.Add(Constants.RequestIdHeaderName, requestId.ToString());
            if (originRequest.Headers.ContainsKey(Constants.RouteAsHeaderName))
            {
                request.Headers.Add(Constants.RouteAsHeaderName, originRequest.Headers[Constants.RouteAsHeaderName].ToArray());
            }
            var response = await _httpClient.SendAsync(request);
            LogUtility.LogWithContext(requestId, "Dependency: {0} {1} - {2} - {3}ms", request.Method.Method, request.RequestUri.ToString(), response.StatusCode.ToString(), stopWatch.ElapsedMilliseconds.ToString());
            return response;
        }
    }
}