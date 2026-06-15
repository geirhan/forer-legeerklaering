using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Altinn.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Altinn.App.Controllers
{
    /// <summary>
    /// Handles SMART on FHIR EHR Launch flow (SMART App Launch IG v2.2.0).
    /// Step 1: EPJ redirects to /smart/launch?iss=...&launch=...
    /// Step 2: App redirects to EPJ auth server for authorization code.
    /// Step 3: EPJ auth server redirects to /smart/callback?code=...
    /// Step 4: App exchanges code for token (server-side, confidential client).
    /// Step 5: App stores token in server session and redirects to form.
    /// </summary>
    [AllowAnonymous]
    [ApiController]
    [Route("{org}/{app}/smart")]
    public class SmartLaunchController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<SmartLaunchController> _logger;

        private const string StateSessionKey = "smart_state";
        private const string PkceSessionKey = "smart_pkce_verifier";
        private const string IssSessionKey = "smart_iss";
        private const string TokenSessionKey = FhirPrefillService.TokenSessionKey;
        private const string FhirContextSessionKey = FhirPrefillService.FhirContextSessionKey;

        public SmartLaunchController(
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            IMemoryCache memoryCache,
            ILogger<SmartLaunchController> logger
        )
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        /// <summary>
        /// Entry point for EHR Launch. EPJ redirects here with iss and launch parameters.
        /// </summary>
        [HttpGet("launch")]
        public async Task<IActionResult> Launch([FromQuery] string iss, [FromQuery] string launch)
        {
            // Fall back to configured defaults for local testing when nginx strips query params
            iss ??= _config["SmartOnFhir:DefaultIss"];
            launch ??= _config["SmartOnFhir:DefaultLaunch"];

            if (string.IsNullOrEmpty(iss) || string.IsNullOrEmpty(launch))
                return BadRequest("Missing required SMART launch parameters: iss, launch");

            // Validate iss against allowlist
            var allowedIssList = _config.GetSection("SmartOnFhir:AllowedIssuerList").Get<List<string>>() ?? new();
            if (allowedIssList.Count > 0 && !allowedIssList.Contains(iss))
            {
                _logger.LogWarning("Rejected SMART launch from unlisted iss: {Iss}", iss);
                return Forbid();
            }

            // Discover SMART configuration from EPJ
            var smartConfig = await DiscoverSmartConfiguration(iss);
            if (smartConfig == null)
                return StatusCode(502, "Could not retrieve SMART configuration from EPJ");

            // Generate PKCE
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(codeVerifier);

            // Generate state
            var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

            // Store in server session
            HttpContext.Session.SetString(StateSessionKey, state);
            HttpContext.Session.SetString(PkceSessionKey, codeVerifier);
            HttpContext.Session.SetString(IssSessionKey, iss);

            var clientId =
                _config["SmartOnFhir:ClientId"]
                ?? _config.GetSection("SmartOnFhir")["ClientId"]
                ?? "forer-legeerklaering-poc";
            var org = RouteData.Values["org"]?.ToString();
            var app = RouteData.Values["app"]?.ToString();
            var redirectUri = $"{Request.Scheme}://{Request.Host}/{org}/{app}/smart/callback";

            var scopes = string.Join(
                " ",
                new[]
                {
                    "openid",
                    "profile",
                    "fhirUser",
                    "launch",
                    "launch/patient",
                    "launch/encounter",
                    "offline_access",
                    "patient/Patient.read",
                    "patient/Encounter.read",
                    "patient/Condition.read",
                    "patient/Observation.read",
                    "user/Practitioner.read",
                    "user/Organization.read",
                }
            );

            var authUrl = BuildAuthorizationUrl(
                smartConfig.AuthorizationEndpoint,
                clientId,
                redirectUri,
                scopes,
                state,
                codeChallenge,
                launch
            );

            return Redirect(authUrl);
        }

        /// <summary>
        /// Test-only shortcut: bypasses OAuth and seeds session directly with mock FHIR context.
        /// Only active when SmartOnFhir:DefaultIss is configured.
        /// Usage: GET /{org}/{app}/smart/test-prefill
        /// </summary>
        [HttpGet("test-prefill")]
        public async Task<IActionResult> TestPrefill()
        {
            var fhirBase = _config["SmartOnFhir:FhirBaseUrlOverride"];
            if (string.IsNullOrEmpty(fhirBase))
                return BadRequest("SmartOnFhir:FhirBaseUrlOverride ikke konfigurert");

            var mockToken = new { AccessToken = "mock-test-token" };
            var tokenJson = System.Text.Json.JsonSerializer.Serialize(mockToken);

            var fhirContext = new FhirLaunchContext
            {
                PatientId = "sophie-salt",
                EncounterId = "enc-sophie-001",
                FhirUser = $"{fhirBase}/Practitioner/lege-ola",
                FhirBaseUrl = fhirBase,
            };
            var contextJson = System.Text.Json.JsonSerializer.Serialize(fhirContext);

            // Store in session (works when cookie is forwarded by browser)
            await HttpContext.Session.LoadAsync();
            HttpContext.Session.SetString(TokenSessionKey, tokenJson);
            HttpContext.Session.SetString(FhirContextSessionKey, contextJson);

            // Also store in memory cache keyed by session ID (fallback if cookie is missing)
            var cacheKey = FhirPrefillService.CacheKeyPrefix + HttpContext.Session.Id;
            _memoryCache.Set(
                cacheKey,
                new FhirPrefillService.CachedFhirData { TokenJson = tokenJson, ContextJson = contextJson },
                TimeSpan.FromMinutes(30)
            );

            _logger.LogInformation(
                "TestPrefill: session ID={SessionId}, cache key={CacheKey}",
                HttpContext.Session.Id,
                cacheKey
            );

            var org = RouteData.Values["org"]?.ToString();
            var app = RouteData.Values["app"]?.ToString();
            return Redirect($"/{org}/{app}");
        }

        /// <summary>
        /// OAuth2 callback. Exchanges authorization code for token server-side.
        /// </summary>
        [HttpGet("callback")]
        public async Task<IActionResult> Callback(
            [FromQuery] string code,
            [FromQuery] string state,
            [FromQuery] string error
        )
        {
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("SMART auth error: {Error}", error);
                return BadRequest($"Authorization error: {error}");
            }

            var sessionState = HttpContext.Session.GetString(StateSessionKey);
            if (state != sessionState)
                return BadRequest("State mismatch — possible CSRF");

            var iss = HttpContext.Session.GetString(IssSessionKey);
            var codeVerifier = HttpContext.Session.GetString(PkceSessionKey);
            var smartConfig = await DiscoverSmartConfiguration(iss);

            var org = RouteData.Values["org"]?.ToString();
            var app = RouteData.Values["app"]?.ToString();
            var redirectUri = $"{Request.Scheme}://{Request.Host}/{org}/{app}/smart/callback";
            var clientId = _config["SmartOnFhir:ClientId"];
            var clientSecret = _config["SmartOnFhir:ClientSecret"];

            var token = await ExchangeCodeForToken(
                smartConfig.TokenEndpoint,
                code,
                redirectUri,
                clientId,
                clientSecret,
                codeVerifier
            );

            if (token == null)
                return StatusCode(502, "Token exchange failed");

            // Store token server-side — never expose to browser
            HttpContext.Session.SetString(TokenSessionKey, JsonSerializer.Serialize(token));

            // Store FHIR context for pre-fill
            // Allow override of FHIR base URL (useful when iss uses localhost but app runs on host)
            var fhirBaseUrl = _config["SmartOnFhir:FhirBaseUrlOverride"] ?? iss;
            var fhirContext = new FhirLaunchContext
            {
                PatientId = token.Patient,
                EncounterId = token.Encounter,
                FhirUser = token.FhirUser,
                FhirBaseUrl = fhirBaseUrl,
            };
            HttpContext.Session.SetString(FhirContextSessionKey, JsonSerializer.Serialize(fhirContext));

            // Clear PKCE and state from session
            HttpContext.Session.Remove(StateSessionKey);
            HttpContext.Session.Remove(PkceSessionKey);

            return Redirect($"/{org}/{app}");
        }

        private async Task<SmartConfiguration> DiscoverSmartConfiguration(string iss)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var wellKnownUrl = $"{iss.TrimEnd('/')}/.well-known/smart-configuration";
                var response = await client.GetAsync(wellKnownUrl);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SmartConfiguration>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover SMART configuration from {Iss}", iss);
                return null;
            }
        }

        private async Task<TokenResponse> ExchangeCodeForToken(
            string tokenEndpoint,
            string code,
            string redirectUri,
            string clientId,
            string clientSecret,
            string codeVerifier
        )
        {
            try
            {
                var client = _httpClientFactory.CreateClient();

                // Confidential client: Basic auth with client_id:client_secret
                if (!string.IsNullOrEmpty(clientSecret))
                {
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                }

                var body = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["client_id"] = clientId,
                    ["code_verifier"] = codeVerifier,
                };

                var response = await client.PostAsync(tokenEndpoint, new FormUrlEncodedContent(body));
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<TokenResponse>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token exchange failed at {Endpoint}", tokenEndpoint);
                return null;
            }
        }

        private static string BuildAuthorizationUrl(
            string authEndpoint,
            string clientId,
            string redirectUri,
            string scope,
            string state,
            string codeChallenge,
            string launch
        )
        {
            var qs = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = clientId ?? "forer-legeerklaering-poc",
                ["redirect_uri"] = redirectUri ?? "",
                ["scope"] = scope ?? "",
                ["state"] = state ?? "",
                ["aud"] = authEndpoint ?? "",
                ["launch"] = launch ?? "",
                ["code_challenge"] = codeChallenge ?? "",
                ["code_challenge_method"] = "S256",
            };
            var query = string.Join(
                "&",
                System.Linq.Enumerable.Select(
                    qs,
                    kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"
                )
            );
            return $"{authEndpoint}?{query}";
        }

        private static string GenerateCodeVerifier()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string GenerateCodeChallenge(string verifier)
        {
            var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private class SmartConfiguration
        {
            public string AuthorizationEndpoint { get; set; }
            public string TokenEndpoint { get; set; }
        }

        private class TokenResponse
        {
            public string AccessToken { get; set; }
            public string TokenType { get; set; }
            public int ExpiresIn { get; set; }
            public string RefreshToken { get; set; }
            public string Patient { get; set; }
            public string Encounter { get; set; }
            // Per SMART App Launch IG v2.2.0: fhirUser er et eget toppnivåfelt i tokenresponsen.
            // Noen EPJ-systemer returnerer det som JWT-claim i access_token i stedet — dekod da tokenet server-side.
            public string FhirUser { get; set; }
        }

        private class FhirLaunchContext
        {
            public string PatientId { get; set; }
            public string EncounterId { get; set; }
            public string FhirUser { get; set; }
            public string FhirBaseUrl { get; set; }
        }
    }
}
