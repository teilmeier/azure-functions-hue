using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Newtonsoft.Json;
using Q42.HueApi.Models;

namespace Functions.Hue
{
  [JsonObject(MemberSerialization.OptIn)]
  public class HueState : IHueState
  {
    [JsonProperty("AccessToken")]
    public AccessTokenResponse AccessToken { get; set; }
    [JsonProperty("AppKey")]
    public string AppKey { get; set; }
    public void SetAccessToken(AccessTokenResponse accessToken) => this.AccessToken = accessToken;
    public void SetAppKey(string appKey) => this.AppKey = appKey;
    public Task<AccessTokenResponse> GetAccessToken() => Task.FromResult(this.AccessToken);
    public Task<string> GetAppKey() => Task.FromResult(this.AppKey);
    public void Delete()
    {
      Entity.Current.DeleteState();
    }

    [FunctionName(nameof(HueState))]
    public static Task Run([EntityTrigger] IDurableEntityContext ctx)
        => ctx.DispatchAsync<HueState>();
  }
}