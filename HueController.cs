using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Q42.HueApi;
using Q42.HueApi.Interfaces;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Q42.HueApi.Models;
using Q42.HueApi.Models.Groups;

namespace Functions.Hue
{
  public static class HueController
  {
    // Get values from https://developers.meethue.com/my-apps/
    static string _appId = Environment.GetEnvironmentVariable("HueAppId") ?? "azure";
    private static string _clientId = Environment.GetEnvironmentVariable("HueClientId");
    static string _clientSecret = Environment.GetEnvironmentVariable("HueClientSecret");
    static string _instanceId = Environment.GetEnvironmentVariable("HueInstanceId") ?? "PhilipsHue0";
    public static IRemoteAuthenticationClient _authClient = new RemoteAuthenticationClient(_clientId, _clientSecret, _appId);
    public static RemoteHueClient _hueClient;
    private static AccessTokenResponse _lastToken;

    [FunctionName("ResetAuthentication")]
    public static async Task<IActionResult> ResetAuthentication(
                [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
                [DurableClient] IDurableEntityClient client,
                ILogger log)
    {
      var entityId = new EntityId(nameof(HueState), _instanceId);
      await client.SignalEntityAsync<IHueState>(entityId, proxy => proxy.Delete());

      return new OkObjectResult("OAuth information cleared.");
    }

    [FunctionName("InitializeAuthentication")]
    public static IActionResult InitializeAuthentication(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
            [DurableClient] IDurableEntityClient client,
            ILogger log)
    {
      var authCodeUrl = _authClient.BuildAuthorizeUri("initialize", "azure_function", "Azure Function");
      var response = $"Visit '{authCodeUrl.AbsoluteUri}' to retrieve an authorization code.";
      log.LogWarning(response);

      return new RedirectResult(authCodeUrl.AbsoluteUri);
    }

    [FunctionName("RedeemCode")]
    public static async Task<IActionResult> RedeemCode(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            [DurableClient] IDurableEntityClient client,
            ILogger log)
    {
      var authCode = req.Query["code"];

      if (string.IsNullOrEmpty(authCode))
      {
        log.LogWarning("No auth code provided.");
        return new BadRequestResult();
      }
      
      var accessToken = await _authClient.GetToken(authCode);

      if (accessToken.Expires_in == 0)
      {
        throw new SystemException("Could not get token.");
      }

      var entityId = new EntityId(nameof(HueState), _instanceId);
      await client.SignalEntityAsync<IHueState>(entityId, proxy => proxy.SetAccessToken(accessToken));

      return new OkObjectResult("OAuth authorization flow completed.");
    }

    public static async Task<IActionResult> ConnectToBridge(
            HttpRequest req,
            IDurableEntityClient client,
            ILogger log)
    {
      var entityId = new EntityId(nameof(HueState), _instanceId);
      var state = await client.ReadEntityStateAsync<HueState>(entityId);

      if (!state.EntityExists)
      {
        log.LogWarning("No access token found. Run 'InitializeAuthentication' to start OAuth flow.");
        var result = InitializeAuthentication(req, client, log);
        return result;
      }

      var accessToken = await state.EntityState.GetAccessToken();

      // Get a new token
      if(accessToken.AccessTokenExpireTime() < DateTimeOffset.UtcNow.AddMinutes(-5))
      {
        accessToken = await _authClient.RefreshToken(accessToken.Refresh_token);

        if(accessToken.Expires_in == 0)
        {
          throw new SystemException("Could not refresh token.");
        }

        await client.SignalEntityAsync<IHueState>(entityId, proxy => proxy.SetAccessToken(accessToken));
      }

      _lastToken = accessToken;
      _authClient.Initialize(accessToken);
      _hueClient = new RemoteHueClient(GetValidToken);

      var bridges = await _hueClient.GetBridgesAsync();

      if (bridges == null)
      {
        log.LogWarning("No bridges found. Run 'InitializeAuthentication' to start OAuth flow.");
        var result = InitializeAuthentication(req, client, log);
        return result;
      }

      var appKey = await state.EntityState.GetAppKey();
      if (string.IsNullOrEmpty(appKey))
      {
        //Register app
        var bridgeId = bridges.First().Id;
        appKey = await _hueClient.RegisterAsync(bridgeId, "Azure Functions");
        await client.SignalEntityAsync<IHueState>(entityId, proxy => proxy.SetAppKey(appKey));
        log.LogInformation($"Your AppKey is '{appKey}' and your Bridge ID is '{bridgeId}'.");
      }
      else
      {
        //Or initialize with saved key:
        _hueClient.Initialize(bridges.First().Id, appKey);
      }

      // Test HueClient
      await _hueClient.GetCapabilitiesAsync();
      
      return new OkObjectResult("Connection to bridge established.");
    }

    [FunctionName("SetSensorState")]
    public static async Task<IActionResult> SetSensorState(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "SetSensorState/{state}")] HttpRequest req,
        string state,
        [DurableClient] IDurableEntityClient client,
        ILogger log)
    {
      if(_hueClient == null || !_hueClient.IsInitialized)
      {
        var connectionResult = await ConnectToBridge(req, client, log);
        if (connectionResult is RedirectResult)
        {
          return connectionResult;
        }
      }

      var results = new HueResults();

      // Begin of Hue controller logic

      var sensors = await _hueClient.GetSensorsAsync();

      var config = new SensorConfig()
      {
        On = "On".Equals(state, StringComparison.OrdinalIgnoreCase)
      };

      sensors
        .Where(s => s.ModelId == "SML001" && s.Type == "ZLLPresence")
        .ToList()
        .ForEach(s => results.AddRange(_hueClient.ChangeSensorConfigAsync(s.Id, config).Result));

      // End of Hue controller logic

      return results.Errors.Any() ? new BadRequestObjectResult(results.Errors) : new OkObjectResult("OK");
    }

    public static Task<string> GetValidToken()
    {
      return Task.FromResult<string>(_lastToken?.Access_token);
    }
  }
}