﻿//
// WebRequestHelper.cs
//
// Author:
//       Bojan Rajkovic <bojan.rajkovic@xamarin.com>
//       Michael Hutchinson <mhutch@xamarin.com>
//
// based on NuGet src/Core/Http/RequestHelper.cs
//
// Copyright (c) 2013-2014 Xamarin Inc.
// Copyright (c) 2010-2014 Outercurve Foundation
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MonoDevelop.Core.Web;
using Mono.Addins;
using System.Linq;

namespace MonoDevelop.Core
{
	/// <summary>
	/// Helper for making web requests with support for authenticated proxies.
	/// </summary>
	public static class WebRequestHelper
	{
		const string WebCredentialProvidersPath = "/MonoDevelop/Core/WebCredentialProviders";

		static ProxyCache proxyCache;
		static CredentialStore credentialStore;
		static ICredentialProvider credentialProvider;

		internal static void Initialize ()
		{
			credentialStore = new CredentialStore ();
			proxyCache = new ProxyCache ();
			credentialProvider = AddinManager.GetExtensionObjects<ICredentialProvider> (WebCredentialProvidersPath).FirstOrDefault ();

			if (credentialProvider == null) {
				LoggingService.LogWarning ("No proxy credential provider was found");
				credentialProvider = new NullCredentialProvider ();
			}
		}

		public static ICredentialProvider CredentialProvider { get { return credentialProvider; } }

		[Obsolete]
		public static IProxyAuthenticationHandler ProxyAuthenticationHandler { get; internal set; }

		/// <summary>
		/// Gets the web response, using the <see cref="ProxyAuthenticationHandler"/> to handle proxy authentication
		/// if necessary.
		/// </summary>
		/// <returns>The response.</returns>
		/// <param name="createRequest">Callback for creating the request.</param>
		/// <param name="prepareRequest">Callback for preparing the request, e.g. writing the request stream.</param>
		/// <param name="token">Cancellation token.</param>
		/// <remarks>
		/// Keeps sending requests until a response code that doesn't require authentication happens or if the request
		/// requires authentication and the user has stopped trying to enter them (i.e. they hit cancel when they are prompted).
		/// </remarks>
		public static Task<HttpWebResponse> GetResponseAsync (
			Func<HttpWebRequest> createRequest,
			Action<HttpWebRequest> prepareRequest = null,
			CancellationToken token = default(CancellationToken))
		{
			//TODO: make this really async under the covers
			return Task.Factory.StartNew (() => GetResponse (createRequest, prepareRequest, token), token);
		}

		/// <summary>
		/// Gets the web response, using the <see cref="ProxyAuthenticationHandler"/> to handle proxy authentication
		/// if necessary.
		/// </summary>
		/// <returns>The response.</returns>
		/// <param name="createRequest">Callback for creating the request.</param>
		/// <param name="prepareRequest">Callback for preparing the request, e.g. writing the request stream.</param>
		/// <param name="token">Cancellation token.</param>
		/// <remarks>
		/// Keeps sending requests until a response code that doesn't require authentication happens or if the request
		/// requires authentication and the user has stopped trying to enter them (i.e. they hit cancel when they are prompted).
		/// </remarks>
		public static HttpWebResponse GetResponse (
			Func<HttpWebRequest> createRequest,
			Action<HttpWebRequest> prepareRequest = null,
			CancellationToken token = default(CancellationToken))
		{
			if (prepareRequest == null) {
				prepareRequest = r => {};
			}

			if (credentialProvider == null) {
				var req = createRequest ();
				req.MakeCancelable (token);
				prepareRequest (req);
				return (HttpWebResponse) req.GetResponse ();
			}

			var handler = new RequestHelper (
				createRequest, prepareRequest, proxyCache, credentialStore, credentialProvider
			);

			return handler.GetResponse (token);
		}

		/// <summary>
		/// Determines whether an error code is likely to have been caused by internet reachability problems.
		/// </summary>
		public static bool IsCannotReachInternetError (this WebExceptionStatus status)
		{
			switch (status) {
			case WebExceptionStatus.NameResolutionFailure:
			case WebExceptionStatus.ConnectFailure:
			case WebExceptionStatus.ConnectionClosed:
			case WebExceptionStatus.ProxyNameResolutionFailure:
			case WebExceptionStatus.SendFailure:
			case WebExceptionStatus.Timeout:
				return true;
			default:
				return false;
			}
		}

		static void MakeCancelable (this HttpWebRequest request, CancellationToken token)
		{
			if (!token.CanBeCanceled)
				return;
			token.Register (() => {
				var r = request;
				if (r != null)
					r.Abort ();
			});
		}
	}
}
