using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Altinn.App.Core.Features;
using Altinn.App.Core.Models;
using Altinn.App.Models;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Altinn.App.Services
{
    /// <summary>
    /// Pre-fills ForerLegeerklaeringModel with data from FHIR resources
    /// fetched from the EPJ using the SMART access token stored in server session.
    /// Falls back to IMemoryCache if session is not available.
    /// </summary>
    public class FhirPrefillService : IDataProcessor
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<FhirPrefillService> _logger;

        public const string TokenSessionKey = "smart_token";
        public const string FhirContextSessionKey = "smart_fhir_context";
        public const string CacheKeyPrefix = "smart_fhir_";

        public FhirPrefillService(
            IHttpClientFactory httpClientFactory,
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            ILogger<FhirPrefillService> logger
        )
        {
            _httpClientFactory = httpClientFactory;
            _httpContextAccessor = httpContextAccessor;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public async Task ProcessDataRead(Instance instance, Guid? dataId, object data, string? language = null)
        {
            _logger.LogInformation("FhirPrefillService.ProcessDataRead called for instance {InstanceId}", instance?.Id);

            if (data is not ForerLegeerklaeringModel model)
            {
                _logger.LogInformation("Data is not ForerLegeerklaeringModel — skipping");
                return;
            }

            string tokenJson = null;
            string contextJson = null;

            // 1. Try session (requires session middleware + cookie from browser)
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session != null)
            {
                await session.LoadAsync();
                tokenJson = session.GetString(TokenSessionKey);
                contextJson = session.GetString(FhirContextSessionKey);
                _logger.LogInformation(
                    "Session loaded — token present: {HasToken}, context present: {HasContext}",
                    !string.IsNullOrEmpty(tokenJson),
                    !string.IsNullOrEmpty(contextJson)
                );
            }

            // 2. Fall back to memory cache keyed by session ID
            if ((string.IsNullOrEmpty(tokenJson) || string.IsNullOrEmpty(contextJson)) && session != null)
            {
                var cacheKey = CacheKeyPrefix + session.Id;
                if (_memoryCache.TryGetValue(cacheKey, out CachedFhirData cached))
                {
                    _logger.LogInformation("Found FHIR context in memory cache (key: {Key})", cacheKey);
                    tokenJson = cached.TokenJson;
                    contextJson = cached.ContextJson;
                }
            }

            if (string.IsNullOrEmpty(tokenJson) || string.IsNullOrEmpty(contextJson))
            {
                _logger.LogInformation("No SMART context found in session or cache — skipping FHIR pre-fill");
                return;
            }

            var token = JsonSerializer.Deserialize<TokenData>(tokenJson);
            var context = JsonSerializer.Deserialize<FhirLaunchContext>(contextJson);
            _logger.LogInformation(
                "Starting FHIR pre-fill: patient={Patient}, encounter={Encounter}",
                context?.PatientId,
                context?.EncounterId
            );

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

            await FillPatient(client, context, model);
            await FillPractitioner(client, context, model);
            await FillEncounterAndOrganization(client, context, model);
            await FillCondition(client, context, model);
        }

        private async Task<JsonDocument> TryGetFhirResource(HttpClient client, string url, string resourceLabel)
        {
            try
            {
                var json = await client.GetStringAsync(url);
                return JsonDocument.Parse(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch FHIR resource {Label} from {Url}", resourceLabel, url);
                return null;
            }
        }

        private async Task FillPatient(HttpClient client, FhirLaunchContext ctx, ForerLegeerklaeringModel model)
        {
            if (string.IsNullOrEmpty(ctx.PatientId))
                return;
            using var doc = await TryGetFhirResource(client, $"{ctx.FhirBaseUrl}/Patient/{ctx.PatientId}", "Patient");
            if (doc == null)
                return;
            var root = doc.RootElement;

            model.Pasient_Fnr = GetIdentifier(root, "urn:oid:2.16.578.1.12.4.1.4.1");
            model.Pasient_Fodselsdato = root.TryGetProperty("birthDate", out var bd) ? bd.GetString() : null;
            model.Pasient_Kjonn = root.TryGetProperty("gender", out var g) ? g.GetString() : null;

            if (root.TryGetProperty("name", out var names) && names.GetArrayLength() > 0)
            {
                var name = names[0];
                model.Pasient_Fornavn =
                    name.TryGetProperty("given", out var given) && given.GetArrayLength() > 0
                        ? given[0].GetString()
                        : null;
                model.Pasient_Etternavn = name.TryGetProperty("family", out var family) ? family.GetString() : null;
            }
        }

        private async Task FillPractitioner(HttpClient client, FhirLaunchContext ctx, ForerLegeerklaeringModel model)
        {
            // fhirUser is a full URL reference to the Practitioner resource
            if (string.IsNullOrEmpty(ctx.FhirUser))
                return;
            using var doc = await TryGetFhirResource(client, ctx.FhirUser, "Practitioner");
            if (doc == null)
                return;
            var root = doc.RootElement;

            model.Lege_HPR = GetIdentifier(root, "urn:oid:2.16.578.1.12.4.1.4.4");

            // B-4: Also try PractitionerRole to get organizational affiliation
            var practitionerId = root.TryGetProperty("id", out var pid) ? pid.GetString() : null;
            if (!string.IsNullOrEmpty(practitionerId))
                await FillPractitionerRole(client, ctx, practitionerId, model);

            if (root.TryGetProperty("name", out var names) && names.GetArrayLength() > 0)
            {
                var name = names[0];
                model.Lege_Fornavn =
                    name.TryGetProperty("given", out var given) && given.GetArrayLength() > 0
                        ? given[0].GetString()
                        : null;
                model.Lege_Etternavn = name.TryGetProperty("family", out var family) ? family.GetString() : null;
            }
        }

        private async Task FillPractitionerRole(
            HttpClient client,
            FhirLaunchContext ctx,
            string practitionerId,
            ForerLegeerklaeringModel model
        )
        {
            var url = $"{ctx.FhirBaseUrl}/PractitionerRole?practitioner={practitionerId}&_count=1";
            using var doc = await TryGetFhirResource(client, url, "PractitionerRole");
            if (doc == null)
                return;
            var root = doc.RootElement;

            if (!root.TryGetProperty("entry", out var entries) || entries.GetArrayLength() == 0)
                return;

            var role = entries[0].GetProperty("resource");
            if (
                role.TryGetProperty("organization", out var orgRef)
                && orgRef.TryGetProperty("reference", out var orgRefVal)
            )
            {
                var orgUrl = orgRefVal.GetString();
                if (!string.IsNullOrEmpty(orgUrl))
                {
                    if (!orgUrl.StartsWith("http"))
                        orgUrl = $"{ctx.FhirBaseUrl}/{orgUrl}";
                    await FillOrganization(client, orgUrl, model);
                }
            }
        }

        private async Task FillEncounterAndOrganization(
            HttpClient client,
            FhirLaunchContext ctx,
            ForerLegeerklaeringModel model
        )
        {
            if (string.IsNullOrEmpty(ctx.EncounterId))
                return;
            using var doc = await TryGetFhirResource(
                client,
                $"{ctx.FhirBaseUrl}/Encounter/{ctx.EncounterId}",
                "Encounter"
            );
            if (doc == null)
                return;
            var root = doc.RootElement;

            // Konsultasjonsdato
            if (root.TryGetProperty("period", out var period) && period.TryGetProperty("start", out var start))
                model.Konsultasjon_Dato = start.GetString();

            // Fall back to Encounter.serviceProvider for org if PractitionerRole lookup didn't populate it
            if (
                string.IsNullOrEmpty(model.Virksomhet_Navn)
                && root.TryGetProperty("serviceProvider", out var sp)
                && sp.TryGetProperty("reference", out var orgRef)
            )
            {
                var orgUrl = orgRef.GetString();
                if (!orgUrl.StartsWith("http"))
                    orgUrl = $"{ctx.FhirBaseUrl}/{orgUrl}";
                await FillOrganization(client, orgUrl, model);
            }
        }

        private async Task FillOrganization(HttpClient client, string orgUrl, ForerLegeerklaeringModel model)
        {
            using var doc = await TryGetFhirResource(client, orgUrl, "Organization");
            if (doc == null)
                return;
            var root = doc.RootElement;

            model.Virksomhet_Orgnr = GetIdentifier(root, "urn:oid:2.16.578.1.12.4.1.4.101");
            model.Virksomhet_HerId = GetIdentifier(root, "urn:oid:2.16.578.1.12.4.1.2");
            model.Virksomhet_Navn = root.TryGetProperty("name", out var name) ? name.GetString() : null;
        }

        private async Task FillCondition(HttpClient client, FhirLaunchContext ctx, ForerLegeerklaeringModel model)
        {
            if (string.IsNullOrEmpty(ctx.PatientId))
                return;

            var url =
                $"{ctx.FhirBaseUrl}/Condition?patient={ctx.PatientId}&clinical-status=active&_sort=-recorded-date&_count=1";
            if (!string.IsNullOrEmpty(ctx.EncounterId))
                url += $"&encounter={ctx.EncounterId}";

            using var doc = await TryGetFhirResource(client, url, "Condition");
            if (doc == null)
                return;
            var root = doc.RootElement;

            if (!root.TryGetProperty("entry", out var entries) || entries.GetArrayLength() == 0)
                return;

            var condition = entries[0].GetProperty("resource");
            if (
                condition.TryGetProperty("code", out var code)
                && code.TryGetProperty("coding", out var codings)
                && codings.GetArrayLength() > 0
            )
            {
                var coding = codings[0];
                model.Diagnose_Kode = coding.TryGetProperty("code", out var c) ? c.GetString() : null;
                model.Diagnose_Tekst = coding.TryGetProperty("display", out var d) ? d.GetString() : null;
            }
        }

        private static string GetIdentifier(JsonElement resource, string system)
        {
            if (!resource.TryGetProperty("identifier", out var identifiers))
                return null;
            foreach (var id in identifiers.EnumerateArray())
            {
                if (
                    id.TryGetProperty("system", out var sys)
                    && sys.GetString() == system
                    && id.TryGetProperty("value", out var val)
                )
                    return val.GetString();
            }
            return null;
        }

        public Task ProcessDataWrite(
            Instance instance,
            Guid? dataId,
            object data,
            object? previousData,
            string? language = null
        ) => Task.CompletedTask;

        private class TokenData
        {
            public string AccessToken { get; set; }
        }

        private class FhirLaunchContext
        {
            public string PatientId { get; set; }
            public string EncounterId { get; set; }
            public string FhirUser { get; set; }
            public string FhirBaseUrl { get; set; }
        }

        public class CachedFhirData
        {
            public string TokenJson { get; set; }
            public string ContextJson { get; set; }
        }
    }
}
