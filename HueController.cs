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

namespace Functions.Hue
{
  public static class HueController
  {
    // Get values from https://developers.meethue.com/my-apps/
    static string AppId = Environment.GetEnvironmentVariable("HueAppId");
    static string ClientId = Environment.GetEnvironmentVariable("HueClientId");
    static string ClientSecret = Environment.GetEnvironmentVariable("HueClientSecret");
    public static IRemoteAuthenticationClient AuthClient = new RemoteAuthenticationClient(ClientId, ClientSecret, AppId);
    public static IRemoteHueClient HueClient;

    [FunctionName("InitializeAuthentication")]
    public static async Task<IActionResult> InitializeAuthentication(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [DurableClient] IDurableEntityClient client,
            ILogger log)
    {
      var authCode = req.Query["code"];

      if (string.IsNullOrEmpty(authCode))
      {
        var authCodeUrl = AuthClient.BuildAuthorizeUri("initialize", "azure_function", "Azure Function");
        var response = $"Visit '{authCodeUrl.AbsoluteUri}' to retrieve an authorization code.";
        log.LogWarning(response);

        return new OkObjectResult(response);
      }
      else
      {
        var accessToken = await AuthClient.GetToken(authCode);
        var entityId = new EntityId(nameof(HueState), "PhilipsHue");
        await client.SignalEntityAsync<IHueState>(entityId, proxy => proxy.SetAccessToken(accessToken));

        return new OkObjectResult("Execution completed.");
      }
    }

    [FunctionName("ConnectToBridge")]
    public static async Task<IActionResult> ConnectToBridge(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [DurableClient] IDurableEntityClient client,
            ILogger log)
    {
      var entityId = new EntityId(nameof(HueState), "PhilipsHue");
      var state = await client.ReadEntityStateAsync<HueState>(entityId);
      var accessToken = await state.EntityState.GetAccessToken();

      AuthClient.Initialize(accessToken);
      HueClient = new RemoteHueClient(AuthClient.GetValidToken);

      var bridges = await HueClient.GetBridgesAsync();

      if (bridges != null)
      {
        var appKey = await state.EntityState.GetAppKey();
        if (string.IsNullOrEmpty(appKey))
        {
          //Register app
          var bridgeId = bridges.First().Id;
          var key = await HueClient.RegisterAsync(bridgeId, "Azure Functions");
          await client.SignalEntityAsync<IHueState>(entityId, proxy => proxy.SetAppKey(appKey));
          log.LogInformation($"Your AppKey is '{key}' and your Bridge ID is '{bridgeId}'.");
        }
        else
        {
          //Or initialize with saved key:
          HueClient.Initialize(bridges.First().Id, appKey);
        }

        try
        {
          await HueClient.GetCapabilitiesAsync();
        }
        catch (HueException)
        {
          return new UnauthorizedResult();
        }

        return new OkObjectResult("Execution completed.");
      }
      else
      {
        return new UnauthorizedResult();
      }
    }

    [FunctionName("TestLights")]
    public static async Task<IActionResult> TestLights(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
        ILogger log)
    {
      //Turn all lights on
      var lightResult = await HueClient.SendCommandAsync(new LightCommand().TurnOn());
      lightResult = await HueClient.SendCommandAsync(new LightCommand().TurnOff());

      return lightResult.Errors.Any() ? new BadRequestObjectResult(lightResult.Errors) : new OkObjectResult("OK");
    }
  }
}